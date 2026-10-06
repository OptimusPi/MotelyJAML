using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Motely.MCP;

var builder = Host.CreateApplicationBuilder(args);
// stdout is the MCP channel; logs go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<Parties>();
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<PartyTools>();
await builder.Build().RunAsync();
