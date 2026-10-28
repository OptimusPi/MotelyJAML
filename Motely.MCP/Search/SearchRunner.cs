using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Motely;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters;
using Motely.Gpu;
using Motely.MCP.Data;

namespace Motely.MCP.Search;

/// <summary>Structured scored match from a search run.</summary>
public sealed record SeedMatch(string Seed, int Score, int[] Tallies);

/// <summary>Structured summary of one seed in a batch analysis.</summary>
public sealed record BatchSeedSummary(
    string Seed,
    string Deck,
    string Stake,
    IReadOnlyList<string> Bosses,
    IReadOnlyList<string> Vouchers,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> SoulJokers,
    string? Error = null
);

/// <summary>Structured result of <see cref="SearchRunner.AnalyzeBatch"/>.</summary>
public sealed record BatchAnalyzeSummary(int Count, IReadOnlyList<BatchSeedSummary> Seeds);

/// <summary>
/// What a search yields as it runs: live progress, each matched seed, a terminal summary, or an
/// error. The engine's callbacks are synchronous and fire on its own threads, so
/// <see cref="SearchRunner"/> bridges them to async callers over a channel.
/// </summary>
public abstract record SearchEvent
{
    private SearchEvent() { }

    public sealed record Progress(int Percent, long Searched, long Matched, double Rate) : SearchEvent;

    public sealed record Match(string Seed, double Score, int[]? Tallies = null) : SearchEvent;

    public sealed record Done(
        long TotalSearched,
        long Matched,
        TimeSpan Elapsed,
        double Rate,
        bool StoppedOnLimit,
        IReadOnlyList<string> Matches,
        IReadOnlyList<SeedMatch>? TopMatches = null,
        IReadOnlyList<string>? TallyLabels = null,
        bool GpuUsed = false,
        string? DeviceName = null
    ) : SearchEvent;

    public sealed record Error(string Message) : SearchEvent;
}

/// <summary>
/// Shared search execution behind both the MCP <c>search</c> tool and the AG-UI agent: resolve a
/// filter, load it, build a plan, apply a mode, then run the engine and stream progress + matches.
/// </summary>
public sealed class SearchRunner
{
    private readonly string? _filtersDir;
    private readonly IMotelyStore? _store;

    public SearchRunner(string? filtersDir = null, IMotelyStore? store = null)
    {
        _filtersDir = filtersDir;
        _store = store;
    }

    /// <summary>Matches a run stops after when the caller gives no <c>collect</c>.</summary>
    public const int DefaultCollect = 100;

    /// <summary>The most matches one remote run may collect, whatever <c>collect</c> asks for.</summary>
    public const int MaxCollect = 10_000;

