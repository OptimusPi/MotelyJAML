using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters;
using Motely.Gpu;
using Motely.MCP.Data;
using Motely.MCP.Party;
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? NextBatch = null,
    bool GpuUsed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DeviceName = null,
    IReadOnlyList<SeedMatch>? TopMatches = null,
    IReadOnlyList<string>? TallyLabels = null
);

/// <summary>One filter in the filters directory, as <c>filter_list</c> reports it.</summary>
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
public sealed record SeedAnalysis(
    string Seed,
    string Deck,
    string Stake,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Error
);

/// <summary>The structured result of <c>seed_analyze</c>.</summary>
public sealed record SeedAnalyzeResult(IReadOnlyList<SeedAnalysis> Analyses);

/// <summary>One place an item appears, as <c>seed_locate</c> reports it.</summary>
public sealed record SeedLocateHit(int Ante, string Source, int Position, string Where, string Item, string Edition);

/// <summary>The structured result of <c>seed_locate</c>.</summary>
public sealed record SeedLocateResult(
    string Seed,
    string Deck,
    string Stake,
    string Item,
    string Searched,
    IReadOnlyList<SeedLocateHit> Hits
);

/// <summary>The structured result of <c>gpu_status</c>.</summary>
public sealed record GpuStatusResult(
    int DeviceCount,
    IReadOnlyList<GpuDeviceInfo> Devices,
    GpuBenchmarkResult Benchmark
);

/// <summary>
/// The MCP tools, bot-first: named, annotated, schema-validated entry points over the raw SIMD +
/// OpenCL GPU engine, persistent database store, and Community Party Search coordinator.
/// </summary>
[McpServerToolType]
public static class MotelyTools
{
    private static readonly object Sync = new();
    private static IMotelyStore? _store;
    private static PartyCoordinator? _partyCoordinator;
    private static SearchRunner? _searchRunner;

    /// <summary>Configures shared server dependencies for MCP tools.</summary>
    public static void Configure(IMotelyStore store, PartyCoordinator partyCoordinator, SearchRunner searchRunner)
    {
        lock (Sync)
        {
            _store = store;
            _partyCoordinator = partyCoordinator;
            _searchRunner = searchRunner;
        }
    }

    internal static IMotelyStore GetStore()
    {
        lock (Sync)
        {
            if (_store is null)
            {
                var filtersDir = Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") ?? "JamlFilters";
                var dataDir = Path.Combine(Path.GetTempPath(), "MotelyMCPData_Default");
                var fileStore = new FileMotelyStore(dataDir);
                fileStore.InitializeAsync(filtersDir).GetAwaiter().GetResult();
                _store = fileStore;
            }
            return _store;
        }
    }

    internal static PartyCoordinator GetPartyCoordinator()
    {
        lock (Sync)
        {
            if (_partyCoordinator is null)
            {
                var filtersDir = Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") ?? "JamlFilters";
                _partyCoordinator = new PartyCoordinator(GetStore(), filtersDir);
            }
            return _partyCoordinator;
        }
    }

