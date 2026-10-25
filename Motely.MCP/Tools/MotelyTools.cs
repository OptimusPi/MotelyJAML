using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters;
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
/// <param name="Filter">The filter that ran: the name as requested, or an inline filter's name.</param>
/// <param name="Mode">The seed-source mode that ran.</param>
/// <param name="Matched">Seeds that passed the filter.</param>
/// <param name="Searched">Seeds the engine evaluated.</param>
/// <param name="DurationMs">Wall-clock run time in milliseconds.</param>
/// <param name="SeedsPerSecond">Engine throughput for the run.</param>
/// <param name="Matches">The matched seeds. The run stops after at least the collect limit's
/// worth, so the engine can overshoot it by one batch; the note flags real truncation.</param>
/// <param name="Note">Truncation or sampling caveats; null when there are none.</param>
/// <param name="RequestedBy">The authenticated caller, or "anonymous".</param>
/// <param name="NextBatch">Sequential only: pass this back as <c>startBatch</c> to sweep the next
/// window. Null when the sweep reached the end, or for any other mode.</param>
public sealed record SeedSearchResult(
    string Filter,
    string Mode,
    long Matched,
    long Searched,
    double DurationMs,
    double SeedsPerSecond,
    IReadOnlyList<string> Matches,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Note,
    string RequestedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NextBatch = null
);

/// <summary>One filter in the filters directory, as <c>filter_list</c> reports it.</summary>
/// <param name="Filter">What to pass as seed_search's <c>filter</c>.</param>
/// <param name="Error">Why the file failed to load; the other fields are empty when set.</param>
public sealed record FilterSummary(
    string Filter,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Deck,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Stake,
    int Must,
    int Should,
    int MustNot,
    int SavedSeeds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Error
);

/// <summary>The structured result of <c>filter_list</c>.</summary>
public sealed record FilterListResult(int Count, IReadOnlyList<FilterSummary> Filters);

/// <summary>One seed's ante-by-ante contents, as <c>seed_analyze</c> reports it.</summary>
/// <param name="Text">The same block <c>motely --analyze</c> prints; null when <c>Error</c> is set.</param>
public sealed record SeedAnalysis(
    string Seed,
    string Deck,
    string Stake,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Error
);

/// <summary>The structured result of <c>seed_analyze</c>.</summary>
public sealed record SeedAnalyzeResult(IReadOnlyList<SeedAnalysis> Analyses);

/// <summary>
/// The MCP tools, bot-first: named, annotated, schema-validated entry points over the raw SIMD
/// engine. <c>filter_list</c> shows what can be searched, <c>seed_validate</c> is the same check
/// behind <c>motely check</c>, <c>seed_search</c> runs the engine, and <c>seed_analyze</c> shows
/// what is actually in a seed.
/// </summary>
[McpServerToolType]
public static class MotelyTools
{
    /// <summary>The most seeds one <c>seed_analyze</c> call reads (each is a page of text).</summary>
    public const int MaxAnalyzeSeeds = 20;

    /// <summary>Batches one sequential call sweeps by default (~686k seeds), and at most (~11M).</summary>
    public const int DefaultSequentialBatches = 16;

    public const int MaxSequentialBatches = 256;

