using System.ComponentModel;
using Motely.Distributed;
using ModelContextProtocol.Server;

namespace Motely.Mcp;

/// <summary>The home queue as MCP tools: queue a JAML from your phone, watch it, read the finds.</summary>
[McpServerToolType]
public sealed class MotelyTools(FilterQueue queue)
{
    [McpServerTool(Name = "queue_filter"), Description(
        "Queue a JAML filter for every MotelyWorker on the network to grind. The filter's name: is its id (slugged). "
        + "Queuing the same JAML again keeps its progress.")]
    public FilterStatus QueueFilter(
        [Description("The full JAML document.")] string jaml,
        [Description("Seeds per batch = 35^batchChars; 1-7, default 4.")] int batchChars = 4
    ) => queue.Add(jaml, batchChars);

    [McpServerTool(Name = "list_filters"), Description("Every queued filter: percent done, finds, and which workers are on it.")]
    public FilterStatus[] ListFilters() => queue.List();

    [McpServerTool(Name = "get_filter"), Description("One filter's progress.")]
    public FilterStatus GetFilter([Description("The filter id (slug).")] string filter) =>
        queue.Status(filter) ?? throw new ArgumentException($"No filter '{filter}'.");

    [McpServerTool(Name = "get_seeds"), Description("A filter's finds, best score first, as seed,score,worker lines.")]
    public string GetSeeds(
        [Description("The filter id (slug).")] string filter,
        [Description("At most this many seeds; default 100.")] int top = 100
    ) => queue.Seeds(filter, top) ?? throw new ArgumentException($"No filter '{filter}'.");

    [McpServerTool(Name = "remove_filter"), Description("Stop a filter and delete its progress and finds.")]
    public string RemoveFilter([Description("The filter id (slug).")] string filter) =>
        queue.Remove(filter) ? $"Removed '{filter}'." : throw new ArgumentException($"No filter '{filter}'.");
}
