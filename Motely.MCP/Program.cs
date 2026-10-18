using Motely.MCP.Agent;
using ModelContextProtocol.Server;

// The filters directory: MOTELY_FILTERS_DIR (Aspire sets it on the mcp resource), else the repo's
// JamlFilters/. FilterCheck reads the variable, so it is set before any filter is resolved.
if (Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") is not { Length: > 0 } filtersDir)
    filtersDir = "JamlFilters";
Environment.SetEnvironmentVariable("MOTELY_FILTERS_DIR", filtersDir);

var builder = WebApplication.CreateBuilder(args);

// The MCP server over Streamable HTTP (stateless: no server-to-client requests, so no session
// affinity), with tools discovered from this assembly.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

// The AG-UI endpoint, backed by the deterministic agent registered as the IChatClient.
builder.Services.AddAGUI();
builder.Services.AddSingleton<IChatClient, MotelySearchAgent>();

// CORS for the Vite dev server's agent panel. Restrictive by origin, method and header.
builder.Services.AddCors(options =>
{
    options.AddPolicy("Web", policy =>
        policy
            .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .WithMethods("POST", "GET", "DELETE")
            .WithHeaders("Content-Type", "Authorization", "MCP-Protocol-Version", "Mcp-Session-Id")
            .WithExposedHeaders("Mcp-Session-Id"));
});

var app = builder.Build();

app.UseCors();
app.MapMcp("/mcp").RequireCors("Web");
app.MapAGUI("/agui").RequireCors("Web");

app.Run();