    [McpServerTool(
        Name = "filter_list",
        Title = "List the available JAML filters",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        """
        List the JAML filters on this server: the name to pass as seed_search's `filter`, plus each
        filter's title, description, deck, stake and clause counts.

        WHEN: first, to find out what filters exist instead of guessing names. Narrow with
        `contains` (case-insensitive, matched against the name, title and description).

        DO-NOT-USE-FOR: a filter you wrote yourself — pass its text as `jaml` to seed_validate and
        seed_search instead; nothing needs to be saved here first.

        <examples>
        <example><tool>filter_list</tool><args>{}</args></example>
        <example><tool>filter_list</tool><args>{"contains":"perkeo"}</args></example>
        </examples>

        Returns: structured content { count, filters: [{ filter, name, description, deck, stake,
        must, should, mustNot, savedSeeds, error }] }. A file that fails to load is listed with
        `error` set rather than failing the call. savedSeeds > 0 means mode "replay" has seeds to run.
        Cost: milliseconds.
        """
    )]
    public static FilterListResult ListFilters(string? contains = null)
    {
        var filters = new List<FilterSummary>();
        foreach (var path in FilterCheck.ListFilterFiles())
        {
            var name = Path.GetFileNameWithoutExtension(path);
            FilterSummary summary;
            try
            {
                var config = JamlConfigLoader.FromFile(path);
                summary = new(
                    name,
                    config.Name,
                    config.Description,
                    config.Deck.ToString(),
                    config.Stake.ToString(),
                    config.Must.Count,
                    config.Should.Count,
                    config.MustNot.Count,
                    config.Seeds.Count,
                    null
                );
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                summary = new(name, null, null, null, null, 0, 0, 0, 0, ex.Message);
            }

            if (
                string.IsNullOrWhiteSpace(contains)
                || Matches(summary.Filter, contains)
                || Matches(summary.Name, contains)
                || Matches(summary.Description, contains)
            )
                filters.Add(summary);
        }
        return new(filters.Count, filters);

        static bool Matches(string? field, string query) =>
            field?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }

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

        Pass exactly one of `filter` (a name from filter_list, or an absolute path) or `jaml` (the
        filter's YAML text, for a filter you wrote — the resource jaml://guide shows the format).

        <examples>
        <example><tool>seed_validate</tool><args>{"filter":"boss-the-eye"}</args></example>
        <example><tool>seed_validate</tool><args>{"jaml":"name: Eye\ndeck: Red\nstake: White\nmust:\n  - boss: TheEye\n    antes: [3, 4]\n"}</args></example>
        </examples>

        Returns: plain text — "ok: <name> (<deck> <stake>) — <n> must, <n> should, <n> mustNot; the
        search plan builds." on success, followed by one "warning: ..." line per clause that loads
        but can't match as written (a boss asked for before its first ante — The Eye can't appear
        before ante 3; a must clause with no possible ante makes the filter unmatchable). A failure (missing filter, bad YAML, broken plan) is a
        tool-execution error: an isError result carrying "Error: <reason>", same as seed_search.
        Cost: negligible; the plan builds in memory in milliseconds.
        """
    )]
    public static string ValidateFilter(string? filter = null, string? jaml = null)
    {
        var (text, _) = ReadFilterText(filter, jaml);

        if (FilterCheck.TryCheck(text, out var config, out _, out var error))
            return $"ok: {config.Name ?? "unnamed"} ({config.Deck} {config.Stake}) — "
                + $"{config.Must.Count} must, {config.Should.Count} should, {config.MustNot.Count} mustNot; "
                + "the search plan builds."
                + string.Concat(FilterCheck.Warnings(config).Select(w => $"\nwarning: {w}"));

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
    [Description(
        """
        Run the SIMD search engine over a JAML filter and return its matching seeds.

        WHEN: you have a filter (validate a new one with seed_validate first) and want seeds —
        replay the filter's saved seeds, check a specific seed list, explore a keyword's padded
        space, sample random seeds, or sweep the space sequentially.

        DO-NOT-USE-FOR: checking that a filter loads or builds a plan — use seed_validate; a broken
        filter makes seed_search return an Error instead of matches. To see WHY a seed matched
        (its bosses, vouchers, shops, packs), pass the matches to seed_analyze.

