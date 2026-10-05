using Motely;
using Motely.Analysis;
using Motely.Enums;
using Motely.SeedProviders;

/// <summary>
/// One search, configured fluently and run with <see cref="Start"/>. A class, so JS holds it by
/// reference (guide: interop-instances). The build is single threaded: one search thread.
/// </summary>
public sealed class SearchSettings
{
    private readonly IMotelySearchSettings _settings;
    private readonly int[] _antes;
    private readonly SearchModule _module;
    private CancellationTokenSource? _cancel;
    private IMotelySearch? _search;
    private string? _error;

    internal SearchSettings(IMotelySearchSettings settings, int[] antes, SearchModule module)
    {
        _antes = antes;
        _module = module;
        settings = settings.WithThreadCount(1).WithQuietMode(true).WithProgressCallback(module.Progress);
        // A scored search reports every find on both the seed-match and the scored channel.
        // Listen to exactly one, or JS sees each find twice.
        _settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(t =>
                module.Scored(new MotelySeedScore(t.Seed, t.Score, t.TallyValuesSpan.ToArray())))
            : settings.WithSeedMatchCallback(s => module.Scored(new MotelySeedScore(s, 1, [])));
    }

    /// <summary>Runs the Jamlyzer on every find in the same pass; each result arrives on
    /// Search.onAnalyzed. eventRolls 0 is the per-ante summary only, 20 matches Analyze.seeds.</summary>
    public SearchSettings WithAnalysis(int eventRolls) =>
        Apply(s => s.WithSeedAnalyzeProvider(new MotelyJamlyzerRiderDesc(_antes, _module.Analyzed, eventRolls)));

    public SearchSettings WithSequentialSearch() => Apply(s => s.WithSequentialSearch());
    public SearchSettings WithBatchCharacterCount(int count) => Apply(s => s.WithBatchCharacterCount(count));
    public SearchSettings WithStartBatchIndex(long startBatchIndex) => Apply(s => s.WithStartBatchIndex(startBatchIndex));
    public SearchSettings WithEndBatchIndex(long endBatchIndex) => Apply(s => s.WithEndBatchIndex(endBatchIndex));
    public SearchSettings WithSeedList(string[] seeds) => Apply(s => s.WithSeedList(seeds));
    public SearchSettings WithRandomSearch(int count) => Apply(s => s.WithRandomSearch(count));
    public SearchSettings WithKeywordSearch(string[] keywords, bool quickPad) => Apply(s => s.WithKeywordSearch(keywords, quickPad ? JamlAesthetics.QuickPaddingChars : null));
    public SearchSettings WithDeck(MotelyDeck deck) => Apply(s => s.WithDeck(deck));
    public SearchSettings WithStake(MotelyStake stake) => Apply(s => s.WithStake(stake));
    public SearchSettings WithProgressReportIntervalMs(long ms) => Apply(s => s.WithProgressReportIntervalMs(ms));
    public SearchSettings WithAutoScoreCutoff(bool enabled) => Apply(s => s.WithAutoScoreCutoff(enabled));
    public SearchSettings StopAfter(long matchCount) => Apply(s => s.StopAfter(matchCount));

    /// <summary>
    /// Runs until the seed space is exhausted, the match limit hits or <see cref="Cancel"/> is
    /// called; a cancelled run resolves too. When it rejects, <see cref="Error"/> holds the reason.
    /// </summary>
    public async Task Start()
    {
        if (_search is { IsCompleted: false })
            throw new InvalidOperationException("This search is already running.");
        _error = null;
        _search?.Dispose();
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        try
        {
            _search = _settings.Start(_cancel.Token);
            await _search.WaitForCompletionAsync();
        }
        catch (Exception e)
        {
            _error = Reason(e);
            throw;
        }
    }

    /// <summary>Applies one setting. A rejected value throws here and leaves its reason on
    /// <see cref="Error"/>, since the exception reaches JS without its message.</summary>
    private SearchSettings Apply(Action<IMotelySearchSettings> set)
    {
        _error = null;
        try
        {
            set(_settings);
            return this;
        }
        catch (Exception e)
        {
            _error = Reason(e);
            throw;
        }
    }

    /// <summary>The message's first line. The trimmed runtime appends resource keys after it.</summary>
    private static string Reason(Exception e) => e.Message.Split('\n')[0];

    /// <summary>Stops the running search; its Start resolves.</summary>
    public void Cancel() => _cancel?.Cancel();

    /// <summary>Why the last call on these settings threw, or null.</summary>
    public string? Error => _error;

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
