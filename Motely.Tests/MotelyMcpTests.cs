using System.Reflection;
using System.Text;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Motely.MCP.Agent;
using Motely.MCP.Search;
using Motely.MCP.Tools;

namespace Motely.Tests;

/// <summary>
/// The MCP server and AG-UI endpoint's deterministic core: the search runner, the tools, and the
/// agent. Every test is deterministic by construction (no model, no clock, no randomness), so a
/// given request always yields the same run. random mode is the one engine input that is
/// nondeterministic per call, so no test asserts on its specific draws.
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
        var (matched1, searched1) = await RunAsync("Zerkeo_Pure", "replay");
        var (matched2, searched2) = await RunAsync("Zerkeo_Pure", "replay");

        Assert.True(searched1 > 0);
        Assert.Equal(searched1, searched2);
        Assert.Equal(matched1, matched2);
    }

    [Fact]
    public void ValidateFilter_accepts_a_repo_filter()
    {
        Assert.StartsWith("ok:", MotelyTools.ValidateFilter("boss-the-eye"));
    }

    [Fact]
    public void ValidateFilter_rejects_a_missing_filter()
    {
        // A missing filter is a tool-execution error, not an "ok"-shaped answer: the tool-error
        // filter turns the throw into an isError result whose text is the same "Error: ..." line.
        var ex = Assert.Throws<InvalidOperationException>(() => MotelyTools.ValidateFilter("no-such-filter"));
        Assert.StartsWith("Error:", ex.Message);
    }

    [Fact]
    public async Task Search_tool_reports_matches()
    {
        var result = await MotelyTools.Search("Zerkeo_Pure");

        Assert.Equal("Zerkeo_Pure", result.Filter);
        Assert.Equal("replay", result.Mode);
        Assert.True(result.Searched > 0);
        Assert.True(result.Matched > 0);
        Assert.Equal(result.Matched, result.Matches.Count);
        Assert.True(result.SeedsPerSecond > 0);
        Assert.Equal("anonymous", result.RequestedBy);
    }

    [Fact]
    public async Task Search_tool_replays_deterministically()
    {
        var first = await MotelyTools.Search("Zerkeo_Pure");
        var second = await MotelyTools.Search("Zerkeo_Pure");

        Assert.Equal(first.Matched, second.Matched);
        Assert.Equal(first.Searched, second.Searched);
        Assert.Equal(first.Matches, second.Matches);
    }

    [Fact]
    public async Task Search_tool_seeds_mode_checks_exactly_the_list()
    {
        // One seed in, one searched: proves the structured seeds field mapped onto the runner's
        // "seeds <list>" mode rather than replay or sequential.
        var result = await MotelyTools.Search("Zerkeo_Pure", SeedSearchMode.Seeds, seeds: "A1B2C3D4");

        Assert.Equal("seeds", result.Mode);
        Assert.Equal(1, result.Searched);
        Assert.InRange(result.Matched, 0, 1);
    }

    [Theory]
    // Each mode maps onto exactly the SearchRunner mode string the AG-UI agent path also uses.
    [InlineData(SeedSearchMode.Replay, null, null, null, "replay")]
    [InlineData(SeedSearchMode.Seeds, "A1B2C3D4,E5F6G7H8", null, null, "seeds A1B2C3D4,E5F6G7H8")]
    [InlineData(SeedSearchMode.Keyword, null, "DEAL", null, "keyword DEAL")]
    [InlineData(SeedSearchMode.Random, null, null, 1000, "random 1000")]
    [InlineData(SeedSearchMode.Sequential, null, null, null, "sequential")]
    public void Search_input_maps_each_mode_to_its_runner_mode_string(
        SeedSearchMode mode,
        string? seeds,
        string? keyword,
        int? randomCount,
        string expectedRunnerMode
    )
    {
        var source = MotelyTools.ValidateModeCombo(mode, seeds, keyword, randomCount);
        Assert.Equal(expectedRunnerMode, source.RunnerMode);
    }

    [Theory]
    // A source field with the wrong mode: the error must name the problem and the valid combos.
    [InlineData(SeedSearchMode.Replay, "A1B2C3D4", null, null, "takes no seed source")]
    [InlineData(SeedSearchMode.Sequential, null, null, 10, "takes no seed source")]
    [InlineData(SeedSearchMode.Seeds, null, null, null, "requires 'seeds'")]
    [InlineData(SeedSearchMode.Seeds, "A1B2C3D4", "DEAL", null, "does not take 'keyword'")]
    [InlineData(SeedSearchMode.Keyword, "A1B2C3D4", "DEAL", null, "does not take 'seeds'")]
    [InlineData(SeedSearchMode.Keyword, null, null, null, "requires 'keyword'")]
    [InlineData(SeedSearchMode.Random, null, "DEAL", 10, "does not take 'keyword'")]
    [InlineData(SeedSearchMode.Random, null, null, null, "requires 'randomCount'")]
    public void Search_input_rejects_invalid_combos_with_a_corrective_error(
        SeedSearchMode mode,
        string? seeds,
        string? keyword,
        int? randomCount,
        string expectedMessagePart
    )
    {
        var ex = Assert.Throws<ArgumentException>(
            () => MotelyTools.ValidateModeCombo(mode, seeds, keyword, randomCount)
        );
        Assert.Contains(expectedMessagePart, ex.Message);
        Assert.Contains("Valid combinations", ex.Message);
    }

    [Fact]
    public async Task Search_tool_surfaces_a_run_error_as_a_tool_exception()
    {
        // A missing filter reaches the runner and comes back as an exception — the MCP host turns
        // this into an isError tool result with the message as corrective text.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MotelyTools.Search("no-such-filter-xyz")
        );
        Assert.Contains("no filter", ex.Message);
    }

    [Fact]
    public void Mcp_tool_names_are_the_seed_family()
    {
        Assert.Equal("seed_validate", ToolAttribute(nameof(MotelyTools.ValidateFilter)).Name);
        Assert.Equal("seed_search", ToolAttribute(nameof(MotelyTools.Search)).Name);
    }

    [Fact]
    public void Seed_validate_is_annotated_readonly_idempotent_closed()
    {
        var attr = ToolAttribute(nameof(MotelyTools.ValidateFilter));
        Assert.True(attr.ReadOnly);
        Assert.False(attr.Destructive);
        Assert.True(attr.Idempotent);
        Assert.False(attr.OpenWorld);
    }

    [Fact]
    public void Seed_search_is_annotated_readonly_closed_but_not_idempotent()
    {
        // random mode draws from Random.Shared, so identical calls can differ: idempotentHint must
        // stay off even though replay/seeds/keyword/sequential are deterministic.
        var attr = ToolAttribute(nameof(MotelyTools.Search));
        Assert.True(attr.ReadOnly);
        Assert.False(attr.Destructive);
        Assert.False(attr.Idempotent);
        Assert.False(attr.OpenWorld);
    }

    [Fact]
    public void Seed_search_advertises_structured_content()
    {
        Assert.True(ToolAttribute(nameof(MotelyTools.Search)).UseStructuredContent);
        Assert.False(ToolAttribute(nameof(MotelyTools.ValidateFilter)).UseStructuredContent);
    }

    private static McpServerToolAttribute ToolAttribute(string methodName)
    {
        var method = typeof(MotelyTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method.GetCustomAttribute<McpServerToolAttribute>()
            ?? throw new InvalidOperationException($"{methodName} has no McpServerToolAttribute.");
    }

    [Fact]
    public async Task Agent_streams_a_deterministic_run()
    {
        var first = await RunAgentAsync("search Zerkeo_Pure");
        var second = await RunAgentAsync("search Zerkeo_Pure");

        Assert.Contains("Searching Zerkeo_Pure", first);
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
