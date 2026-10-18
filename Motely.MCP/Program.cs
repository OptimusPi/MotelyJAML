using System.Text;
using Motely.MCP.Agui;
using Motely.MCP.Agent;
using Motely.MCP.Resources;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Extensions.Apps;

// The filters directory: MOTELY_FILTERS_DIR (Aspire sets it on the mcp resource), else the repo's
// JamlFilters/. FilterCheck reads the variable, so it is set before any filter is resolved.
if (Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") is not { Length: > 0 } filtersDir)
    filtersDir = "JamlFilters";
Environment.SetEnvironmentVariable("MOTELY_FILTERS_DIR", filtersDir);

var builder = WebApplication.CreateBuilder(args);

// The MCP server over Streamable HTTP (stateless: no server-to-client requests, so no session
// affinity), with tools from this assembly, the MCP Apps UI resource, and MCP Apps support.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly()
    .WithResources<MotelyResources>()
    .WithMcpApps();

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
            ResourceDocumentation = "https://docs.motely.dev/mcp",
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
app.MapMcp("/mcp").RequireAuthorization().RequireCors("Web");
app.MapAGUI("/agui").RequireCors("Web");

// Dev token issuer: mint a local token so a client can connect without an external IdP.
if (allowDevToken && string.IsNullOrEmpty(authority))
{
    app.MapPost("/dev/token", () => new { token = Motely.MCP.DevTokenIssuer.Mint(signingKey, audience) });
}

app.Run();
