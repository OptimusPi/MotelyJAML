using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motely.MCP;

// motely-mcp: the Motely seed engine as an MCP server.
//   motely-mcp           stdio, for a client on this machine. No auth: only the parent process can talk.
//   motely-mcp --http    Streamable HTTP on http://127.0.0.1:3141/mcp, an OAuth resource server per
//                        the 2026-07-28 MCP authorization spec. Settings (args or Mcp__* env vars):
//     --Mcp:AuthServer=https://login.example.com   token issuer; required unless bound to loopback
//     --Mcp:Resource=https://motely.example.com/mcp  this server's canonical URI (token audience)
//     --Mcp:Scope=motely                            scope every request needs (default: motely)
//     --urls=http://0.0.0.0:3141                    where to listen

const string Instructions =
    "Motely searches Balatro seeds with JAML filters. motely_search starts a search and returns at once; "
    + "poll motely_status for progress and the best seeds; motely_stop ends it; motely_analyze shows a seed ante by ante.";

if (!args.Contains("--http"))
{
    var host = Host.CreateApplicationBuilder(args);
    // stdout is the MCP channel; logs go to stderr.
    host.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    var tools = new MotelyTools(new Searches(), allowPaths: true);
    host.Services.AddMcpServer(Configure).WithStdioServerTransport().WithMotely(tools);
    await host.Build().RunAsync();
    return 0;
}

var builder = WebApplication.CreateSlimBuilder(args);
// Telemetry, health checks and resilience for the Aspire dashboard; OTLP export only when Aspire
// (or anyone) sets OTEL_EXPORTER_OTLP_ENDPOINT.
builder.AddServiceDefaults();
var config = builder.Configuration;
string urls = config["urls"] ?? "http://127.0.0.1:3141";
builder.WebHost.UseUrls(urls);
string? authServer = config["Mcp:AuthServer"];
string scope = config["Mcp:Scope"] ?? "motely";

if (authServer is null)
{
    // No token issuer means no auth, which is only acceptable when nothing off this machine can connect.
    if (!urls.Split(';').All(IsLoopback))
    {
        Console.Error.WriteLine("Error: --http on a non-loopback address needs --Mcp:AuthServer and --Mcp:Resource.");
        return 1;
    }
    Console.Error.WriteLine($"motely-mcp: no --Mcp:AuthServer, serving {urls} without auth (loopback only).");
}
else
{
    string resource =
        config["Mcp:Resource"]
        ?? throw new InvalidOperationException("--Mcp:Resource (this server's canonical URI) is required with --Mcp:AuthServer.");
    builder
        .Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(o =>
        {
            // Issuer, signature, lifetime and audience: the token must have been minted for this server.
            o.Authority = authServer;
            o.Audience = resource;
            o.MapInboundClaims = false;
        })
        .AddMcp(o =>
            o.ResourceMetadata = new ProtectedResourceMetadata
            {
                Resource = resource,
                AuthorizationServers = [authServer],
                ScopesSupported = [scope],
                ResourceName = "Motely",
            }
        );
    builder.Services.AddAuthorizationBuilder().AddPolicy("motely", p => p.RequireAuthenticatedUser().RequireAssertion(c => HasScope(c.User, scope)));
    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler>(new InsufficientScope(scope));
}

var httpTools = new MotelyTools(new Searches(), allowPaths: false);
// Stateless: search state lives in Searches, not in an MCP session, so any request can poll any search.
builder.Services.AddMcpServer(Configure).WithHttpTransport(o => o.Stateless = true).WithMotely(httpTools);

var app = builder.Build();
app.MapDefaultEndpoints();
if (authServer is not null)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMcp("/mcp").RequireAuthorization("motely");
}
else
    app.MapMcp("/mcp");
await app.RunAsync();
return 0;

static void Configure(McpServerOptions o)
{
    o.ServerInfo = new Implementation { Name = "motely", Title = "Motely seed search", Version = ThisAssembly.Version };
    o.ServerInstructions = Instructions;
}

static bool IsLoopback(string url) =>
    Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
    && (uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip)));

// OAuth puts granted scopes in "scope" (space-separated); Entra uses "scp".
static bool HasScope(ClaimsPrincipal user, string scope) =>
    user.FindAll("scope").Concat(user.FindAll("scp")).SelectMany(static c => c.Value.Split(' ')).Contains(scope);

static class Registration
{
    public static IMcpServerBuilder WithMotely(this IMcpServerBuilder builder, MotelyTools tools) =>
        builder
            .WithListToolsHandler((_, _) => ValueTask.FromResult(tools.ListTools()))
            .WithCallToolHandler((ctx, _) => ValueTask.FromResult(tools.Call(ctx.Params!)))
            .WithListResourcesHandler((_, _) => ValueTask.FromResult(MotelyTools.ListResources()))
            .WithReadResourceHandler((ctx, _) => ValueTask.FromResult(MotelyTools.ReadResource(ctx.Params!.Uri)));
}

/// <summary>
/// A valid token without the scope gets 403 plus the step-up challenge the spec asks for, so the
/// client knows which scope to request instead of looping on a bare 403.
/// </summary>
sealed class InsufficientScope(string scope) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (!result.Forbidden)
            return _default.HandleAsync(next, context, policy, result);
        var metadata = $"{context.Request.Scheme}://{context.Request.Host}/.well-known/oauth-protected-resource";
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.WWWAuthenticate =
            $"Bearer error=\"insufficient_scope\", scope=\"{scope}\", resource_metadata=\"{metadata}\"";
        return Task.CompletedTask;
    }
}

static class ThisAssembly
{
    public static string Version => typeof(MotelyTools).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
