using System.Reflection;
using System.Text;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using Motely.MCP.Agent;
using Motely.MCP.Resources;
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
        Assert.Equal("seed_analyze", ToolAttribute(nameof(MotelyTools.Analyze)).Name);
        Assert.Equal("filter_list", ToolAttribute(nameof(MotelyTools.ListFilters)).Name);
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
    public void Filter_list_shows_repo_filters_with_their_deck_and_stake()
    {
        var all = MotelyTools.ListFilters();
        var eye = Assert.Single(all.Filters, f => f.Filter == "boss-the-eye");

        Assert.Equal(all.Filters.Count, all.Count);
        Assert.Equal("Red", eye.Deck);
        Assert.Equal("White", eye.Stake);
        Assert.True(eye.Must > 0);
        Assert.Null(eye.Error);
        // The stray CSVs in JamlFilters/ are seed lists, not filters.
        Assert.DoesNotContain(all.Filters, f => f.Filter.Contains("Seed,Score"));
    }

    [Fact]
    public void Filter_list_narrows_by_name_title_or_description()
    {
        var found = MotelyTools.ListFilters("PERKEO");

        Assert.NotEmpty(found.Filters);
        Assert.Contains(found.Filters, f => f.Filter == "legendary-perkeo");
        Assert.DoesNotContain(found.Filters, f => f.Filter == "boss-the-eye");
    }

    [Fact]
    public void Validate_accepts_inline_jaml()
    {
        var jaml = "name: Eye\nmust:\n  - boss: TheEye\n    antes: [1, 2]\n";
        Assert.StartsWith("ok: Eye", MotelyTools.ValidateFilter(jaml: jaml));
    }

    [Theory]
    [InlineData("boss-the-eye", "name: x\n", "not both")]
    [InlineData(null, null, "pass 'filter'")]
    [InlineData("name: x\nmust: []\n", null, "pass it as 'jaml'")]
    public void Validate_names_the_fix_for_a_bad_filter_source(string? filter, string? jaml, string expected)
    {
        var ex = Assert.Throws<ArgumentException>(() => MotelyTools.ValidateFilter(filter, jaml));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Validate_reports_broken_inline_jaml_as_an_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => MotelyTools.ValidateFilter(jaml: "must:\n  - boss: NotARealBoss\n    antes: [1]\n")
        );
        Assert.StartsWith("Error:", ex.Message);
    }

    [Fact]
    public void Guide_examples_all_validate()
    {
        foreach (var example in MotelyResources.GuideExamples)
        {
            Assert.StartsWith("ok:", MotelyTools.ValidateFilter(jaml: example));
            Assert.Contains(example, MotelyResources.Guide());
        }
    }

    [Fact]
    public void Schema_resource_serves_the_filter_schema()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(MotelyResources.Schema());
        Assert.True(doc.RootElement.GetProperty("properties").TryGetProperty("must", out _));
    }

    [Fact]
    public async Task Search_runs_inline_jaml_against_a_seed_list()
    {
        var result = await MotelyTools.Search(
            mode: SeedSearchMode.Seeds,
            seeds: "A1B2C3D4,E5F6G7H8",
            jaml: MotelyResources.GuideExamples[0]
        );

        Assert.Equal("Eye in ante 3 or 4", result.Filter);
        Assert.Equal(2, result.Searched);
        Assert.Null(result.NextBatch);
    }

    [Fact]
    public async Task Sequential_windows_tile_the_sweep_with_no_overlap_or_gap()
    {
        // Two one-batch windows, then the same two batches as one window: the halves must add up
        // to the whole exactly, which fails if a window stops early, repeats seeds or skips them.
        var first = await MotelyTools.Search("boss-the-eye", SeedSearchMode.Sequential, collect: 10_000, batches: 1);
        var second = await MotelyTools.Search(
            "boss-the-eye",
            SeedSearchMode.Sequential,
            collect: 10_000,
            startBatch: first.NextBatch,
            batches: 1
        );
        var both = await MotelyTools.Search("boss-the-eye", SeedSearchMode.Sequential, collect: 10_000, batches: 2);

        Assert.Equal(1, first.NextBatch);
        Assert.Equal(2, second.NextBatch);
        Assert.Equal(2, both.NextBatch);
        Assert.True(first.Matched > 0);
        Assert.Empty(first.Matches.Intersect(second.Matches));
        Assert.Equal(both.Matches, first.Matches.Concat(second.Matches).Order(StringComparer.Ordinal));
        Assert.Equal(both.Searched, first.Searched + second.Searched);
    }

    [Fact]
    public async Task Sequential_window_lists_collect_matches_but_counts_them_all()
    {
        var result = await MotelyTools.Search("boss-the-eye", SeedSearchMode.Sequential, collect: 3, batches: 1);

        Assert.Equal(3, result.Matches.Count);
        Assert.True(result.Matched > 3);
        Assert.Contains("truncated", result.Note);
        Assert.Equal(1, result.NextBatch);
    }

    [Theory]
    [InlineData(SeedSearchMode.Replay)]
    [InlineData(SeedSearchMode.Random)]
    public void Start_batch_is_refused_outside_sequential(SeedSearchMode mode)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => MotelyTools.ValidateModeCombo(mode, null, null, mode == SeedSearchMode.Random ? 10 : null, 5)
        );
        Assert.Contains("startBatch", ex.Message);
    }

    [Fact]
    public void Batches_are_refused_outside_sequential()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => MotelyTools.ValidateModeCombo(SeedSearchMode.Random, null, null, 10, batches: 4)
        );
        Assert.Contains("batches", ex.Message);
    }

    [Theory]
    // The Eye first appears in ante 3; ante 8 is always a showdown boss.
    [InlineData("must:\n  - boss: TheEye\n    antes: [1, 2]\n", "can never match")]
    [InlineData("must:\n  - boss: TheEye\n    antes: [1, 2, 3]\n", "ante(s) 1, 2")]
    [InlineData("should:\n  - boss: TheWindow\n    antes: [8]\n", "never counts")]
    [InlineData("must:\n  - boss: VioletVessel\n    antes: [4]\n", "showdown")]
    public void Validate_warns_about_a_boss_in_an_impossible_ante(string jaml, string expected)
    {
        var result = MotelyTools.ValidateFilter(jaml: jaml);
        Assert.StartsWith("ok:", result);
        Assert.Contains("warning:", result);
        Assert.Contains(expected, result);
    }

    [Fact]
    public void Validate_stays_quiet_for_possible_boss_antes()
    {
        Assert.DoesNotContain("warning", MotelyTools.ValidateFilter(jaml: MotelyResources.GuideExamples[0]));
        Assert.DoesNotContain("warning", MotelyTools.ValidateFilter(jaml: MotelyResources.GuideExamples[2]));
    }

    [Fact]
    public async Task Search_surfaces_filter_warnings_in_its_note()
    {
        var result = await MotelyTools.Search(
            mode: SeedSearchMode.Seeds,
            seeds: "A1B2C3D4",
            jaml: "must:\n  - boss: TheEye\n    antes: [1]\n"
        );
        Assert.Contains("can never match", result.Note);
    }

    [Fact]
    public void Seeds_from_stdin_are_refused_on_the_server()
    {
        // "-" makes the shared resolver read Console.In, which would block the request forever.
        var ex = Assert.Throws<ArgumentException>(
            () => MotelyTools.ValidateModeCombo(SeedSearchMode.Seeds, "-", null, null)
        );
        Assert.Contains("stdin", ex.Message);
        Assert.Throws<ArgumentException>(() => MotelyTools.Analyze("-"));
    }

    [Fact]
    public void Analyze_reads_a_seed_ante_by_ante()
    {
        var result = MotelyTools.Analyze("F2U88X11", deck: "anaglyph", stake: "White");
        var analysis = Assert.Single(result.Analyses);

        Assert.Equal("F2U88X11", analysis.Seed);
        Assert.Equal("Anaglyph", analysis.Deck);
        Assert.Null(analysis.Error);
        Assert.False(string.IsNullOrWhiteSpace(analysis.Text));
    }

    [Fact]
    public void Analyze_keeps_only_the_antes_asked_for()
    {
        var full = Assert.Single(MotelyTools.Analyze("F2U88X11").Analyses).Text!;
        var cut = Assert.Single(MotelyTools.Analyze("F2U88X11", antes: [1, 2]).Analyses).Text!;

        Assert.Contains("==ANTE 1==", cut);
        Assert.Contains("==ANTE 2==", cut);
        Assert.DoesNotContain("==ANTE 3==", cut);
        Assert.DoesNotContain("\r", cut);
        Assert.True(cut.Length < full.Length / 2);
    }

    [Fact]
    public async Task Random_note_says_when_collect_stopped_the_run_early()
    {
        var result = await MotelyTools.Search(
            "boss-the-eye",
            SeedSearchMode.Random,
            randomCount: 1_000_000,
            collect: 1
        );
        Assert.True(result.Searched < 1_000_000);
        Assert.Contains("because collect was reached", result.Note);
    }

    [Fact]
    public void Analyze_names_the_valid_decks_for_a_bad_one()
    {
        var ex = Assert.Throws<ArgumentException>(() => MotelyTools.Analyze("F2U88X11", deck: "Rainbow"));
        Assert.Contains("Anaglyph", ex.Message);
    }

    [Fact]
    public void Analyze_caps_the_seeds_per_call()
    {
        var seeds = string.Join(",", Enumerable.Range(1, MotelyTools.MaxAnalyzeSeeds + 1).Select(i => $"AAAA{i:D2}".Replace('0', 'Z')));
        var ex = Assert.Throws<ArgumentException>(() => MotelyTools.Analyze(seeds));
        Assert.Contains("at most", ex.Message);
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
