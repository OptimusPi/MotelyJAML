using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;
using Motely.Gpu;
using Motely.MCP.Agent;
using Motely.MCP.Agui;
using Motely.MCP.Data;
using Motely.MCP.Party;
using Motely.MCP.Resources;
using Motely.MCP.Search;
using Motely.MCP.Tools;

// The filters directory: MOTELY_FILTERS_DIR (Aspire sets it on the mcp resource), else the repo's
// JamlFilters/. FilterCheck reads the variable, so it is set before any filter is resolved.
if (Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") is not { Length: > 0 } filtersDir)
    filtersDir = "JamlFilters";
Environment.SetEnvironmentVariable("MOTELY_FILTERS_DIR", filtersDir);

using var telemetry = Motely.ServiceDefaults.Extensions.AddServiceDefaults(
    [.. Motely.SearchTelemetry.Sources, Motely.SearchTelemetry.ActivitySourceName],
    [.. Motely.SearchTelemetry.Sources, Motely.SearchTelemetry.MeterName]
);

var builder = WebApplication.CreateBuilder(args);

string dataDir =
    builder.Configuration["Motely:DataDir"]
    ?? Environment.GetEnvironmentVariable("MOTELY_DATA_DIR")
    ?? Path.Combine(builder.Environment.ContentRootPath, ".motely-data");

var fileStore = new FileMotelyStore(dataDir);
var connStr = builder.Configuration.GetConnectionString("motelydb");
IMotelyStore store = !string.IsNullOrWhiteSpace(connStr)
    ? new PostgresMotelyStore(connStr, dataDir)
    : fileStore;

var partyCoordinator = new PartyCoordinator(store, filtersDir);
var searchRunner = new SearchRunner(filtersDir, store);

builder.Services.AddSingleton(store);
builder.Services.AddSingleton(partyCoordinator);
builder.Services.AddSingleton(searchRunner);

// The MCP server over Streamable HTTP (stateless: no server-to-client requests, so no session
// affinity), with optional --stdio transport, tools from this assembly, and JAML resources.
var mcpServerBuilder = builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly()
    .WithResources<MotelyResources>();

if (args.Contains("--stdio", StringComparer.OrdinalIgnoreCase))
{
    mcpServerBuilder.WithStdioServerTransport();
}

// IHttpContextAccessor: the tools take a ClaimsPrincipal; the SDK's tool invoker resolves it from
// HttpContext, which needs this registration.
builder.Services.AddHttpContextAccessor();

// The server-level usage brief, returned on initialize so agents read it before tools/list, plus
// the tool-error filter that turns tool exceptions into corrective isError results.
builder.Services.Configure<McpServerOptions>(options =>
{
    options.ServerInstructions =
        """
        Motely: Balatro seed search over JAML filters (SIMD CPU + OpenCL FP64 GPU + Community Party Search).

        Seeds are 1-8 characters from 1-9 and A-Z; 0 is never used (it reads as O).

        The usual loop:
        1. filter_list — see the server's filters (narrow with `contains`), or write your own:
           the resource jaml://guide shows the format in a page, jaml://schema has every key.
        2. seed_validate — check the filter: `filter` (a name from filter_list, or an absolute
           path) or `jaml` (your filter's JAML text; nothing needs saving).
        3. seed_search — find seeds. To find NEW seeds use mode random + randomCount, or
           sequential + collect (with optional `useGpu: true`).
        4. seed_analyze / seed_analyze_batch / seed_locate — read what matched seeds contain.
        5. gpu_status, party_create, party_sweep_gpu, party_status, leaderboard, recent_finds —
           inspect GPU hardware, run community party sweeps, and query persisted discoveries.
        """;

    options.Filters ??= new McpServerFilters();
    options.Filters.Request ??= new McpRequestFilters();
    options.Filters.Request.CallToolFilters.Add(ToolErrorFilter.Filter);
});

// The AG-UI endpoint, backed by the deterministic agent registered as the IChatClient.
builder.Services.AddAGUI();
builder.Services.AddSingleton<IChatClient, MotelySearchAgent>();

// EXT-Auth: JWT Bearer + RFC 9728 protected-resource metadata, following the SDK's
// ProtectedMcpServer sample.
var mcpConfig = builder.Configuration.GetSection("Mcp");
var authority = mcpConfig["Authority"];
var audience = mcpConfig["Audience"] ?? "motely-mcp";
var signingKey = mcpConfig["DevSigningKey"] ?? "motely-dev-signing-key-32-bytes!!";
var allowDevToken = mcpConfig.GetValue<bool?>("AllowDevTokenIssuer") ?? builder.Environment.IsDevelopment();
var requireAuth = !string.IsNullOrEmpty(authority) || !builder.Environment.IsDevelopment();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        if (!string.IsNullOrEmpty(authority))
        {
            options.Authority = authority;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidAudience = audience,
                ValidIssuer = authority
            };
        }
        else
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidIssuer = "motely-dev",
                ValidAudience = audience,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true
            };
        }
    })
    .AddMcp(options =>
    {
        options.ResourceMetadata = new()
        {
            ResourceDocumentation = "https://github.com/OptimusPi/MotelyJAML#mcp-server",
            AuthorizationServers = { authority ?? "local-dev" },
            ScopesSupported = ["mcp:tools"]
        };
    });

