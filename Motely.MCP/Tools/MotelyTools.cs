using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using Motely.MCP.Search;

namespace Motely.MCP.Tools;

/// <summary>
/// The MCP tools. <c>validate_filter</c> is the same check behind <c>motely check</c> and the agent
/// hook; <c>search</c> runs the engine and returns matching seeds. Both are the raw SIMD power over
/// the wire.
/// </summary>
[McpServerToolType]
public static class MotelyTools
{
    [McpServerTool, Description("Check that a JAML filter loads and builds a search plan. Pass a filter name (in JamlFilters/) or a path.")]
    public static string ValidateFilter(string filter)
    {
        var path = FilterCheck.ResolvePath(filter);
        if (!File.Exists(path))
            return $"Error: no filter at {path}.";

        if (FilterCheck.TryCheck(File.ReadAllText(path), out var config, out _, out var error))
            return $"ok: {config.Name ?? "unnamed"} ({config.Deck} {config.Stake}) — "
                + $"{config.Must.Count} must, {config.Should.Count} should, {config.MustNot.Count} mustNot; "
                + "the search plan builds.";

        return $"Error: {error}";
    }

    [McpServerTool, Description("Run a search over a JAML filter and return matching seeds. Modes: replay (default), keyword <kw>, random <n>, sequential. Stops after collect matches (default 100, max 10,000).")]
    [McpAppUi(ResourceUri = "ui://motely/search")]
    public static async Task<string> Search(
        ClaimsPrincipal? user,
        string filter,
        string? mode = null,
        int? collect = null,
        CancellationToken cancellationToken = default
    )
    {
        var runner = new SearchRunner();
        var who = user?.Identity?.Name ?? "anonymous";
        var matches = new List<string>();
        long total = 0;
        long matched = 0;
        double rate = 0;
        TimeSpan elapsed = default;

        await foreach (var evt in runner.RunAsync(filter, mode, collect, cancellationToken))
        {
            switch (evt)
            {
                case SearchEvent.Match m:
                    matches.Add(m.Seed);
                    break;
                case SearchEvent.Done d:
                    total = d.TotalSearched;
                    matched = d.Matched;
                    rate = d.Rate;
                    elapsed = d.Elapsed;
                    break;
                case SearchEvent.Error e:
                    return $"Error: {e.Message}";
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            $"{filter}: {matched:N0} matched / {total:N0} searched in {FormatDuration(elapsed)} ({rate:N0} seeds/s) by {who}."
        );
        if (matches.Count > 0)
        {
            var shown = matches.Take(50);
            sb.AppendLine("Matches: " + string.Join(", ", shown));
            if (matches.Count > 50)
                sb.AppendLine($"… and {matches.Count - 50:N0} more.");
        }
        else
            sb.AppendLine("No seeds matched.");
        return sb.ToString();
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 24
            ? $"{span.Days} {(span.Days == 1 ? "day" : "days")} {span.Hours} {(span.Hours == 1 ? "hour" : "hours")}"
            : span.ToString(@"hh\:mm\:ss\.f");
}
