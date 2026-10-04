using Bootsharp;
using Motely;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters;
using Motely.Filters.Jaml;
using Motely.SeedProviders;

/// <summary>
/// Search host. <c>Search.settings(jaml)</c> returns the engine's fluent settings; chain
/// <c>with*</c> calls, then <c>start(token)</c>. Finds arrive on <see cref="OnScored"/>, progress on
/// <see cref="OnProgress"/>, and with <c>withAnalysis(n)</c> each find's Jamlyzer breakdown follows
/// on <see cref="OnAnalyzed"/>.
/// </summary>
public static partial class Search
{
    [Export] public static event Action<MotelyProgress>? OnProgress;
    [Export] public static event Action<MotelySeedScore>? OnScored;
    [Export] public static event Action<MotelyJamlyzerSeedResult>? OnAnalyzed;

    [Export]
    public static SearchSettings Settings(string jaml)
    {
        var config = JamlConfigLoader.FromJaml(jaml);
        // Read the ante window before CreateSettings fills unscoped clauses, so withAnalysis walks
        // the same antes Analyze.seeds does.
        int[] antes = MotelyJamlyzer.ComputeAntes(config);
        return new(JamlSearchBuilder.CreateSettings(config), antes);
    }

    internal static void Progress(MotelyProgress p) => OnProgress?.Invoke(p);
    internal static void Scored(MotelySeedScore s) => OnScored?.Invoke(s);
    internal static void Analyzed(MotelyJamlyzerSeedResult r) => OnAnalyzed?.Invoke(r);
}

/// <summary>The engine's search settings for JS. One thread: the build is single threaded.</summary>
public sealed class SearchSettings
{
    private readonly IMotelySearchSettings _settings;
    private readonly int[] _antes;
    private IMotelySearch? _search;

    internal SearchSettings(IMotelySearchSettings settings, int[] antes)
    {
        _antes = antes;
        settings = settings.WithThreadCount(1).WithQuietMode(true).WithProgressCallback(Search.Progress);
        // A scored search reports each find on both channels; listen to exactly one.
        _settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(static t =>
                Search.Scored(new MotelySeedScore(t.Seed, t.Score, t.TallyValuesSpan.ToArray())))
            : settings.WithSeedMatchCallback(static s => Search.Scored(new MotelySeedScore(s, 1, [])));
    }

    public SearchSettings WithAnalysis(int eventRolls)
    {
        _settings.WithSeedAnalyzeProvider(new MotelyJamlyzerRiderDesc(_antes, Search.Analyzed, eventRolls));
        return this;
    }

    public SearchSettings WithBatchCharacterCount(int n) { _settings.WithBatchCharacterCount(n); return this; }
    public SearchSettings WithProviderBatchSeedCount(int n) { _settings.WithProviderBatchSeedCount(n); return this; }
    public SearchSettings WithStartBatchIndex(long i) { _settings.WithStartBatchIndex(i); return this; }
    public SearchSettings WithEndBatchIndex(long i) { _settings.WithEndBatchIndex(i); return this; }
    public SearchSettings WithSeedList(string[] seeds) { _settings.WithSeedList(seeds); return this; }
    public SearchSettings WithRandomSearch(int count) { _settings.WithRandomSearch(count); return this; }
    public SearchSettings WithKeywordSearch(string[] keywords, bool quickPad) { _settings.WithKeywordSearch(keywords, quickPad ? JamlAesthetics.QuickPaddingChars : null); return this; }
    public SearchSettings WithSequentialSearch() { _settings.WithSequentialSearch(); return this; }
    public SearchSettings WithDeck(MotelyDeck deck) { _settings.WithDeck(deck); return this; }
    public SearchSettings WithStake(MotelyStake stake) { _settings.WithStake(stake); return this; }
    public SearchSettings WithProgressReportIntervalMs(long ms) { _settings.WithProgressReportIntervalMs(ms); return this; }
    public SearchSettings WithAutoScoreCutoff(bool enabled) { _settings.WithAutoScoreCutoff(enabled); return this; }
    public SearchSettings StopAfter(long matchCount) { _settings.StopAfter(matchCount); return this; }

    /// <summary>Runs until the space is exhausted, the match limit hits or the token fires.
    /// A cancelled run resolves too.</summary>
    public async Task Start(CancellationToken cancellationToken)
    {
        if (_search is not null && !_search.IsCompleted)
            throw new InvalidOperationException("This search is already running.");
        _search?.Dispose();
        _search = _settings.Start(cancellationToken);
        await _search.WaitForCompletionAsync();
    }

    public bool IsCompleted => _search?.IsCompleted ?? false;
    public bool StoppedOnMatchLimit => _search?.StoppedOnMatchLimit ?? false;
    public long TotalSeedsSearched => _search?.TotalSeedsSearched ?? 0;
    public long MatchingSeeds => _search?.MatchingSeeds ?? 0;
    public long ElapsedMs => _search?.ElapsedMs ?? 0;
    public double SeedsPerSecond => _search?.SeedsPerSecond ?? 0;
    public long TotalBatchCount => _search?.TotalBatchCount ?? 0;
    public long CompletedBatchCount => _search?.CompletedBatchCount ?? 0;
    public long ResumeBatchIndex => _search?.ResumeBatchIndex ?? -1;
}