        The filter: exactly one of `filter` (a name from filter_list, or an absolute path) or `jaml`
        (the filter's YAML text — write your own; the resource jaml://guide shows the format).

        Exactly one mode, with exactly its matching source field:
          replay     — the filter's saved seeds: block (fast, deterministic). No source field.
                       A filter with no seeds: block is an error here; use random or sequential.
          seeds      — with `seeds`: comma-separated seeds ("A1B2C3D4,E5F6G7H8") or an absolute path
                       to a seed file/CSV (deterministic).
          keyword    — with `keyword`: a seed word ("DEAL"); its padded space is searched.
          random     — with `randomCount`: that many fresh random seeds (NOT deterministic —
                       every call samples anew). The easiest way to find new seeds.
          sequential — a deterministic sweep of the seed space, one window per call: `batches`
                       whole batches (default 16, max 256; a batch is 42,875 seeds) starting at
                       `startBatch` (default 0). Continue with startBatch = the returned
                       `nextBatch`: windows never overlap or leave gaps. `collect` only caps how
                       many matches are listed; every batch in the window is searched.
        Any other combination is rejected with an error naming the fix.

        <examples>
        <example><tool>seed_search</tool><args>{"filter":"Zerkeo_Pure"}</args></example>
        <example><tool>seed_search</tool><args>{"filter":"boss-the-eye","mode":"random","randomCount":100000,"collect":10}</args></example>
        <example><tool>seed_search</tool><args>{"jaml":"name: Eye\nmust:\n  - boss: TheEye\n    antes: [3, 4]\n","mode":"random","randomCount":50000,"collect":5}</args></example>
        <example><tool>seed_search</tool><args>{"filter":"Zerkeo_Pure","mode":"seeds","seeds":"A1B2C3D4,E5F6G7H8"}</args></example>
        <example><tool>seed_search</tool><args>{"filter":"boss-the-eye","mode":"sequential","collect":100}</args></example>
        <example><tool>seed_search</tool><args>{"filter":"boss-the-eye","mode":"sequential","collect":100,"startBatch":16}</args></example>
        </examples>

        Returns: structured content { filter, mode, matched, searched, durationMs,
        seedsPerSecond, matches, note, requestedBy, nextBatch }; `matched` counts every match in the
        run and `matches` lists them (random/keyword stop after at least `collect` matches, so some
        overshoot is normal); `note` flags truncated lists and filter warnings (a clause that can
        never match — check it when a search finds 0); `nextBatch` is where the next sequential
        window starts, null once the sweep reaches the end.
        Cost: replay/seeds are bounded by the list (instant). random and sequential speed depends on
        the filter and the host: a strict filter rejects millions of seeds per second (random
        1,000,000 in ~0.1 s on a 16-core desktop), a loose one that matches often runs ~0.3-0.5M/s
        while it collects. Each run reports its own seedsPerSecond. A rare filter can need
        millions of seeds per match, so random 1,000,000+ is a reasonable first try. keyword
        depends on the word's padded space. A sequential window of 16 batches is ~686k seeds; the
        whole space is ~2.3T seeds.
        """
    )]
    public static async Task<SeedSearchResult> Search(
        string? filter = null,
        SeedSearchMode mode = SeedSearchMode.Replay,
        string? seeds = null,
        string? keyword = null,
        [Range(1, int.MaxValue)] int? randomCount = null,
        [Range(1, 10_000)] int? collect = null,
        string? jaml = null,
        [Range(0, long.MaxValue)] long? startBatch = null,
        [Range(1, MaxSequentialBatches)] int? batches = null,
        // DI-augmented parameter: bound from the request, not the schema. Defaults to null so an
        // unauthenticated (dev) request binds to null instead of failing service resolution.
        ClaimsPrincipal? user = null,
        CancellationToken cancellationToken = default
    )
    {
        var source = ValidateModeCombo(mode, seeds, keyword, randomCount, startBatch, batches);
        var (text, label) = ReadFilterText(filter, jaml);
        var config = LoadConfig(text, label);

        var runner = new SearchRunner();
        var who = user?.Identity?.Name ?? "anonymous";
        var matches = new List<string>();
        long total = 0;
        long matched = 0;
        double rate = 0;
        TimeSpan elapsed = default;

        // A sequential call sweeps whole batches [start, end) so the next call can start exactly
        // where this one ended.
        long? endBatch = null;
        long? nextBatch = null;
        if (mode == SeedSearchMode.Sequential)
        {
            long maxBatch = MotelyGlobals.SequentialBatchCount(CliSearchMode.DefaultBatchCharacterCount);
            long start = startBatch ?? 0;
            if (start >= maxBatch)
                throw new ArgumentException($"Error: startBatch must be 0..{maxBatch - 1}; the sweep has {maxBatch:N0} batches.");
            endBatch = Math.Min(start + (batches ?? DefaultSequentialBatches), maxBatch);
            nextBatch = endBatch < maxBatch ? endBatch : null;
        }

        await foreach (
            var evt in runner.RunAsync(config, label, source.RunnerMode, collect, startBatch, endBatch, cancellationToken)
        )
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

        // A window runs to its end, so it can find more than collect; list the first in seed order.
        if (endBatch is not null)
        {
            int listed = collect ?? SearchRunner.DefaultCollect;
            matches.Sort(StringComparer.Ordinal);
            if (matches.Count > listed)
                matches.RemoveRange(listed, matches.Count - listed);
        }

        List<string> notes = [];
        if (matched > matches.Count)
            notes.Add(
                endBatch is not null
                    ? $"matches truncated to {matches.Count} of {matched:N0}; raise collect (max 10,000) or lower batches to list them all."
                    : $"matches truncated to {matches.Count} of {matched:N0}; raise collect (max 10,000) to see more."
            );
        if (mode == SeedSearchMode.Random)
            notes.Add(
                total < randomCount
                    ? $"stopped after {total:N0} of {randomCount:N0} random seeds because collect was reached; "
                        + "random mode samples fresh seeds every call, so rerun for more."
                    : "random mode samples fresh seeds every call; rerun for a different draw."
            );
        notes.AddRange(FilterCheck.Warnings(config).Select(w => $"warning: {w}"));
        var note = notes.Count == 0 ? null : string.Join(" ", notes);

        return new SeedSearchResult(
            Filter: filter ?? config.Name ?? "inline",
            Mode: mode.ToString().ToLowerInvariant(),
            Matched: matched,
            Searched: total,
            DurationMs: Math.Round(elapsed.TotalMilliseconds, 1),
            SeedsPerSecond: Math.Round(rate, 1),
            Matches: matches,
            Note: note,
            RequestedBy: who,
            NextBatch: nextBatch
        );
    }

    [McpServerTool(
        Name = "seed_analyze",
        Title = "Show what is in a seed",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        """
        Show a seed's contents ante by ante: boss, voucher, tags, shop queue and booster packs —
        the same report as `motely --analyze`.

        WHEN: after seed_search, to check or explain a match (say what the seed actually has
        instead of guessing), or to see what a seed someone mentioned contains.

        DO-NOT-USE-FOR: finding seeds — use seed_search; this reads seeds you already have.

        `seeds`: comma-separated seeds or an absolute path to a seed file/CSV, at most 20 per call.
        `deck` and `stake` change what a seed contains — pass the filter's own (filter_list shows
        them); they default to Red and White. `antes` (e.g. [1, 2]) keeps only those antes: the
        full report is a few KB per seed, mostly shop queues, so pass the antes your filter uses.

