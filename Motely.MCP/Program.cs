using System.Runtime.InteropServices;
using Motely.MCP;

// Motely.MCP: the engine as an MCP server for the coding agents on this machine.
//   dotnet run --project Motely.MCP               stdin/stdout (a "command" server in Claude Code, Cursor, ...)
//   dotnet run --project Motely.MCP -- --port N   Streamable HTTP on http://localhost:N/mcp (what Aspire runs)
// ConnectionStrings__db (Aspire sets it; Neon in production) turns on the seed database: every
// search_seeds result is saved, and saved_seeds / list_filters read it back. Without it the tools
// still run and nothing is kept.

int? port = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed))
    {
        port = parsed;
        i++;
        continue;
    }
    Console.Error.WriteLine($"Usage: Motely.MCP [--port N]  (unexpected argument '{args[i]}')");
    return 1;
}

using var cts = new CancellationTokenSource();

// Aspire stops a resource with SIGTERM (SIGINT from a terminal); the first one ends the server cleanly.
void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    cts.Cancel();
}

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);

return await MotelyMcp.RunAsync(port, cts.Token);
