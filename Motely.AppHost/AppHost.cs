// Motely under Aspire. `aspire run` (or `dotnet run --project Motely.AppHost`) brings up every
// long-running piece with one dashboard for logs, traces and metrics:
//
//   motely-mcp    Motely.MCP in --http mode on http://localhost:3141/mcp. Loopback only and no
//                 auth, which the server allows only because nothing off this machine can reach
//                 it. Point an MCP client (or the MCP Inspector) at that URL.

var builder = DistributedApplication.CreateBuilder(args);

builder
    .AddProject<Projects.Motely_MCP>("motely-mcp")
    .WithArgs("--http")
    // 3141 is the port the server binds on its own; Aspire hands it in as ASPNETCORE_URLS.
    .WithHttpEndpoint(port: 3141, isProxied: false)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