        <examples>
        <example><tool>seed_analyze</tool><args>{"seeds":"F2U88X11","deck":"Anaglyph","stake":"White"}</args></example>
        <example><tool>seed_analyze</tool><args>{"seeds":"A1B2C3D4,E5F6G7H8","antes":[1,2]}</args></example>
        </examples>

        Returns: structured content { analyses: [{ seed, deck, stake, text, error }] }; `text` is
        the readable per-ante report. A bad seed list, deck or stake is an isError result.
        Cost: milliseconds per seed.
        """
    )]
    public static SeedAnalyzeResult Analyze(
        string seeds,
        string? deck = null,
        string? stake = null,
        int[]? antes = null
    )
    {
        var motelyDeck = ParseEnum(deck, MotelyDeck.Red, "deck");
        var motelyStake = ParseEnum(stake, MotelyStake.White, "stake");
        var list = ResolveSeeds(seeds);
        if (list.Length > MaxAnalyzeSeeds)
            throw new ArgumentException(
                $"Error: seed_analyze reads at most {MaxAnalyzeSeeds} seeds per call, got {list.Length}; "
                    + "split the list across calls."
            );

        var analyses = list.Select(seed =>
            {
                var analysis = MotelyUnitTestAnalyzer.Analyze(new(seed, motelyDeck, motelyStake));
                return string.IsNullOrEmpty(analysis.Error)
                    ? new SeedAnalysis(seed, motelyDeck.ToString(), motelyStake.ToString(), Report(analysis, antes), null)
                    : new SeedAnalysis(seed, motelyDeck.ToString(), motelyStake.ToString(), null, analysis.Error);
            })
            .ToList();
        return new(analyses);
    }

    /// <summary>
    /// The analyzer's text block with \n line ends, cut down to <paramref name="antes"/> when given.
    /// Each ante starts at its "==ANTE n==" line; anything before ante 1 (an Erratic deck's
    /// composition) is kept.
    /// </summary>
    private static string Report(MotelyUnitTestAnalysis analysis, int[]? antes)
    {
        var text = analysis.ToString().Replace("\r\n", "\n");
        if (antes is not { Length: > 0 })
            return text;

        var sb = new System.Text.StringBuilder();
        bool keep = true;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("==ANTE ", StringComparison.Ordinal))
                keep = int.TryParse(line.AsSpan(7).TrimEnd('='), out var ante) && antes.Contains(ante);
            if (keep)
                sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// The filter's text and a label for errors: a named or path filter is read from disk, inline
    /// JAML is used as is. Exactly one of the two must be given.
    /// </summary>
    internal static (string Text, string Label) ReadFilterText(string? filter, string? jaml)
    {
        bool hasFilter = !string.IsNullOrWhiteSpace(filter);
        bool hasJaml = !string.IsNullOrWhiteSpace(jaml);

        if (hasFilter && hasJaml)
            throw new ArgumentException(
                "Error: pass either 'filter' (a name or absolute path) or 'jaml' (the filter's text), not both."
            );
        if (hasJaml)
            return (jaml!, "inline JAML");
        if (!hasFilter)
            throw new ArgumentException(
                "Error: pass 'filter' (a name from filter_list, or an absolute path) or 'jaml' (the filter's YAML text)."
            );

        // A pasted filter body in the name field: the path lookup would only report "no filter at
        // <the whole YAML>".
        if (filter!.Contains('\n'))
            throw new ArgumentException("Error: 'filter' looks like JAML text; pass it as 'jaml' instead.");

        var path = FilterCheck.ResolvePath(filter);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Error: no filter at {path}. filter_list shows the available names; "
                    + "or pass your own filter's text as 'jaml'."
            );
        return (File.ReadAllText(path), path);
    }

    private static JamlConfig LoadConfig(string text, string label)
    {
        try
        {
            return JamlConfigLoader.FromJaml(text);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"Error: {label}: {ex.Message}", ex);
        }
    }

    /// <summary>Seeds for a tool call. "-" (stdin) means nothing on a server and would block the
    /// request, so it is refused here before the shared resolver sees it.</summary>
    private static string[] ResolveSeeds(string value)
    {
        if (value.Trim() == "-")
            throw new ArgumentException("Error: 'seeds' can't be '-' (stdin) here; pass seeds or an absolute file path.");
        if (!CliSearchMode.TryResolveSeeds(value, out var list, out var error))
            throw new ArgumentException(error.Replace("--seeds", "seeds"));
        return list;
    }

    private static T ParseEnum<T>(string? value, T fallback, string field)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new ArgumentException(
            $"Error: unknown {field} '{value}'. Use one of: {string.Join(", ", Enum.GetNames<T>())}."
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
        int? randomCount,
        long? startBatch = null,
        int? batches = null
    )
    {
        var extras = new List<string>();
        if (seeds is not null)
            extras.Add("seeds");
        if (keyword is not null)
            extras.Add("keyword");
        if (randomCount is not null)
            extras.Add("randomCount");

        if ((startBatch is not null || batches is not null) && mode != SeedSearchMode.Sequential)
            throw new ArgumentException(
                $"Invalid seed_search arguments: '{(startBatch is not null ? "startBatch" : "batches")}' sets a "
                    + $"sequential sweep's window; mode '{mode.ToString().ToLowerInvariant()}' does not take it. "
                    + "Remove it, or use mode 'sequential'."
            );

        if (mode == SeedSearchMode.Seeds && seeds?.Trim() == "-")
            throw new ArgumentException("Error: 'seeds' can't be '-' (stdin) here; pass seeds or an absolute file path.");

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
