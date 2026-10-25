using System.Text;
using Motely.MCP.Agui;
using Motely.MCP.Agent;
using Motely.MCP.Resources;
using Motely.MCP.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;

// The filters directory: MOTELY_FILTERS_DIR (Aspire sets it on the mcp resource), else the repo's
// JamlFilters/. FilterCheck reads the variable, so it is set before any filter is resolved.
if (Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") is not { Length: > 0 } filtersDir)
    filtersDir = "JamlFilters";
Environment.SetEnvironmentVariable("MOTELY_FILTERS_DIR", filtersDir);

var builder = WebApplication.CreateBuilder(args);

// The MCP server over Streamable HTTP (stateless: no server-to-client requests, so no session
// affinity), with tools from this assembly and the JAML guide + schema resources.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly()
    .WithResources<MotelyResources>();

// IHttpContextAccessor: the tools take a ClaimsPrincipal; the SDK's tool invoker resolves it from
// HttpContext, which needs this registration.
builder.Services.AddHttpContextAccessor();

// The server-level usage brief, returned on initialize so agents read it before tools/list, plus
// the tool-error filter that turns tool exceptions into corrective isError results.
builder.Services.Configure<McpServerOptions>(options =>
{
    options.ServerInstructions =
        """
        Motely: Balatro seed search over JAML filters.

        Seeds are 1-8 characters from 1-9 and A-Z; 0 is never used (it reads as O).

        The usual loop:
        1. filter_list — see the server's filters (narrow with `contains`), or write your own:
           the resource jaml://guide shows the format in a page, jaml://schema has every key.
        2. seed_validate — check the filter: `filter` (a name from filter_list, or an absolute
           path) or `jaml` (your filter's YAML text; nothing needs saving).
        3. seed_search — find seeds. To find NEW seeds use mode random + randomCount, or
           sequential + collect (continue a sweep with the returned nextBatch as startBatch).
           replay runs the filter's saved seeds; seeds + seeds checks a list; keyword + keyword
           searches a seed word's padded space. Exactly one mode with its matching field.
        4. seed_analyze — read what a matched seed actually contains, ante by ante, before
           describing it. Pass the filter's deck and stake.

        Auth: a local server (Development, no Mcp:Authority) needs no token; just call the tools.
        A deployed server requires a bearer token from its configured authority.
        """;

    options.Filters ??= new McpServerFilters();
    options.Filters.Request ??= new McpRequestFilters();
    options.Filters.Request.CallToolFilters.Add(ToolErrorFilter.Filter);
});

// The AG-UI endpoint, backed by the deterministic agent registered as the IChatClient.
builder.Services.AddAGUI();
builder.Services.AddSingleton<IChatClient, MotelySearchAgent>();

// EXT-Auth: JWT Bearer + RFC 9728 protected-resource metadata, following the SDK's
// ProtectedMcpServer sample. With Mcp:Authority configured, tokens are validated against that
// issuer (production). Without one, a local symmetric key stands in so the server runs out of the
// box, and POST /dev/token mints a dev token.
var mcpConfig = builder.Configuration.GetSection("Mcp");
var authority = mcpConfig["Authority"];
var audience = mcpConfig["Audience"] ?? "motely-mcp";
var signingKey = mcpConfig["DevSigningKey"] ?? "motely-dev-signing-key-32-bytes!!";
var allowDevToken = mcpConfig.GetValue<bool?>("AllowDevTokenIssuer") ?? builder.Environment.IsDevelopment();
// Secure by default: require a token in Production, or whenever an authority is configured. In
// Development with no authority, the server is open so the editor and web panel work out of the
// box; POST /dev/token still mints a token if you want to exercise the auth flow.
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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
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