builder.Services.AddAuthorization();

// CORS for the Vite dev server's agent panel. Restrictive by origin, method and header.
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "Web",
        policy =>
            policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .WithMethods("POST", "GET", "DELETE")
                .WithHeaders("Content-Type", "Authorization", "MCP-Protocol-Version", "Mcp-Session-Id")
                .WithExposedHeaders("Mcp-Session-Id")
    );
});

var app = builder.Build();

await store.InitializeAsync(filtersDir);
await partyCoordinator.EnsureLoadedAsync();
MotelyTools.Configure(store, partyCoordinator, searchRunner);

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/alive", () => Results.Ok(new { status = "healthy" }));

app.MapGet(
    "/",
    () =>
    {
        var devices = GpuDeviceInfo.DiscoverDevices();
        return Results.Ok(
            new
            {
                service = "Motely.MCP",
                gpu = new
                {
                    deviceCount = devices.Count,
                    bestDevice = GpuDeviceInfo.GetBestDevice()?.Name,
                    devices
                },
                database = store.GetType().Name,
                endpoints = new[]
                {
                    "/mcp",
                    "/agui",
                    "/health",
                    "/alive",
                    "/api/party/create",
                    "/api/party/list",
                    "/api/party/{partyId}",
                    "/api/party/next",
                    "/api/party/report",
                    "/api/party/{partyId}/sweep"
                }
            }
        );
    }
);

app.MapPost(
    "/api/party/create",
    async (
        HttpContext httpContext,
        PartyCoordinator coordinator,
        string? filter,
        string? name,
        string? deck,
        string? stake,
        int? batchChars,
        long? blockSize,
        long? totalBlocks,
        int? targetMatches,
        CancellationToken ct
    ) =>
    {
        CreatePartyRequest? req = null;
        if (httpContext.Request.ContentLength is > 0 && httpContext.Request.HasJsonContentType())
        {
            req = await httpContext.Request.ReadFromJsonAsync<CreatePartyRequest>(ct).ConfigureAwait(false);
        }

        req = new CreatePartyRequest(
            FilterOrJaml: req?.FilterOrJaml ?? filter ?? "",
            Name: req?.Name ?? name,
            Deck: req?.Deck ?? deck,
            Stake: req?.Stake ?? stake,
            BatchChars: req?.BatchChars is > 0 ? req.BatchChars : (batchChars ?? 3),
            BlockSize: req?.BlockSize is > 0 ? req.BlockSize : (blockSize ?? 10),
            TotalBlocks: req?.TotalBlocks ?? totalBlocks,
            TargetMatches: req?.TargetMatches ?? targetMatches
        );

        if (string.IsNullOrWhiteSpace(req.FilterOrJaml))
            return Results.BadRequest(new { error = "FilterOrJaml (or ?filter=...) is required." });
        var status = await coordinator.CreatePartyAsync(req, ct).ConfigureAwait(false);
        return Results.Ok(status);
    }
);

app.MapGet(
    "/api/party/list",
    async (PartyCoordinator coordinator, CancellationToken ct) =>
        Results.Ok(await coordinator.ListPartiesAsync(ct).ConfigureAwait(false))
);

app.MapGet(
    "/api/party/{partyId}",
    async (string partyId, PartyCoordinator coordinator, CancellationToken ct) =>
    {
        var status = await coordinator.GetPartyStatusAsync(partyId, ct).ConfigureAwait(false);
        return status is not null ? Results.Ok(status) : Results.NotFound(new { error = $"Party '{partyId}' not found." });
    }
);

app.MapGet(
    "/api/party/next",
    async (string? partyId, long? blocks, PartyCoordinator coordinator, CancellationToken ct) =>
    {
        var next = await coordinator.NextLeaseAsync(partyId, blocks, ct).ConfigureAwait(false);
        return Results.Ok(next);
    }
);

app.MapPost(
    "/api/party/report",
    async (PartyReportRequestDto payload, PartyCoordinator coordinator, CancellationToken ct) =>
    {
        var (statusCode, body) = await coordinator.ReportAsync(payload, ct).ConfigureAwait(false);
        return Results.Json(body, statusCode: statusCode);
    }
);

app.MapPost(
    "/api/party/{partyId}/sweep",
    async (string partyId, long? blocks, bool? gpu, int? threads, PartyCoordinator coordinator, CancellationToken ct) =>
    {
        var result = await coordinator
            .SweepBlocksAsync(partyId, blocks ?? 10, gpu ?? true, threads, ct)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }
);

var mcp = app.MapMcp("/mcp");
mcp.RequireCors("Web");
if (requireAuth)
    mcp.RequireAuthorization();
app.MapAGUI("/agui").RequireCors("Web");

// Dev token issuer: mint a local token so a client can connect without an external IdP.
if (allowDevToken && string.IsNullOrEmpty(authority))
{
    app.MapPost("/dev/token", () => new { token = Motely.MCP.DevTokenIssuer.Mint(signingKey, audience) });
}

app.Run();