    internal static SearchRunner GetSearchRunner()
    {
        lock (Sync)
        {
            if (_searchRunner is null)
            {
                var filtersDir = Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") ?? "JamlFilters";
                _searchRunner = new SearchRunner(filtersDir, GetStore());
            }
            return _searchRunner;
        }
    }

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
        Run the SIMD or OpenCL GPU search engine over a JAML filter and return its matching seeds.
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
        CancellationToken cancellationToken = default,
        string? deck = null,
        string? stake = null,
        int? minScore = null,
        [Range(1, 256)] int? threads = null,
        [Range(1, MaxSequentialBatches)] int? batchCount = null,
        bool useGpu = false,
        bool saveToDb = true
    )
    {
        int? effectiveBatches = batchCount ?? batches;
        var source = ValidateModeCombo(mode, seeds, keyword, randomCount, startBatch, effectiveBatches);
        var (text, label) = ReadFilterText(filter, jaml);
        var config = LoadConfig(text, label);

        var runner = GetSearchRunner();
        var who = user?.Identity?.Name ?? "anonymous";
        var matches = new List<string>();
        IReadOnlyList<SeedMatch>? topMatches = null;
        IReadOnlyList<string>? tallyLabels = null;
        bool gpuUsed = false;
        string? deviceName = null;
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
            endBatch = Math.Min(start + (effectiveBatches ?? DefaultSequentialBatches), maxBatch);
            nextBatch = endBatch < maxBatch ? endBatch : null;
        }

        string resolvedFilterName = filter ?? config.Name ?? "inline";
        await foreach (
            var evt in runner.RunAsync(
                config,
                label,
                source.RunnerMode,
                collect,
                startBatch,
                endBatch,
                cancellationToken,
                deck: deck,
                stake: stake,
                minScore: minScore,
                useGpu: useGpu,
                saveToDb: saveToDb,
                foundBy: who,
                filterName: resolvedFilterName,
                threads: threads,
                batchCount: effectiveBatches
            )
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
                    topMatches = d.TopMatches;
                    tallyLabels = d.TallyLabels;
                    gpuUsed = d.GpuUsed;
                    deviceName = d.DeviceName;
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
            Filter: resolvedFilterName,
            Mode: mode.ToString().ToLowerInvariant(),
            Matched: matched,
            Searched: total,
            DurationMs: Math.Round(elapsed.TotalMilliseconds, 1),
            SeedsPerSecond: Math.Round(rate, 1),
            Matches: matches,
            Note: note,
            RequestedBy: who,
            NextBatch: nextBatch,
            GpuUsed: gpuUsed,
            DeviceName: deviceName,
            TopMatches: topMatches,
            TallyLabels: tallyLabels
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

    [McpServerTool(
        Name = "seed_locate",
        Title = "Find where an item is in a seed",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        """
        Find every ante and place one item appears in a seed: shop card #N, a pack's card, a Soul
        (legendary, in run order), an Uncommon/Rare Tag pull, Judgement, Wraith, Emperor, Purple
        Seal, Sixth Sense, Seance or Riff-raff pull, the boss, the voucher, or a blind tag.
        """
    )]
    public static SeedLocateResult Locate(
        string seed,
        string item,
        string? deck = null,
        string? stake = null,
        int[]? antes = null,
        int? rolls = null
    )
    {
        var motelyDeck = ParseEnum(deck, MotelyDeck.Red, "deck");
        var motelyStake = ParseEnum(stake, MotelyStake.White, "stake");
        var list = ResolveSeeds(seed);
        if (list.Length != 1)
            throw new ArgumentException(
                $"Error: seed_locate reads one seed per call, got {list.Length}; call it once per seed."
            );
        int minAnte = antes is { Length: > 0 } ? antes.Min() : 1;
        int maxAnte = antes is { Length: > 0 } ? antes.Max() : 8;

        MotelyLocateResult result;
        try
        {
            result = MotelySeedLocator.Locate(list[0], motelyDeck, motelyStake, item, minAnte, maxAnte, rolls ?? 20);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"Error: item: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Error: {ex.Message}", ex);
        }

        return new(
            result.Seed,
            motelyDeck.ToString(),
            motelyStake.ToString(),
            result.Name,
            result.Depth,
            [
                .. result.Hits.Select(h => new SeedLocateHit(
                    h.Ante,
                    h.Source.ToString(),
                    h.Position,
                    h.Where,
                    h.Item,
                    h.Edition.ToString()
                )),
            ]
        );
    }

    [McpServerTool(
        Name = "seed_analyze_batch",
        Title = "Analyze and optionally score up to 50 seeds in structured format",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        """
        Analyze up to 50 seeds (comma or newline separated) and return structured summaries of
        bosses, vouchers, tags, and Soul jokers across Antes 1-8, optionally scoring and comparing
        them against a JAML filter (`filter` or `jaml`).
        """
    )]
    public static BatchAnalyzeSummary AnalyzeBatch(
        string seeds,
        string? deck = null,
        string? stake = null,
        string? filter = null,
        string? jaml = null
    ) => GetSearchRunner().AnalyzeBatch(seeds, deck, stake, filter, jaml);

    [McpServerTool(
        Name = "gpu_status",
        Title = "Inspect OpenCL GPU devices and run a live FP64 seed benchmark",
        ReadOnly = true,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description(
        """
        Discover OpenCL devices on the host, report FP64 capability, compute units, global memory,
        and run a live GPU seed hash/PRNG benchmark (`GpuSeedSearcher.Benchmark`).
        """
    )]
    public static GpuStatusResult GpuStatus(int batchChars = 3, int batchCount = 64)
    {
        var devices = GpuDeviceInfo.DiscoverDevices();
        var benchmark = GpuSeedSearcher.Benchmark(Math.Clamp(batchChars, 2, 4), Math.Clamp(batchCount, 1, 1024));
        return new GpuStatusResult(devices.Count, devices, benchmark);
    }

    [McpServerTool(
        Name = "recent_finds",
        Title = "List recently discovered seeds from the database",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Return recently discovered seeds persisted in the Motely database, optionally filtered by filter name.")]
    public static Task<IReadOnlyList<DiscoveredSeedRecord>> RecentFinds(
        string? filterName = null,
        int limit = 50,
        CancellationToken cancellationToken = default
    ) => GetStore().GetRecentFindsAsync(filterName, Math.Clamp(limit, 1, 500), cancellationToken);

    [McpServerTool(
        Name = "leaderboard",
        Title = "Top-scoring seeds discovered for a filter",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Return the highest-scoring seeds persisted for a filter in the Motely database.")]
    public static Task<IReadOnlyList<DiscoveredSeedRecord>> Leaderboard(
        string? filterName = null,
        string? deck = null,
        string? stake = null,
        int limit = 25,
        CancellationToken cancellationToken = default
    ) => GetStore().GetLeaderboardAsync(filterName, deck, stake, Math.Clamp(limit, 1, 500), cancellationToken);

    [McpServerTool(
        Name = "saved_filters",
        Title = "List filters stored in the database",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Return all JAML filters stored in the Motely database.")]
    public static Task<IReadOnlyList<SavedFilterRecord>> SavedFilters(CancellationToken cancellationToken = default) =>
        GetStore().GetFiltersAsync(cancellationToken);

    [McpServerTool(
        Name = "save_filter",
        Title = "Validate and save a JAML filter to the database",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Validate a JAML filter YAML document and persist it in the Motely database.")]
    public static async Task<SavedFilterRecord> SaveFilter(
        string name,
        string jamlYaml,
        string? author = null,
        string? description = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Error: filter name is required.");
        if (!FilterCheck.TryCheck(jamlYaml, out var config, out _, out var error) || config is null)
            throw new InvalidOperationException($"Error: {error}");

        var now = DateTimeOffset.UtcNow;
        var stored = new SavedFilterRecord(
            Slug: name.Trim(),
            Name: config.Name ?? name.Trim(),
            Author: author ?? config.Author,
            Description: description ?? config.Description,
            Deck: config.Deck.ToString(),
            Stake: config.Stake.ToString(),
            JamlContent: jamlYaml,
            CreatedAtUtc: now,
            UpdatedAtUtc: now
        );
        return await GetStore().SaveFilterAsync(stored, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(
        Name = "party_create",
        Title = "Create a Community Party Search",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Create a coordinated Community Party Search for a named filter or inline JAML.")]
    public static Task<PartyStatusDto> PartyCreate(
        string filterOrJaml,
        string? name = null,
        string? deck = null,
        string? stake = null,
        int batchChars = 3,
        long blockSize = 10,
        long? totalBlocks = null,
        int? targetMatches = null,
        CancellationToken cancellationToken = default
    ) =>
        GetPartyCoordinator()
            .CreatePartyAsync(
                new CreatePartyRequest(filterOrJaml, name, deck, stake, batchChars, blockSize, totalBlocks, targetMatches),
                cancellationToken
            );

    [McpServerTool(
        Name = "party_list",
        Title = "List active and persisted Community Party Searches",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("List all Community Party Searches and their current progress.")]
    public static Task<IReadOnlyList<PartyStatusDto>> PartyList(CancellationToken cancellationToken = default) =>
        GetPartyCoordinator().ListPartiesAsync(cancellationToken);

    [McpServerTool(
        Name = "party_status",
        Title = "Get status of a Community Party Search",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Get detailed progress, throughput, and top finds for a Community Party Search.")]
    public static async Task<PartyStatusDto> PartyStatus(
        string partyId,
        CancellationToken cancellationToken = default
    ) =>
        await GetPartyCoordinator().GetPartyStatusAsync(partyId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException($"Error: party '{partyId}' not found.");

    [McpServerTool(
        Name = "party_sweep_gpu",
        Title = "Sweep blocks in a Community Party Search using the host GPU",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true
    )]
    [Description("Lease and sweep `blocks` batch blocks in a Community Party Search using OpenCL GPU acceleration.")]
    public static Task<PartyGpuSweepResultDto> PartySweepGpu(
        string partyId,
        long? blocks = 10,
        bool useGpu = true,
        int? threads = null,
        CancellationToken cancellationToken = default
    ) => GetPartyCoordinator().SweepBlocksAsync(partyId, blocks, useGpu, threads, cancellationToken);

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
    /// The filter's text and a label for errors: a named or path filter is read from disk (or database),
    /// inline JAML is used as is. Exactly one of the two must be given.
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
                "Error: pass 'filter' (a name from filter_list, or an absolute path) or 'jaml' (the filter's JAML text)."
            );

        // A pasted filter body in the name field: the path lookup would only report "no filter at
        // <the whole JAML>".
        if (filter!.Contains('\n'))
            throw new ArgumentException("Error: 'filter' looks like JAML text; pass it as 'jaml' instead.");

        var path = FilterCheck.ResolvePath(filter);
        if (File.Exists(path))
            return (File.ReadAllText(path), path);

        var stored = GetStore().GetFilterAsync(filter).GetAwaiter().GetResult();
        if (stored is not null)
            return (stored.JamlContent, stored.Name);

        throw new InvalidOperationException(
            $"Error: no filter at {path}. filter_list shows the available names; "
                + "or pass your own filter's text as 'jaml'."
        );
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
