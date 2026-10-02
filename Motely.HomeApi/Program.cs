using System.Globalization;
using System.Text;
using ModelContextProtocol.AspNetCore;
using Motely.Distributed;
using Motely.HomeApi;
using Motely.Mcp;

// MotelyHome [--db motely.duckdb] [--urls http://0.0.0.0:35036]
//
// The queue every MotelyWorker on the LAN grinds. Queue a filter from the Claude app over MCP
// (/mcp), from curl (POST /filters), or anything that speaks HTTP; read the finds the same way.
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["urls"] is null)
    builder.WebHost.UseUrls("http://0.0.0.0:35036");
// Workers poll /claim every few seconds; one log line per request is noise.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

builder.Services.AddSingleton(new FilterQueue(builder.Configuration["db"] ?? "motely.duckdb"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, HomeJson.Default));
builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<MotelyTools>();
builder.Services.AddHostedService<Beacon>();

var app = builder.Build();

app.MapMcp("/mcp");

// Phone browser / curl.
app.MapGet("/", (FilterQueue queue) => Results.Text(Dashboard(queue.List())));
app.MapGet("/filters", (FilterQueue queue) => queue.List());
app.MapPost("/filters", async (HttpRequest request, FilterQueue queue, int batchChars = 4) =>
{
    string jaml = await new StreamReader(request.Body, Encoding.UTF8).ReadToEndAsync();
    try
    {
        return Results.Ok(queue.Add(jaml, batchChars));
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(ex.Message);
    }
});
app.MapGet("/filters/{slug}", (string slug, FilterQueue queue) =>
    queue.Status(slug) is { } status ? Results.Ok(status) : Results.NotFound());
app.MapGet("/filters/{slug}/seeds", (string slug, FilterQueue queue, int top = int.MaxValue) =>
    queue.Seeds(slug, top) is { } seeds ? Results.Text(seeds) : Results.NotFound());
app.MapDelete("/filters/{slug}", (string slug, FilterQueue queue) =>
    queue.Remove(slug) ? Results.Ok() : Results.NotFound());

// Workers.
app.MapGet("/claim", (string worker, FilterQueue queue) =>
    queue.Claim(worker) is { } work ? Results.Ok(work) : Results.NoContent());
app.MapPost("/done", (WorkDone done, FilterQueue queue) =>
    queue.Done(done) ? Results.Ok() : Results.NotFound());

app.Run();

static string Dashboard(FilterStatus[] filters)
{
    if (filters.Length == 0)
        return "No filters queued. POST a JAML to /filters, or queue_filter over MCP at /mcp.\n";
    var sb = new StringBuilder();
    foreach (var f in filters)
        sb.Append(CultureInfo.InvariantCulture,
            $"{f.Filter,-40} {f.Percent,6:0.00}% {f.Finds,6} finds  {(f.Complete ? "complete" : string.Join(" ", f.Workers))}\n");
    return sb.ToString();
}
