using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;
using Motely.MCP.Search;

namespace Motely.MCP.Tools;

/// <summary>The seed-source half of a <see cref="SeedSearchMode"/> call: which optional field the
/// caller may set alongside it, and the SearchRunner mode string to run.</summary>
internal readonly struct SeedSource(string runnerMode, string? requiredField)
{
    public string RunnerMode { get; } = runnerMode;

    /// <summary>The argument name that must carry the source, or null when the mode takes none.</summary>
    public string? RequiredField { get; } = requiredField;
}

/// <summary>
/// How <see cref="MotelyTools.Search"/> picks its seeds. Serialized as lowercase strings in the
/// tool schema so agents send "replay", not "Replay".
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SeedSearchMode>))]
public enum SeedSearchMode
{
    [JsonStringEnumMemberName("replay")]
    Replay,

    [JsonStringEnumMemberName("seeds")]
    Seeds,

    [JsonStringEnumMemberName("keyword")]
    Keyword,

    [JsonStringEnumMemberName("random")]
    Random,

    [JsonStringEnumMemberName("sequential")]
    Sequential,
}

/// <summary>The structured result of a <c>seed_search</c> run, mirrored into the MCP tool result's
/// structured content so agents can consume counts and matches without parsing prose.</summary>
/// <param name="Filter">The filter that ran, as requested.</param>
/// <param name="Mode">The seed-source mode that ran.</param>
/// <param name="Matched">Seeds that passed the filter.</param>
/// <param name="Searched">Seeds the engine evaluated.</param>
/// <param name="DurationMs">Wall-clock run time in milliseconds.</param>
/// <param name="SeedsPerSecond">Engine throughput for the run.</param>
/// <param name="Matches">The matched seeds. The run stops after at least the collect limit's
/// worth, so the engine can overshoot it by one batch; the note flags real truncation.</param>
/// <param name="Note">Truncation or sampling caveats; null when there are none.</param>
/// <param name="RequestedBy">The authenticated caller, or "anonymous".</param>
public sealed record SeedSearchResult(
    string Filter,
    string Mode,
    long Matched,
    long Searched,
    double DurationMs,
    double SeedsPerSecond,
    IReadOnlyList<string> Matches,
    string? Note,
    string RequestedBy
);