    /// <summary>
    /// Yields progress, matches and a terminal summary for <paramref name="filter"/>. A null or
    /// empty <paramref name="mode"/> replays the filter's saved seeds (fast, deterministic);
    /// "seeds &lt;list or file&gt;", "keyword &lt;kw&gt;", "random &lt;n&gt;" and "sequential" select
    /// the other engines.
    /// </summary>
    public async IAsyncEnumerable<SearchEvent> RunAsync(
        string filter,
        string? mode,
        int? collect,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var path = FilterCheck.ResolvePath(filter);
        JamlConfig? config = null;
        string label = path;

        if (File.Exists(path))
        {
            if (!TryLoadConfig(path, out config, out var loadError))
            {
                yield return new SearchEvent.Error(loadError);
                yield break;
            }
        }
        else if (_store is not null)
        {
            var stored = await _store.GetFilterAsync(filter, cancellationToken).ConfigureAwait(false);
            if (stored is not null)
            {
                if (!JamlConfigLoader.TryLoad(stored.JamlContent, out config, out var loadError) || config is null)
                {
                    yield return new SearchEvent.Error(loadError ?? $"invalid stored filter '{filter}'");
                    yield break;
                }
                label = stored.Name;
            }
        }

        if (config is null)
        {
            yield return new SearchEvent.Error($"no filter at {path}");
            yield break;
        }

        await foreach (
            var evt in RunAsync(config, label, mode, collect, null, null, cancellationToken, filterName: filter)
                .ConfigureAwait(false)
        )
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Runs an already-loaded filter. <paramref name="label"/> names it in errors (a path, or
    /// "inline JAML"). A sequential sweep given <paramref name="endBatch"/> searches exactly the
    /// batches [startBatch, endBatch) to the end, with no collect stop: a run that stopped part way
    /// through its batches could only be resumed by repeating or skipping seeds.
    /// </summary>
    public async IAsyncEnumerable<SearchEvent> RunAsync(
        JamlConfig config,
        string label,
        string? mode,
        int? collect,
        long? startBatch,
        long? endBatch,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        string? deck = null,
        string? stake = null,
        int? minScore = null,
        bool useGpu = false,
        bool saveToDb = true,
        string? foundBy = null,
        string? filterName = null
    )
    {
        if (!string.IsNullOrWhiteSpace(deck) && Enum.TryParse<MotelyDeck>(deck.Trim(), true, out var parsedDeck))
            config.Deck = parsedDeck;
        if (!string.IsNullOrWhiteSpace(stake) && Enum.TryParse<MotelyStake>(stake.Trim(), true, out var parsedStake))
            config.Stake = parsedStake;

        if (!TryCreatePlan(config, minScore, out var plan, out var planError))
        {
            yield return new SearchEvent.Error(planError);
            yield break;
        }

        var (keyword, random, replay, seeds, modeError) = ParseMode(mode);
        if (modeError is not null)
        {
            yield return new SearchEvent.Error(modeError);
            yield break;
        }

        var input = new CliSearchMode.Input(
            SeedsArgument: seeds,
            Replay: replay,
            JamlPath: label,
            FilterId: config.Id,
            JamlSeeds: config.Seeds,
            KeywordInputs: keyword is not null ? [keyword] : [],
            PaddingCharsOption: null,
            RandomCount: random,
            AestheticName: null,
            StartBatch: startBatch,
            EndBatch: endBatch,
            StartPercent: null,
            StartSeed: null,
            StopSeed: null,
            BatchCharacterCount: null
        );

        var baseSettings = plan.Settings.WithSourceConfig(config).WithGpuSearch(useGpu);

        if (!CliSearchMode.TryApplySearchMode(baseSettings, input, out var applyError, out var settings))
        {
            yield return new SearchEvent.Error(applyError);
            yield break;
        }

        // Remote callers always get a cap: a sequential run with no limit walks the whole seed
        // space (~2.3 trillion seeds) and would hold the request, and the match list, forever.
        // A replay or seed list is finite, so by default it runs to the end of the list.
        bool finite = replay || seeds is not null;
        if (endBatch is null)
            settings = settings.StopAfter(Math.Clamp(collect ?? (finite ? MaxCollect : DefaultCollect), 1, MaxCollect));

        var channel = Channel.CreateUnbounded<SearchEvent>();
        var matches = new List<string>();
        var scoredMatches = new List<SeedMatch>();

        settings = settings.WithProgressCallback(p =>
            channel.Writer.TryWrite(
                new SearchEvent.Progress(
                    (int)p.PercentComplete,
                    p.SeedsSearched,
                    p.MatchingSeeds,
                    p.SeedsPerMillisecond * 1000.0
                )
            )
        );

        // A filter with no clauses has no score provider, and then finds arrive only on the
        // seed-match channel. With a provider they arrive on both, so listen to exactly one.
        if (settings.SeedScoreDesc is not null)
            settings = settings.WithScoredResultCallback(r =>
            {
                int[] tallies = r.Tallies is { Length: > 0 } ? [.. r.Tallies] : [];
                lock (matches)
                {
                    matches.Add(r.Seed);
                    scoredMatches.Add(new SeedMatch(r.Seed, r.Score, tallies));
                }
                channel.Writer.TryWrite(new SearchEvent.Match(r.Seed, r.Score, tallies));
            });
        else
            settings = settings.WithSeedMatchCallback(line =>
            {
                lock (matches)
                {
                    matches.Add(line);
                    scoredMatches.Add(new SeedMatch(line, 0, []));
                }
                channel.Writer.TryWrite(new SearchEvent.Match(line, 0, []));
            });

        bool gpuUsed = false;
        string? deviceName = null;
        if (useGpu && endBatch is not null)
        {
            var dev = GpuDeviceInfo.GetBestDevice();
            if (dev is not null && GpuFilterProgram.Compile(config).HasSelectiveOps)
            {
                gpuUsed = true;
                deviceName = dev.Name;
            }
        }

        using var search = settings.Start(cancellationToken);
        _ = search
            .WaitForCompletionAsync(cancellationToken)
            .ContinueWith(
                t =>
                {
                    if (t.IsFaulted && t.Exception is not null && !cancellationToken.IsCancellationRequested)
                        channel.Writer.TryWrite(
                            new SearchEvent.Error(
                                t.Exception.Flatten().InnerException?.Message ?? "the search failed."
                            )
                        );
                    channel.Writer.TryComplete();
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default
            );

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return evt;

        List<SeedMatch> topSnapshot;
        lock (matches)
        {
            topSnapshot = [.. scoredMatches.OrderByDescending(m => m.Score).ThenBy(m => m.Seed, StringComparer.Ordinal)];
        }

        if (_store is not null && saveToDb && topSnapshot.Count > 0)
        {
            string resolvedFilterName = filterName ?? config.Name ?? label;
            string resolvedFoundBy = string.IsNullOrWhiteSpace(foundBy) ? "anonymous" : foundBy;
            var now = DateTimeOffset.UtcNow;
            var records = topSnapshot
                .Take(MaxCollect)
                .Select(m => new DiscoveredSeedRecord(
                    Seed: m.Seed,
                    FilterSlug: resolvedFilterName,
                    FilterName: config.Name ?? resolvedFilterName,
                    Score: m.Score,
                    Tallies: m.Tallies,
                    Deck: config.Deck.ToString(),
                    Stake: config.Stake.ToString(),
                    PartyId: null,
                    WorkerId: resolvedFoundBy,
                    FoundAtUtc: now
                ));
            await _store.RecordSeedsAsync(records, cancellationToken).ConfigureAwait(false);
        }

        yield return new SearchEvent.Done(
            search.TotalSeedsSearched,
            search.MatchingSeeds,
            TimeSpan.FromMilliseconds(search.ElapsedMs),
            search.SeedsPerSecond,
            search.StoppedOnMatchLimit,
            [.. matches],
            topSnapshot,
            plan.TallyLabels,
            gpuUsed,
            deviceName
        );
    }

    /// <summary>
    /// Analyzes a batch of seeds (up to 50) and returns structured summaries of bosses, vouchers,
    /// tags, and Soul jokers across Antes 1-8.
    /// </summary>
    public BatchAnalyzeSummary AnalyzeBatch(string seedsCsvOrLines, string? deck = null, string? stake = null)
    {
        var motelyDeck = Enum.TryParse<MotelyDeck>(deck?.Trim(), true, out var d) ? d : MotelyDeck.Red;
        var motelyStake = Enum.TryParse<MotelyStake>(stake?.Trim(), true, out var s) ? s : MotelyStake.White;

        var rawList = seedsCsvOrLines
            .Split([',', '\n', '\r', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.Equals("seed", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();

        var results = new List<BatchSeedSummary>(rawList.Length);
        foreach (var raw in rawList)
        {
            if (!CliSearchMode.TryNormalizeSeed(raw, out var seed))
            {
                results.Add(
                    new BatchSeedSummary(raw, motelyDeck.ToString(), motelyStake.ToString(), [], [], [], [], $"Invalid seed '{raw}'")
                );
                continue;
            }

            var analysis = MotelyUnitTestAnalyzer.Analyze(new(seed, motelyDeck, motelyStake));
            if (!string.IsNullOrEmpty(analysis.Error))
            {
                results.Add(
                    new BatchSeedSummary(seed, motelyDeck.ToString(), motelyStake.ToString(), [], [], [], [], analysis.Error)
                );
                continue;
            }

            var bosses = new List<string>();
            var vouchers = new List<string>();
            var tags = new List<string>();
            foreach (var ante in analysis.Antes.OrderBy(a => a.Ante))
            {
                bosses.Add($"A{ante.Ante}:{ante.Boss}");
                vouchers.Add($"A{ante.Ante}:{ante.Voucher}");
                tags.Add($"A{ante.Ante}:{ante.SmallBlindTag}/{ante.BigBlindTag}");
            }

            var soulJokers = new List<string>();
            foreach (var legendary in Enum.GetNames<MotelyJokerLegendary>())
            {
                var loc = MotelySeedLocator.Locate(seed, motelyDeck, motelyStake, legendary, 1, 8, 5);
                foreach (var hit in loc.Hits.Where(h => h.Source == MotelyLocateSource.Soul))
                {
                    soulJokers.Add($"A{hit.Ante}:{hit.Item}");
                }
            }

            results.Add(
                new BatchSeedSummary(
                    seed,
                    motelyDeck.ToString(),
                    motelyStake.ToString(),
                    bosses,
                    vouchers,
                    tags,
                    soulJokers,
                    null
                )
            );
        }

        return new BatchAnalyzeSummary(results.Count, results);
    }

    private static bool TryLoadConfig(
        string path,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JamlConfig? config,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error
    )
    {
        config = null;
        error = null;
        try
        {
            config = JamlConfigLoader.FromFile(path);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = $"{path}: {ex.Message}";
            return false;
        }
    }

    private static bool TryCreatePlan(
        JamlConfig config,
        int? engineCutoff,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JamlSearchPlan? plan,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error
    )
    {
        plan = null;
        error = null;
        try
        {
            plan = MotelySearchBuilder.CreatePlan(config, engineCutoff);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Splits a mode string into (keyword, random, replay, seeds); exactly one is set, or an error.</summary>
    private static (string? keyword, int? random, bool replay, string? seeds, string? error) ParseMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode == "replay")
            return (null, null, true, null, null);

        if (mode.StartsWith("seeds ", StringComparison.OrdinalIgnoreCase))
        {
            var list = mode["seeds ".Length..].Trim();
            return list.Length == 0
                ? (null, null, false, null, "Error: 'seeds' needs comma-separated seeds or a seed file path after it.")
                : (null, null, false, list, null);
        }

        if (mode.StartsWith("keyword ", StringComparison.OrdinalIgnoreCase))
        {
            var kw = mode["keyword ".Length..].Trim();
            return kw.Length == 0
                ? (null, null, false, null, "Error: 'keyword' needs a seed word after it.")
                : (kw, null, false, null, null);
        }

        if (mode.StartsWith("random ", StringComparison.OrdinalIgnoreCase))
        {
            var n = mode["random ".Length..].Trim();
            return int.TryParse(n, out var count) && count > 0
                ? (null, count, false, null, null)
                : (null, null, false, null, $"Error: 'random' needs a positive count, got '{n}'.");
        }

        if (mode == "sequential")
            return (null, null, false, null, null);

        return (
            null,
            null,
            false,
            null,
            $"Error: unknown mode '{mode}'. Use replay, seeds <list or file>, keyword <kw>, random <n>, or sequential."
        );
    }
}
