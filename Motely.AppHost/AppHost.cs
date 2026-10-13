// Motely under Aspire. `aspire run` (or `dotnet run --project Motely.AppHost`) starts Motely's
// pieces behind one dashboard, with their console logs, traces and metrics:
//
//   db       The seed database. Neon when `ConnectionStrings:db` is configured (user secrets:
//            `dotnet user-secrets set ConnectionStrings:db "<Neon connection string>"`, or the
//            environment); otherwise a local Postgres container with a named volume, so a clean
//            clone runs with Docker alone.
//   check    `motely check` over YamlFilters/. Runs once: Finished when every filter loads, the
//            failures in its console log when one doesn't.
//   mcp      Motely.MCP over Streamable HTTP, marked as an MCP server. Aspire hands its tools
//            (validate_filter, search_seeds, saved_seeds, list_filters, analyze_seeds, list_names)
//            to coding agents through `aspire agent mcp`, and `aspire mcp call mcp <tool> --input
//            '{...}'` calls one directly. Every search's finds go to db.
//   search   `motely --yaml <filter>`, started by hand from the dashboard or with
//            `aspire resource search start`. The `filter` parameter picks the filter; its progress
//            is on the Metrics page (motely.search.*) and its seeds are in the console log.
//   web      Motely.Web's Vite dev server: jaml-ui's SeedLab on the motely-wasm package (publish
//            Motely.Wasm first; Motely.Web links it with file:../Motely.Wasm).
//
// Resources run in their own project directory, not the repo root, so each one that takes a
// filter by name gets the repo's YamlFilters/ through MOTELY_FILTERS_DIR.

using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var filters = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "YamlFilters"));

// Neon (or any Postgres) by connection string, else a container. Either way the MCP server sees
// ConnectionStrings__db; the container is waited for, Neon is simply there.
IResourceBuilder<IResourceWithConnectionString> db;
IResourceBuilder<IResource>? dbReady = null;
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("db")))
    db = builder.AddConnectionString("db");
else
{
    var postgres = builder.AddPostgres("postgres").WithDataVolume("motely-postgres");
    var database = postgres.AddDatabase("db");
    db = database;
    dbReady = database;
}

builder.AddProject<Projects.Motely_CLI>("check").WithArgs("check", filters);

var mcp = builder
    .AddProject<Projects.Motely_MCP>("mcp")
    .WithHttpEndpoint()
    .WithEnvironment("MOTELY_FILTERS_DIR", filters)
    .WithReference(db)
    .WithHttpHealthCheck("/health");
if (dbReady is not null)
    mcp.WaitFor(dbReady);
// The server binds the endpoint's target port; Aspire's proxy owns the public one.
mcp.WithArgs("--port", mcp.GetEndpoint("http").Property(EndpointProperty.TargetPort));
#pragma warning disable ASPIREMCP001 // Resource MCP servers are experimental.
mcp.WithMcpServer("/mcp");
#pragma warning restore ASPIREMCP001

// A bare name means YamlFilters/<name>.yaml; a path is used as given. The filter's own root
// `cutoff:` applies, as it does on the command line.
var filter = builder
    .AddParameter("filter")
    .WithDescription("The filter `search` runs: a name in YamlFilters/ (no extension) or a path to one.");

builder
    .AddProject<Projects.Motely_CLI>("search")
    // --quiet: the progress line goes to the Metrics page, not one log line every 800 ms.
    .WithArgs("--yaml", filter, "--quiet")
    .WithEnvironment("MOTELY_FILTERS_DIR", filters)
    .WithExplicitStart();

builder.AddViteApp("web", "../Motely.Web");

builder.Build().Run();
