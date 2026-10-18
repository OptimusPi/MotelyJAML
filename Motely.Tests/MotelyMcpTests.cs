using System.Text;
using Microsoft.Extensions.AI;
using Motely.MCP.Agent;
using Motely.MCP.Search;
using Motely.MCP.Tools;

namespace Motely.Tests;

/// <summary>
/// The MCP server and AG-UI endpoint's deterministic core: the search runner, the tools, and the
/// agent. Every test is deterministic by construction (no model, no clock, no randomness), so a
/// given request always yields the same run.
/// </summary>
public sealed class MotelyMcpTests
{
    private static string RepoFilters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Motely.slnx")))
            dir = dir.Parent;
        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Motely.slnx not found above the test output."),
            "JamlFilters"
        );
    }

    // The runner resolves filters through MOTELY_FILTERS_DIR; point it at the repo's. A module
    // initializer (not a static constructor) so it is set before any test collection — including the
    // Aspire AppHost tests, which change the working directory — runs.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void SetFiltersDir() =>
        Environment.SetEnvironmentVariable("MOTELY_FILTERS_DIR", RepoFilters());

    [Fact]
    public async Task SearchRunner_replays_a_filter_deterministically()
    {
        var (matched1, searched1) = await RunAsync("longCola", "replay");
        var (matched2, searched2) = await RunAsync("longCola", "replay");

        Assert.True(searched1 > 0);
        Assert.Equal(searched1, searched2);
        Assert.Equal(matched1, matched2);
    }

    [Fact]
    public void Diagnostic_full()
    {
        var filtersDir = Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR");
        var combined = Path.Combine(filtersDir ?? "JamlFilters", "longCola.yaml");
        File.AppendAllText(
            @"C:\Users\pifre\AppData\Local\Temp\opencode\mcpdiag.txt",
            $"filtersDir={filtersDir} | combined={combined} | exists={File.Exists(combined)} | cwd={Environment.CurrentDirectory} | base={AppContext.BaseDirectory}\n"
        );
    }

    [Fact]
    public void ValidateFilter_accepts_a_repo_filter()
    {
        Assert.StartsWith("ok:", MotelyTools.ValidateFilter("boss-the-eye"));
    }

    [Fact]
    public void ValidateFilter_rejects_a_missing_filter()
    {
        Assert.StartsWith("Error:", MotelyTools.ValidateFilter("no-such-filter"));
    }

    [Fact]
    public async Task Search_tool_reports_matches()
    {
        var result = await MotelyTools.Search(null!, "longCola", "replay", null);
        File.WriteAllText(@"C:\Users\pifre\AppData\Local\Temp\opencode\result.txt", result);
        Assert.Contains("matched", result);
    }

    [Fact]
    public async Task Agent_streams_a_deterministic_run()
    {
        var first = await RunAgentAsync("search longCola");
        var second = await RunAgentAsync("search longCola");

        Assert.Contains("Searching longCola", first);
        Assert.Contains("matched", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Agent_reports_a_missing_filter()
    {
        var output = await RunAgentAsync("search no-such-filter-xyz");
        Assert.Contains("no filter", output);
    }

    [Fact]
    public async Task Agent_asks_which_filter_when_the_message_is_empty()
    {
        var output = await RunAgentAsync("");
        Assert.Contains("Tell me which JAML filter", output);
    }

    private static async Task<(int Matched, long Searched)> RunAsync(string filter, string? mode)
    {
        var runner = new SearchRunner();
        int matched = 0;
        long searched = 0;
        await foreach (var evt in runner.RunAsync(filter, mode, null))
        {
            switch (evt)
            {
                case SearchEvent.Match:
                    matched++;
                    break;
                case SearchEvent.Done d:
                    searched = d.TotalSearched;
                    break;
            }
        }
        return (matched, searched);
    }

    private static async Task<string> RunAgentAsync(string message)
    {
        var agent = new MotelySearchAgent();
        var sb = new StringBuilder();
        await foreach (
            var update in agent.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, message)])
        )
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent text)
                    sb.Append(text.Text);
            }
        }
        return sb.ToString();
    }
}