/// <summary>
/// The MCP tools, bot-first: two named, annotated, schema-validated entry points over the raw SIMD
/// engine. <c>seed_validate</c> is the same check behind <c>motely check</c>; <c>seed_search</c>
/// runs the engine and returns structured results.
/// </summary>
[McpServerToolType]
public static class MotelyTools
{
    [McpServerTool(
        Name = "seed_validate",
        Title = "Validate a JAML filter",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description(
        """
        Check that a JAML filter loads and that its search plan builds — the same check behind
        `motely check` — without running the engine.

        WHEN: before the first seed_search on a new or edited filter, or to diagnose a filter whose
        seed_search call returned an Error.

        DO-NOT-USE-FOR: finding seeds — use seed_search. seed_validate never runs the engine, so it
        reports clause counts, not matches, and can never time out.

        <examples>
        <example><tool>seed_validate</tool><args>{"filter":"boss-the-eye"}</args></example>
        <example><tool>seed_validate</tool><args>{"filter":"/abs/path/to/filter.jaml"}</args></example>
        </examples>

        Returns: plain text — "ok: <name> (<deck> <stake>) — <n> must, <n> should, <n> mustNot; the
        search plan builds." on success. A failure (missing filter, broken plan) is a tool-execution
        error: an isError result carrying "Error: <reason>", same as seed_search.
        Cost: negligible; the plan builds in memory in milliseconds.
        """
    )]
    public static string ValidateFilter(string filter)
    {
        var path = FilterCheck.ResolvePath(filter);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Error: no filter at {path}.");

        if (FilterCheck.TryCheck(File.ReadAllText(path), out var config, out _, out var error))
            return $"ok: {config.Name ?? "unnamed"} ({config.Deck} {config.Stake}) — "
                + $"{config.Must.Count} must, {config.Should.Count} should, {config.MustNot.Count} mustNot; "
                + "the search plan builds.";

        throw new InvalidOperationException($"Error: {error}");
    }

    // IdempotentHint is deliberately NOT set: random mode draws from Random.Shared, so two calls
    // with identical arguments can return different matches. The other modes are deterministic
    // (same input, same output), but the hint describes the tool as a whole.
    [McpServerTool(
        Name = "seed_search",
        Title = "Search seeds against a JAML filter",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [McpAppUi(ResourceUri = "ui://motely/search")]
    [Description(
        """
        Run the SIMD search engine over a JAML filter and return its matching seeds.

        WHEN: you have a filter (validate a new one with seed_validate first) and want seeds —
        replay the filter's saved seeds, check a specific seed list, explore a keyword's padded
        space, sample random seeds, or sweep the space sequentially.

        DO-NOT-USE-FOR: checking that a filter loads or builds a plan — use seed_validate; a broken
        filter makes seed_search return an Error instead of matches.

        Exactly one mode, with exactly its matching source field:
          replay     — the filter's saved seeds: block (fast, deterministic). No source field.
          seeds      — with `seeds`: comma-separated seeds ("A1B2C3D4,E5F6G7H8") or an absolute path
                       to a seed file/CSV (deterministic).
          keyword    — with `keyword`: a seed word ("DEAL"); its padded space is searched.
          random     — with `randomCount`: that many fresh random seeds (NOT deterministic —
                       every call samples anew).
          sequential — a sequential sweep of the seed space. No source field; always pass
                       `collect`, or it stops at the default cap.
        Any other combination is rejected with an error naming the fix.

        <examples>
        <example><tool>seed_search</tool><args>{"filter":"Zerkeo_Pure"}</args></example>
        <example><tool>seed_search</tool><args>{"filter":"Zerkeo_Pure","mode":"seeds","seeds":"A1B2C3D4,E5F6G7H8"}</args></example>
        <example><tool>seed_search</tool><args>{"filter":"Zerkeo_Pure","mode":"random","randomCount":100000,"collect":10}</args></example>
        <example><tool>seed_search</tool><args>{"filter":"Zerkeo_Pure","mode":"sequential","collect":100}</args></example>
        </examples>

        Returns: structured content { filter, mode, matched, searched, durationMs,
        seedsPerSecond, matches, note, requestedBy }; `matched` counts every match in the run and
        `matches` lists them (the run stops after at least `collect` matches, so one batch of
        overshoot is normal); `note` flags when `matched` exceeded what `matches` can carry.
        Cost: replay/seeds are bounded by the list (instant). random ≈ seconds at ~70k seeds/s
        (random 100k ≈ 1.5 s). keyword depends on the word's padded space. sequential over the
        uncapped space can run ~2.3T seeds — always pass `collect` for sequential.
        """
    )]
    public static async Task<SeedSearchResult> Search(
        string filter,
        SeedSearchMode mode = SeedSearchMode.Replay,
        string? seeds = null,
        string? keyword = null,
        [Range(1, int.MaxValue)] int? randomCount = null,
        [Range(1, 10_000)] int? collect = null,
        // DI-augmented parameter: bound from the request, not the schema. Defaults to null so an
        // unauthenticated (dev) request binds to null instead of failing service resolution.
        ClaimsPrincipal? user = null,
        CancellationToken cancellationToken = default
    )
    {
        var source = ValidateModeCombo(mode, seeds, keyword, randomCount);
        var runner = new SearchRunner();
        var who = user?.Identity?.Name ?? "anonymous";
        var matches = new List<string>();
        long total = 0;
        long matched = 0;
        double rate = 0;
        TimeSpan elapsed = default;

        await foreach (var evt in runner.RunAsync(filter, source.RunnerMode, collect, cancellationToken))
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
                    // A run that fails (missing/broken filter, bad seed list) is a tool-execution
                    // error: the SDK turns this into an isError result with this corrective text.
                    throw new InvalidOperationException(e.Message);
            }
        }

        var note = matched > matches.Count
            ? $"matches truncated to {matches.Count} of {matched:N0}; raise collect (max 10,000) to see more."
            : mode == SeedSearchMode.Random
                ? "random mode samples fresh seeds every call; rerun for a different draw."
                : null;

        return new SeedSearchResult(
            Filter: filter,
            Mode: mode.ToString().ToLowerInvariant(),
            Matched: matched,
            Searched: total,
            DurationMs: Math.Round(elapsed.TotalMilliseconds, 1),
            SeedsPerSecond: Math.Round(rate, 1),
            Matches: matches,
            Note: note,
            RequestedBy: who
        );
    }

    /// <summary>
    /// The mode/source matrix as an MCP tool-execution error: names the exact problem and the
    /// valid combinations, so an agent can correct the call without guessing.
    /// </summary>
    internal static SeedSource ValidateModeCombo(
        SeedSearchMode mode,
        string? seeds,
        string? keyword,
        int? randomCount
    )
    {
        var extras = new List<string>();
        if (seeds is not null)
            extras.Add("seeds");
        if (keyword is not null)
            extras.Add("keyword");
        if (randomCount is not null)
            extras.Add("randomCount");

        SeedSource expected = mode switch
        {
            SeedSearchMode.Replay => new("replay", null),
            SeedSearchMode.Seeds => new($"seeds {seeds}", "seeds"),
            SeedSearchMode.Keyword => new($"keyword {keyword}", "keyword"),
            SeedSearchMode.Random => new($"random {randomCount}", "randomCount"),
            SeedSearchMode.Sequential => new("sequential", null),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unreachable"),
        };

        var modeName = mode.ToString().ToLowerInvariant();
        var extrasText = extras.Count == 0 ? "none" : string.Join(", ", extras);

        if (expected.RequiredField is null && extras.Count > 0)
            throw new ArgumentException(
                $"Invalid seed_search arguments: mode '{modeName}' takes no seed source, but {extrasText} "
                    + $"provided. Valid combinations: replay (no source), seeds + seeds, keyword + keyword, "
                    + "random + randomCount, sequential (no source)."
            );

        if (expected.RequiredField is not null)
        {
            var present = expected.RequiredField switch
            {
                "seeds" => seeds,
                "keyword" => keyword,
                "randomCount" => randomCount?.ToString(),
                _ => null,
            };
            if (present is null or { Length: 0 })
                throw new ArgumentException(
                    $"Invalid seed_search arguments: mode '{modeName}' requires '{expected.RequiredField}', "
                        + $"got {extrasText}. Valid combinations: replay (no source), seeds + seeds, "
                        + "keyword + keyword, random + randomCount, sequential (no source)."
                );

            var unexpected = extras.Where(e => e != expected.RequiredField).ToList();
            if (unexpected.Count > 0)
                throw new ArgumentException(
                    $"Invalid seed_search arguments: mode '{modeName}' does not take "
                        + $"'{string.Join("' and '", unexpected)}'; remove it. Valid combinations: "
                        + "replay (no source), seeds + seeds, keyword + keyword, random + randomCount, "
                        + "sequential (no source)."
                );
        }

        return expected;
    }
}
