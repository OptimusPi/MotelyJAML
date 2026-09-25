using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Motely.Filters;

namespace Motely;

public interface IMotelySeedFilter
{
    public VectorMask Filter(ref MotelyVectorSearchContext searchContext);
}

public interface IMotelySeedFilterDesc
{
    public IMotelySeedFilter CreateFilter(ref MotelyFilterCreationContext ctx);
}

public interface IMotelySeedFilterDesc<TFilter> : IMotelySeedFilterDesc
    where TFilter : struct, IMotelySeedFilter
{
    public new TFilter CreateFilter(ref MotelyFilterCreationContext ctx);

    IMotelySeedFilter IMotelySeedFilterDesc.CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        return CreateFilter(ref ctx);
    }
}

public interface IMotelySeedScoreDesc
{
    public IMotelySeedScoreProvider CreateScoreProvider(ref MotelyFilterCreationContext ctx);
}

public interface IMotelySeedScoreDesc<TScoreProvider> : IMotelySeedScoreDesc
    where TScoreProvider : struct, IMotelySeedScoreProvider
{
    public new TScoreProvider CreateScoreProvider(ref MotelyFilterCreationContext ctx);

    IMotelySeedScoreProvider IMotelySeedScoreDesc.CreateScoreProvider(
        ref MotelyFilterCreationContext ctx
    )
    {
        return CreateScoreProvider(ref ctx);
    }
}

public interface IMotelySeedScores
{
    string Seed { get; }
    int Score { get; }
    byte[] Tally { get; }
}

public interface IMotelySeedScoreProvider
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorMask Score(
        ref MotelyVectorSearchContext searchContext,
        MotelyScoredSeedResult[] buffer,
        VectorMask baseFilterMask,
        int scoreThreshold = 0
    );
}

public interface IMotelySeedAnalyzeDesc
{
    public IMotelySeedAnalyzeProvider CreateAnalyzeProvider(ref MotelyFilterCreationContext ctx);
}

public interface IMotelySeedAnalyzeDesc<TAnalyzeProvider> : IMotelySeedAnalyzeDesc
    where TAnalyzeProvider : struct, IMotelySeedAnalyzeProvider
{
    public new TAnalyzeProvider CreateAnalyzeProvider(ref MotelyFilterCreationContext ctx);

    IMotelySeedAnalyzeProvider IMotelySeedAnalyzeDesc.CreateAnalyzeProvider(
        ref MotelyFilterCreationContext ctx
    )
    {
        return CreateAnalyzeProvider(ref ctx);
    }
}

public interface IMotelySeedAnalyzeProvider
{
    void Analyze(
        ref MotelyVectorSearchContext searchContext,
        VectorMask reportedMask,
        MotelyScoredSeedResult[]? scores
    );
}

public interface IMotelySeedRouter
{
    public void InjectSingleSeedContext(in MotelySingleSearchContext ctx);
}

public interface IMotelySeedRouterDesc
{
    public IMotelySeedRouter CreateSeedRouter(ref MotelyFilterCreationContext ctx);
}

public interface IMotelySeedRouterDesc<TProvider> : IMotelySeedRouterDesc
    where TProvider : struct, IMotelySeedRouter
{
    public new TProvider CreateSeedRouter(ref MotelyFilterCreationContext ctx);

    IMotelySeedRouter IMotelySeedRouterDesc.CreateSeedRouter(ref MotelyFilterCreationContext ctx)
    {
        return CreateSeedRouter(ref ctx);
    }
}

public enum MotelySearchMode
{
    Sequential,
    Provider,
}

public interface IMotelySearchSettings
{
    IMotelySeedFilterDesc BaseFilterDescBase { get; }
    IList<IMotelySeedFilterDesc>? AdditionalFilters { get; }
    IMotelySearchSettings WithAdditionalFilter(IMotelySeedFilterDesc filterDesc);
    IMotelySearchSettings WithThreadCount(int threadCount);
    IMotelySearchSettings WithBatchCharacterCount(int batchCharacterCount);
    IMotelySearchSettings WithProviderBatchSeedCount(int seedCount);
    IMotelySearchSettings WithStartBatchIndex(long startBatchIndex);
    IMotelySearchSettings WithEndBatchIndex(long endBatchIndex);
    IMotelySearchSettings WithSeedScoreProvider(IMotelySeedScoreDesc seedScoreDesc);
    IMotelySearchSettings WithSeedAnalyzeProvider(IMotelySeedAnalyzeDesc seedAnalyzeDesc);
    IMotelySearchSettings WithSeedRouter(IMotelySeedRouterDesc desc);
    IMotelySearchSettings WithSeedGenerator(IEnumerable<string> seeds, int seedCount = -1);

    IMotelySearchSettings WithSeedList(string[] seeds);
    IMotelySearchSettings WithRandomSearch(int count);
    IMotelySearchSettings WithKeywordSearch(
        IReadOnlyList<string> keywords,
        char[]? paddingAlphabet = null
    );
    IMotelySearchSettings WithAestheticSearch(
        JamlAesthetic aesthetic,
        char[]? paddingAlphabet = null
    );
    IMotelySearchSettings WithProviderSearch(IMotelySeedProvider provider);
    IMotelySearchSettings WithSequentialSearch();
    IMotelySearchSettings WithDeck(MotelyDeck deck);
    IMotelySearchSettings WithStake(MotelyStake stake);
    IMotelySearchSettings WithProgressCallback(Action<MotelyProgress> callback);
    IMotelySearchSettings WithProgressReportIntervalMs(long intervalMs);
    IMotelySearchSettings WithCsvOutput(bool csvOutput);
    IMotelySearchSettings WithQuietMode(bool quietMode);
    IMotelySearchSettings WithSeedMatchCallback(Action<string> callback);
    IMotelySearchSettings WithScoredResultCallback(Action<MotelyScoredSeedResult> callback);
    IMotelySearchSettings WithBatchBoundaryCallback(Action callback);
    IMotelySearchSettings WithAutoScoreCutoff(bool enabled = true);
    IMotelySearchSettings StopAfter(long matchCount);

    IMotelySearchSettings WithJimmolate(MotelyIndividualSeedSearcher searcher, int scoreCutoff = 1);

    IMotelySearch CreateSearch();
    IMotelySearch Start(CancellationToken cancellationToken = default);
}

public sealed class MotelySearchSettings<TBaseFilter>(
    IMotelySeedFilterDesc<TBaseFilter> baseFilterDesc
) : IMotelySearchSettings
    where TBaseFilter : struct, IMotelySeedFilter
{
    public int ThreadCount { get; set; } = Environment.ProcessorCount;
    public long StartBatchIndex { get; set; } = 0;
    public long EndBatchIndex { get; set; } = long.MaxValue;

    public IMotelySeedFilterDesc<TBaseFilter> BaseFilterDesc { get; set; } = baseFilterDesc;

    public IMotelySeedFilterDesc BaseFilterDescBase => BaseFilterDesc;

    public IList<IMotelySeedFilterDesc>? AdditionalFilters { get; set; } = null;

    public IMotelySeedScoreDesc? SeedScoreDesc { get; set; } = null;

    public IMotelySeedAnalyzeDesc? SeedAnalyzeDesc { get; set; } = null;

    public IMotelySeedRouterDesc? SeedRouterDesc { get; set; } = null;

    public MotelySearchMode Mode { get; set; }

    public IMotelySeedProvider? SeedProvider { get; set; }

    public int SequentialBatchCharacterCount { get; set; } = 3;

    public int ProviderBatchSeedCount { get; set; } = MotelyGlobals.DefaultProviderBatchSeedCount;

    public MotelyDeck Deck { get; set; } = MotelyDeck.Red;
    public MotelyStake Stake { get; set; } = MotelyStake.White;

    public bool CsvOutput { get; set; } = false;
    public bool QuietMode { get; set; } = false;
    public bool AutoScoreCutoff { get; set; } = false;

    public long StopAfterMatches { get; set; } = 0;

    public Action<MotelyProgress>? ProgressCallback { get; set; }

    public long ProgressReportIntervalMs { get; set; } = 800;

    public Action<string>? SeedMatchCallback { get; set; }

    public Action<MotelyScoredSeedResult>? ScoredResultCallback { get; set; }

    public Action? BatchBoundaryCallback { get; set; }

    public MotelySearchSettings<TBaseFilter> WithThreadCount(int threadCount)
    {
        ThreadCount = threadCount;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithStartBatchIndex(long startBatchIndex)
    {
        StartBatchIndex = startBatchIndex;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithEndBatchIndex(long endBatchIndex)
    {
        EndBatchIndex = endBatchIndex;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithBatchCharacterCount(int batchCharacterCount)
    {
        SequentialBatchCharacterCount = batchCharacterCount;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithProviderBatchSeedCount(int seedCount)
    {
        if (seedCount < MotelyGlobals.MaxVectorWidth)
            seedCount = MotelyGlobals.MaxVectorWidth;
        ProviderBatchSeedCount = seedCount;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithSeedGenerator(
        IEnumerable<string> seeds,
        int seedCount = -1
    )
    {
        return WithProviderSearch(new MotelySeedListProvider(seeds, seedCount));
    }

    public MotelySearchSettings<TBaseFilter> WithSeedList(string[] seeds)
    {
        return WithSeedGenerator(seeds, seeds.Length);
    }

    public MotelySearchSettings<TBaseFilter> WithRandomSearch(int count)
    {
        return WithProviderSearch(new MotelyRandomSeedProvider(count));
    }

    public MotelySearchSettings<TBaseFilter> WithKeywordSearch(
        IReadOnlyList<string> keywords,
        char[]? paddingAlphabet = null
    )
    {
        var seedCount = MotelyGlobals.GetPaddedSeedCountForKeywordsLong(keywords, paddingAlphabet);
        return WithProviderSearch(
            new MotelySeedListProvider(
                MotelyGlobals.GeneratePaddedSeedsForKeywords(keywords, paddingAlphabet),
                seedCount
            )
        );
    }

    public MotelySearchSettings<TBaseFilter> WithAestheticSearch(
        JamlAesthetic aesthetic,
        char[]? paddingAlphabet = null
    )
    {
        if (aesthetic == JamlAesthetic.Repeater)
            return WithProviderSearch(new MotelyRepeaterSeedProvider(paddingAlphabet));
        return WithProviderSearch(new MotelyAestheticSeedProvider(aesthetic, paddingAlphabet));
    }

    public MotelySearchSettings<TBaseFilter> WithProviderSearch(IMotelySeedProvider provider)
    {
        SeedProvider = provider;
        Mode = MotelySearchMode.Provider;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithSequentialSearch()
    {
        SeedProvider = null;
        Mode = MotelySearchMode.Sequential;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithAdditionalFilter(IMotelySeedFilterDesc filterDesc)
    {
        AdditionalFilters ??= [];
        AdditionalFilters.Add(filterDesc);
        return this;
    }

    IMotelySearchSettings IMotelySearchSettings.WithAdditionalFilter(
        IMotelySeedFilterDesc filterDesc
    )
    {
        return WithAdditionalFilter(filterDesc);
    }

    public MotelySearchSettings<TBaseFilter> WithSeedScoreProvider(
        IMotelySeedScoreDesc seedScoreDesc
    )
    {
        SeedScoreDesc = seedScoreDesc;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithSeedAnalyzeProvider(
        IMotelySeedAnalyzeDesc seedAnalyzeDesc
    )
    {
        SeedAnalyzeDesc = seedAnalyzeDesc;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithSeedRouter(IMotelySeedRouterDesc desc)
    {
        SeedRouterDesc = desc;
        return this;
    }

    IMotelySearchSettings IMotelySearchSettings.WithThreadCount(int threadCount) =>
        WithThreadCount(threadCount);

    IMotelySearchSettings IMotelySearchSettings.WithBatchCharacterCount(int count) =>
        WithBatchCharacterCount(count);

    IMotelySearchSettings IMotelySearchSettings.WithProviderBatchSeedCount(int seedCount) =>
        WithProviderBatchSeedCount(seedCount);

    IMotelySearchSettings IMotelySearchSettings.WithStartBatchIndex(long index) =>
        WithStartBatchIndex(index);

    IMotelySearchSettings IMotelySearchSettings.WithEndBatchIndex(long index) =>
        WithEndBatchIndex(index);

    IMotelySearchSettings IMotelySearchSettings.WithSeedScoreProvider(IMotelySeedScoreDesc desc) =>
        WithSeedScoreProvider(desc);

    IMotelySearchSettings IMotelySearchSettings.WithSeedAnalyzeProvider(
        IMotelySeedAnalyzeDesc desc
    ) => WithSeedAnalyzeProvider(desc);

    IMotelySearchSettings IMotelySearchSettings.WithSeedRouter(IMotelySeedRouterDesc desc) =>
        WithSeedRouter(desc);

    IMotelySearchSettings IMotelySearchSettings.WithSeedGenerator(
        IEnumerable<string> seeds,
        int seedCount
    ) => WithSeedGenerator(seeds, seedCount);

    IMotelySearchSettings IMotelySearchSettings.WithSeedList(string[] seeds) => WithSeedList(seeds);

    IMotelySearchSettings IMotelySearchSettings.WithRandomSearch(int count) =>
        WithRandomSearch(count);

    IMotelySearchSettings IMotelySearchSettings.WithKeywordSearch(
        IReadOnlyList<string> keywords,
        char[]? paddingAlphabet
    ) => WithKeywordSearch(keywords, paddingAlphabet);

    IMotelySearchSettings IMotelySearchSettings.WithAestheticSearch(
        JamlAesthetic aesthetic,
        char[]? paddingAlphabet
    ) => WithAestheticSearch(aesthetic, paddingAlphabet);

    IMotelySearchSettings IMotelySearchSettings.WithProviderSearch(IMotelySeedProvider provider) =>
        WithProviderSearch(provider);

    IMotelySearchSettings IMotelySearchSettings.WithSequentialSearch() => WithSequentialSearch();

    IMotelySearchSettings IMotelySearchSettings.WithDeck(MotelyDeck deck) => WithDeck(deck);

    IMotelySearchSettings IMotelySearchSettings.WithStake(MotelyStake stake) => WithStake(stake);

    IMotelySearchSettings IMotelySearchSettings.WithProgressCallback(
        Action<MotelyProgress> callback
    ) => WithProgressCallback(callback);

    IMotelySearchSettings IMotelySearchSettings.WithProgressReportIntervalMs(long intervalMs) =>
        WithProgressReportIntervalMs(intervalMs);

    IMotelySearchSettings IMotelySearchSettings.WithCsvOutput(bool csvOutput) =>
        WithCsvOutput(csvOutput);

    IMotelySearchSettings IMotelySearchSettings.WithQuietMode(bool quietMode) =>
        WithQuietMode(quietMode);

    IMotelySearchSettings IMotelySearchSettings.WithSeedMatchCallback(Action<string> callback) =>
        WithSeedMatchCallback(callback);

    IMotelySearchSettings IMotelySearchSettings.WithScoredResultCallback(
        Action<MotelyScoredSeedResult> callback
    ) => WithScoredResultCallback(callback);

    IMotelySearchSettings IMotelySearchSettings.WithBatchBoundaryCallback(Action callback) =>
        WithBatchBoundaryCallback(callback);

    IMotelySearchSettings IMotelySearchSettings.WithAutoScoreCutoff(bool enabled) =>
        WithAutoScoreCutoff(enabled);

    IMotelySearchSettings IMotelySearchSettings.StopAfter(long matchCount) =>
        StopAfter(matchCount);

    IMotelySearchSettings IMotelySearchSettings.WithJimmolate(
        MotelyIndividualSeedSearcher searcher,
        int scoreCutoff
    ) => WithJimmolate(searcher, scoreCutoff);

    IMotelySearch IMotelySearchSettings.Start(CancellationToken cancellationToken) =>
        Start(cancellationToken);

    public IMotelySearch CreateSearch() => new MotelySearch<TBaseFilter>(this);

    public MotelySearchSettings<TBaseFilter> WithDeck(MotelyDeck deck)
    {
        Deck = deck;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithStake(MotelyStake stake)
    {
        Stake = stake;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithProgressCallback(Action<MotelyProgress> callback)
    {
        ProgressCallback = callback;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithProgressReportIntervalMs(long intervalMs)
    {
        ProgressReportIntervalMs = Math.Max(0, intervalMs);
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithCsvOutput(bool csvOutput)
    {
        CsvOutput = csvOutput;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithQuietMode(bool quietMode)
    {
        QuietMode = quietMode;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithSeedMatchCallback(Action<string> callback)
    {
        SeedMatchCallback = callback;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithScoredResultCallback(
        Action<MotelyScoredSeedResult> callback
    )
    {
        ScoredResultCallback = callback;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithBatchBoundaryCallback(Action callback)
    {
        BatchBoundaryCallback = callback;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithAutoScoreCutoff(bool enabled = true)
    {
        AutoScoreCutoff = enabled;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> StopAfter(long matchCount)
    {
        StopAfterMatches = matchCount;
        return this;
    }

    public MotelySearchSettings<TBaseFilter> WithJimmolate(
        MotelyIndividualSeedSearcher searcher,
        int scoreCutoff = 1
    ) => WithAdditionalFilter(new Motely.Filters.Native.JimmolateFilterDesc(searcher, scoreCutoff));

    public IMotelySearch Start(CancellationToken cancellationToken = default)
    {
        MotelySearch<TBaseFilter> search = new(this);

        return search.Start(cancellationToken);
    }
}

public interface IMotelySearch : IDisposable
{
    long ElapsedMs { get; }
    long TotalSeedsSearched { get; }
    long MatchingSeeds { get; }
    long FilteredSeeds { get; }

    double SeedsPerSecond { get; }

    bool IsCompleted { get; }
    bool IsSequentialBatchSearch { get; }
    long CompletedBatchCount { get; }

    long TotalBatchCount { get; }

    long ResumeBatchIndex { get; }

    bool StoppedOnMatchLimit { get; }

    IMotelySearch Start(CancellationToken cancellationToken = default);
    Task RunSearchAsync(CancellationToken cancellationToken = default);
    void AwaitCompletion();
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);
}

internal unsafe interface IInternalMotelySearch : IMotelySearch
{
    internal int PseudoHashKeyLengthCount { get; }
    internal int* PseudoHashKeyLengths { get; }
}

public struct MotelySearchParameters
{
    public MotelyStake Stake;
    public MotelyDeck Deck;
}

public sealed unsafe partial class MotelySearch<TBaseFilter> : IInternalMotelySearch
    where TBaseFilter : struct, IMotelySeedFilter
{
    internal static readonly object ConsoleLock = new();

    private readonly MotelySearchParameters _searchParameters;

    internal CancellationToken _cancellationToken = CancellationToken.None;

    private readonly long _stopAfterMatches;
    private long _stopMatchCount;
    private CancellationTokenSource? _stopSource;

    private int _stoppedOnMatchLimit;

    private readonly TaskCompletionSource<bool> _completionSource = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int _isDisposed;
    private int _hasStarted;

    private readonly TBaseFilter _baseFilter;
    private readonly IMotelySeedFilter[] _additionalFilters;
    private readonly int _pseudoHashKeyLengthCount;
    private readonly bool _isProviderMode;

    private readonly IMotelySeedScoreProvider? _scoreProvider;
    private readonly IMotelySeedAnalyzeProvider? _analyzeProvider;
    private readonly IMotelySeedRouter? _seedRouter;

    int IInternalMotelySearch.PseudoHashKeyLengthCount => _pseudoHashKeyLengthCount;
    private readonly int* _pseudoHashKeyLengths;
    int* IInternalMotelySearch.PseudoHashKeyLengths => _pseudoHashKeyLengths;

    private readonly long _startBatchIndex;
    private readonly long _endBatchIndex;
    private readonly MotelySearchPlan[] _plans;
    private readonly int _threadCount;
    private readonly bool _runInline;

    private readonly int _workerCount;

    private Thread[]? _workerThreads;

    public bool IsCompleted => _completionSource.Task.IsCompleted;
    public bool IsSequentialBatchSearch => !_isProviderMode;

    public bool StoppedOnMatchLimit => Volatile.Read(ref _stoppedOnMatchLimit) != 0;

    public long TotalBatchCount => _plans[0].MaxBatch;

    public long ResumeBatchIndex
    {
        get
        {
            if (_isProviderMode)
                return -1;
            long min = long.MaxValue;
            for (int i = 0; i < _workerCount; i++)
                min = Math.Min(min, _plans[i].SnapshotNextBatch());
            return Math.Max(_startBatchIndex, min);
        }
    }

    public double SeedsPerSecond
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < _plans.Length; i++)
                sum += _plans[i].SnapshotSeedsPerSecond();
            return sum;
        }
    }

    public long CompletedBatchCount
    {
        get
        {
            long totalBatches = 0;
            for (int i = 0; i < _plans.Length; i++)
            {
                totalBatches += _plans[i].SnapshotBatchesCompleted();
            }

            return _isProviderMode ? totalBatches : _startBatchIndex + totalBatches;
        }
    }

    public long TotalSeedsSearched
    {
        get
        {
            long totalSeeds = 0;
            for (int i = 0; i < _plans.Length; i++)
            {
                totalSeeds += _plans[i].SnapshotSeedsSearched();
            }
            return totalSeeds;
        }
    }
    public long MatchingSeeds
    {
        get
        {
            long totalSeeds = 0;
            for (int i = 0; i < _plans.Length; i++)
            {
                totalSeeds += _plans[i].SnapshotMatchingSeeds();
            }
            return totalSeeds;
        }
    }

    public long FilteredSeeds
    {
        get
        {
            long total = TotalSeedsSearched;
            long matched = MatchingSeeds;
            long diff = total - matched;
            return diff > 0 ? diff : 0;
        }
    }

    public long ElapsedMs => _elapsedTime.ElapsedMilliseconds;

    public bool TryGetScoreProvider([NotNullWhen(true)] out IMotelySeedScoreProvider? scoreProvider)
    {
        scoreProvider = _scoreProvider;
        return scoreProvider != null;
    }

    public bool TryGetAnalyzeProvider(
        [NotNullWhen(true)] out IMotelySeedAnalyzeProvider? analyzeProvider
    )
    {
        analyzeProvider = _analyzeProvider;
        return analyzeProvider != null;
    }

    public bool TryGetSingleSeedRouter([NotNullWhen(true)] out IMotelySeedRouter? seedRouter)
    {
        seedRouter = _seedRouter;
        return seedRouter != null;
    }

    private readonly Action<MotelyProgress>? _progressCallback;
    private readonly Action<string>? _seedMatchCallback;
    private readonly Action<MotelyScoredSeedResult>? _scoredResultCallback;
    private readonly Action? _batchBoundaryCallback;
    private readonly bool _autoScoreCutoff;
    private readonly long _progressReportIntervalMs;
    private readonly int _providerBatchSeedCount;

    private readonly Stopwatch _elapsedTime = new();
    private long _lastProgressReportElapsedMs = -1;
    private long _lastReportSeeds;

    public MotelySearch(MotelySearchSettings<TBaseFilter> settings)
    {
        _isProviderMode = settings.Mode == MotelySearchMode.Provider;
        _searchParameters = new() { Deck = settings.Deck, Stake = settings.Stake };
        _progressCallback = settings.ProgressCallback;
        _progressReportIntervalMs = settings.ProgressReportIntervalMs;
        _seedMatchCallback = settings.SeedMatchCallback;
        _scoredResultCallback = settings.ScoredResultCallback;
        _batchBoundaryCallback = settings.BatchBoundaryCallback;
        _autoScoreCutoff = settings.AutoScoreCutoff;
        _stopAfterMatches = settings.StopAfterMatches;
        _providerBatchSeedCount = Math.Max(
            MotelyGlobals.MaxVectorWidth,
            settings.ProviderBatchSeedCount
        );

        MotelyFilterCreationContext filterCreationContext = new(in _searchParameters)
        {
            IsAdditionalFilter = false,
            SeedMatchCallback = _seedMatchCallback,
        };

        _baseFilter = settings.BaseFilterDesc.CreateFilter(ref filterCreationContext);

        if (settings.AdditionalFilters == null)
        {
            _additionalFilters = [];
        }
        else
        {
            _additionalFilters = new IMotelySeedFilter[settings.AdditionalFilters.Count];
            for (int i = 0; i < _additionalFilters.Length; i++)
            {
                filterCreationContext.IsAdditionalFilter = true;
                _additionalFilters[i] = settings
                    .AdditionalFilters[i]
                    .CreateFilter(ref filterCreationContext);
            }
        }

        if (settings.SeedScoreDesc != null)
        {
            _scoreProvider = settings.SeedScoreDesc.CreateScoreProvider(ref filterCreationContext);
        }

        if (settings.SeedAnalyzeDesc != null)
        {
            _analyzeProvider = settings.SeedAnalyzeDesc.CreateAnalyzeProvider(
                ref filterCreationContext
            );
        }

        if (settings.SeedRouterDesc != null)
        {
            _seedRouter = settings.SeedRouterDesc.CreateSeedRouter(ref filterCreationContext);
        }

        _startBatchIndex = settings.StartBatchIndex;
        _endBatchIndex = settings.EndBatchIndex;

        int[] pseudohashKeyLengths = [.. filterCreationContext.CachedPseudohashKeyLengths];
        _pseudoHashKeyLengthCount = pseudohashKeyLengths.Length;

        _pseudoHashKeyLengths = (int*)Marshal.AllocHGlobal(sizeof(int) * _pseudoHashKeyLengthCount);
        for (int i = 0; i < _pseudoHashKeyLengthCount; i++)
        {
            _pseudoHashKeyLengths[i] = pseudohashKeyLengths[i];
        }

        _threadCount = Math.Max(1, settings.ThreadCount);
        _runInline = settings.SeedProvider is MotelySeedListProvider;
        _workerCount = OperatingSystem.IsBrowser() ? 1 : _threadCount;
        _plans = new MotelySearchPlan[_threadCount];
        for (int i = 0; i < _threadCount; i++)
        {
            _plans[i] = settings.Mode switch
            {
                MotelySearchMode.Sequential => new MotelySequentialSearchPlan(this, settings, i),
                MotelySearchMode.Provider => new MotelyProviderSearchPlan(this, settings, i),
                _ => throw new InvalidEnumArgumentException(nameof(settings.Mode)),
            };
        }
    }

    private void RunWorkerBody(MotelySearchPlan plan)
    {
        if (_isProviderMode)
        {
            RunProviderPlan(plan);
        }
        else
        {
            plan.ExecuteSequentialPlan();
        }
    }

    private void SignalSearchCompleted()
    {
        _elapsedTime.Stop();
        Thread.MemoryBarrier();
        bool stoppedOnLimit = Volatile.Read(ref _stoppedOnMatchLimit) != 0;
        bool completed =
            Volatile.Read(ref _isDisposed) == 0
            && (stoppedOnLimit || !_cancellationToken.IsCancellationRequested);
        _completionSource.TrySetResult(completed);
    }

    private void RunProviderPlan(MotelySearchPlan plan)
    {
        while (plan.TryExecuteProviderBatch()) { }
        plan.FlushFilterBatches();
    }

    public Task RunSearchAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        BeginSearch(cancellationToken);
        return _completionSource.Task;
    }

    public IMotelySearch Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        BeginSearch(cancellationToken);
        return this;
    }

    private void BeginSearch(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _hasStarted, 1) != 0)
            throw new InvalidOperationException("Search has already been started.");

        if (_stopAfterMatches > 0)
        {
            _stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationToken = _stopSource.Token;
        }
        else
        {
            _cancellationToken = cancellationToken;
        }

        StartSearchThreads();
    }

    internal void NoteMatchForStop()
    {
        if (_stopAfterMatches <= 0)
            return;

        if (Interlocked.Increment(ref _stopMatchCount) != _stopAfterMatches)
            return;

        Interlocked.Exchange(ref _stoppedOnMatchLimit, 1);
        try
        {
            _stopSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void StartSearchThreads()
    {
        _elapsedTime.Start();

        if (_runInline)
        {
            try
            {
                RunWorkerBody(_plans[0]);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _completionSource.TrySetException(ex);
                return;
            }
            SignalSearchCompleted();
            return;
        }

        if (OperatingSystem.IsBrowser())
        {
            Task pump = _isProviderMode
                ? RunProviderBrowserPumpAsync()
                : RunSequentialBrowserPumpAsync();

            pump.ContinueWith(
                t => _completionSource.TrySetException(t.Exception!.InnerException ?? t.Exception),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
            return;
        }

        WorkerCoordinator coordinator = new(this, _threadCount);

        var threads = new Thread[_threadCount];
        for (int i = 0; i < _threadCount; i++)
        {
            int threadIdx = i;
            threads[i] = new Thread(() => coordinator.RunWorker(threadIdx))
            {
                Name = $"Motely Search Thread {threadIdx}",
                IsBackground = true,
            };
            threads[i].Start();
        }
        _workerThreads = threads;
    }

    private sealed class WorkerCoordinator
    {
        private readonly MotelySearch<TBaseFilter> _owner;
        private int _remaining;
        private Exception? _firstError;

        public WorkerCoordinator(MotelySearch<TBaseFilter> owner, int totalWorkers)
        {
            _owner = owner;
            _remaining = totalWorkers;
        }

        public void RunWorker(int idx)
        {
            try
            {
                _owner.RunWorkerBody(_owner._plans[idx]);
            }
            catch (OperationCanceledException)
                when (_owner._cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WORKER EXCEPTION] {ex}");
                Interlocked.CompareExchange(ref _firstError, ex, null);
            }
            finally
            {
                if (Interlocked.Decrement(ref _remaining) == 0)
                {
                    _owner._elapsedTime.Stop();
                    Thread.MemoryBarrier();
                    var err = Volatile.Read(ref _firstError);
                    if (err is not null)
                    {
                        _owner._completionSource.TrySetException(err);
                    }
                    else
                    {
                        bool completed =
                            Volatile.Read(ref _owner._isDisposed) == 0
                            && !_owner._cancellationToken.IsCancellationRequested;
                        _owner._completionSource.TrySetResult(completed);
                    }
                }
            }
        }
    }

    public Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        return _completionSource.Task.WaitAsync(cancellationToken);
    }

    public void AwaitCompletion()
    {
        _completionSource.Task.GetAwaiter().GetResult();
    }

    private void NotifyBatchBoundary() => _batchBoundaryCallback?.Invoke();

    private void PrintReport()
    {
        if (_progressCallback == null)
            return;

        long elapsedMS = _elapsedTime.ElapsedMilliseconds;
        if (
            _progressReportIntervalMs > 0
            && _lastProgressReportElapsedMs >= 0
            && elapsedMS - _lastProgressReportElapsedMs < _progressReportIntervalMs
        )
            return;

        long prevReportSeeds = _lastReportSeeds;
        long prevReportMs = _lastProgressReportElapsedMs;
        _lastProgressReportElapsedMs = elapsedMS;

        long completedBatches = 0,
            seedsSearched = 0,
            matchingSeeds = 0;
        for (int i = 0; i < _plans.Length; i++)
        {
            completedBatches += _plans[i].SnapshotBatchesCompleted();
            seedsSearched += _plans[i].SnapshotSeedsSearched();
            matchingSeeds += _plans[i].SnapshotMatchingSeeds();
        }
        long thisCompletedCount = _isProviderMode
            ? completedBatches
            : _startBatchIndex + completedBatches;
        long totalBatches = _plans[0].MaxBatch;

        double percentComplete;
        double totalPortionFinished;
        double thisPortionFinished;

        if (_isProviderMode && _plans[0] is MotelyProviderSearchPlan providerPlan)
        {
            long totalSeeds = providerPlan.SeedProvider.SeedCount;
            totalPortionFinished = totalSeeds > 0 ? (double)seedsSearched / totalSeeds : 0;
            percentComplete = totalPortionFinished * 100.0;
            thisPortionFinished = totalPortionFinished;
        }
        else
        {
            long batchesSinceStart = thisCompletedCount - _startBatchIndex;
            long lastBatchToDo = Math.Min(_endBatchIndex, _plans[0].MaxBatch);
            long totalBatchesToDo = lastBatchToDo - _startBatchIndex;
            totalPortionFinished = totalBatches > 0 ? (double)thisCompletedCount / totalBatches : 0;
            percentComplete = totalPortionFinished * 100.0;
            thisPortionFinished =
                totalBatchesToDo > 0 ? (double)batchesSinceStart / totalBatchesToDo : 0.0;
        }

        double seedsPerMs;
        if (prevReportMs >= 0)
        {
            long deltaSeeds = seedsSearched - prevReportSeeds;
            long deltaMs = elapsedMS - prevReportMs;
            seedsPerMs = deltaMs > 0 ? (double)deltaSeeds / deltaMs : 0;
        }
        else
        {
            seedsPerMs = elapsedMS > 1 ? (double)seedsSearched / elapsedMS : 0;
        }
        _lastReportSeeds = seedsSearched;

        long? etaMs = null;
        if (thisPortionFinished >= 0.0000001)
        {
            double totalTimeEstimate = elapsedMS / thisPortionFinished;
            double timeLeftMs = totalTimeEstimate - elapsedMS;
            if (
                !double.IsNaN(timeLeftMs)
                && !double.IsInfinity(timeLeftMs)
                && timeLeftMs >= 0
                && timeLeftMs <= 365.0 * 24 * 60 * 60 * 1000
            )
                etaMs = (long)timeLeftMs;
        }

        var progress = new MotelyProgress
        {
            SeedsSearched = seedsSearched,
            MatchingSeeds = matchingSeeds,
            SeedsPerMillisecond = seedsPerMs,
            PercentComplete = percentComplete,
            ElapsedMilliseconds = elapsedMS,
            EstimatedTimeRemainingMilliseconds = etaMs,
        };
        _progressCallback(progress);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        if (_workerThreads is not null)
        {
            int self = Environment.CurrentManagedThreadId;
            foreach (Thread t in _workerThreads)
                if (t.ManagedThreadId != self)
                    t.Join();
        }

        if (_plans is not null)
            for (int i = 0; i < _plans.Length; i++)
                _plans[i]?.Dispose();
        Marshal.FreeHGlobal((nint)_pseudoHashKeyLengths);

        _stopSource?.Dispose();

        _completionSource.TrySetResult(false);

        GC.SuppressFinalize(this);
    }

    ~MotelySearch()
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            Dispose();
        }
    }

    private abstract class MotelySearchPlan : IDisposable
    {
        public const int MAX_SEED_WAIT_MS = 500;

        public readonly MotelySearch<TBaseFilter> Search;
        public readonly int ThreadIndex;

        public long MaxBatch { get; internal set; }

        private readonly Stopwatch _clock = new();

        private long _nextBatch;

        internal long _localMatchingSeeds = 0;
        internal long _localBatchesCompleted = 0;
        internal long _localSeedsSearched = 0;
        private readonly AutoCutoffState _autoCutoffState = new() { LearnedCutoff = int.MinValue };

        private sealed class AutoCutoffState
        {
            public int LearnedCutoff;

            public long RawMatches;

            public long LastGateRawMatches;
            public long LastGateSeeds;

            public long SeedsFiltered;

            public bool Engaged;
        }

        protected readonly MotelyScoredSeedResult[] _resultBuffer = new MotelyScoredSeedResult[
            MotelyGlobals.MaxVectorWidth
        ];

        [InlineArray(MotelyGlobals.MaxSeedLength)]
        internal struct FilterSeedBatchCharacters
        {
            public Vector512<double> Character;
        }

        internal struct FilterSeedBatch
        {
            public FilterSeedBatchCharacters SeedCharacters;
            public Vector512<double>* SeedHashes;
            public PartialSeedHashCache SeedHashCache;
            public int SeedLength;
            public int SeedCount;
            public long WaitStartMS;
        }

        internal readonly FilterSeedBatch* _filterSeedBatches;

        internal bool _providerExhausted;

        public MotelySearchPlan(MotelySearch<TBaseFilter> search, int threadIndex)
        {
            Search = search;
            ThreadIndex = threadIndex;
            _nextBatch = search._startBatchIndex + threadIndex;

            if (search._additionalFilters.Length != 0)
            {
                _filterSeedBatches = (FilterSeedBatch*)
                    Marshal.AllocHGlobal(
                        sizeof(FilterSeedBatch) * search._additionalFilters.Length
                    );

                int allocatedCount = 0;
                try
                {
                    for (int i = 0; i < search._additionalFilters.Length; i++)
                    {
                        FilterSeedBatch* batch = &_filterSeedBatches[i];

                        *batch = new()
                        {
                            SeedHashes = (Vector512<double>*)
                                Marshal.AllocHGlobal(
                                    sizeof(Vector512<double>)
                                        * MotelyGlobals.MaxCachedPseudoHashKeyLength
                                ),
                        };
                        allocatedCount = i + 1;

                        batch->SeedHashCache = new(search, batch->SeedHashes);
                    }
                }
                catch
                {
                    for (int i = 0; i < allocatedCount; i++)
                    {
                        if (_filterSeedBatches[i].SeedHashes != null)
                        {
                            Marshal.FreeHGlobal((nint)_filterSeedBatches[i].SeedHashes);
                        }
                    }
                    Marshal.FreeHGlobal((nint)_filterSeedBatches);
                    _filterSeedBatches = null;
                    throw;
                }
            }
        }

        internal void ExecuteSequentialPlan()
        {
            while (TryExecuteSequentialBatch()) { }
            FlushFilterBatches();
        }

        internal bool TryExecuteSequentialBatch()
        {
            _clock.Start();
            if (ExecuteSequentialBatchCore())
                return true;
            _clock.Stop();
            return false;
        }

        private bool ExecuteSequentialBatchCore()
        {
            if (Volatile.Read(ref Search._isDisposed) != 0)
                return false;

            if (Search._cancellationToken.IsCancellationRequested)
                return false;

            long batchIdx = _nextBatch;
            if (batchIdx >= Search._endBatchIndex || batchIdx >= MaxBatch)
                return false;

            SearchSequentialBatch(batchIdx);

            if (
                Volatile.Read(ref Search._isDisposed) == 0
                && !Search._cancellationToken.IsCancellationRequested
            )
            {
                _localBatchesCompleted++;
                _nextBatch = batchIdx + Search._workerCount;
            }

            if (Search._additionalFilters.Length != 0)
            {
                for (int i = 0; i < Search._additionalFilters.Length; i++)
                {
                    FilterSeedBatch* batch = &_filterSeedBatches[i];

                    if (batch->SeedCount != 0)
                    {
                        if (Environment.TickCount64 - batch->WaitStartMS >= MAX_SEED_WAIT_MS)
                        {
                            SearchFilterBatch(i, batch);
                            Debug.Assert(
                                batch->SeedCount == 0,
                                "Batch should be reset after SearchFilterBatch"
                            );
                        }
                    }
                }
            }

            OnBatchDone();

            return true;
        }

        internal bool TryExecuteProviderBatch()
        {
            _clock.Start();
            if (ExecuteProviderBatchCore())
                return true;
            _clock.Stop();
            return false;
        }

        private bool ExecuteProviderBatchCore()
        {
            if (Volatile.Read(ref Search._isDisposed) != 0)
                return false;

            if (Search._cancellationToken.IsCancellationRequested)
                return false;

            if (_providerExhausted)
                return false;

            long target = Search._providerBatchSeedCount;
            long chewed = 0;
            while (
                chewed < target
                && !_providerExhausted
                && Volatile.Read(ref Search._isDisposed) == 0
                && !Search._cancellationToken.IsCancellationRequested
            )
            {
                long before = _localSeedsSearched;
                SearchProviderBatch();
                long got = _localSeedsSearched - before;
                if (got <= 0)
                    break;
                chewed += got;
            }

            if (chewed == 0)
                return false;

            _localBatchesCompleted++;

            OnBatchDone();

            return !_providerExhausted
                && Volatile.Read(ref Search._isDisposed) == 0
                && !Search._cancellationToken.IsCancellationRequested;
        }

        internal void FlushFilterBatches()
        {
            if (Search._additionalFilters.Length != 0 && _filterSeedBatches != null)
            {
                for (int i = 0; i < Search._additionalFilters.Length; i++)
                {
                    FilterSeedBatch* batch = &_filterSeedBatches[i];
                    if (batch->SeedCount != 0)
                    {
                        SearchFilterBatch(i, batch);
                    }
                }
            }
        }

        private void OnBatchDone()
        {
            if (Search._autoScoreCutoff)
            {
                long batchMatches = _autoCutoffState.RawMatches - _autoCutoffState.LastGateRawMatches;
                long batchSeeds = _localSeedsSearched - _autoCutoffState.LastGateSeeds;
                _autoCutoffState.Engaged = batchSeeds > 0 && batchMatches >= batchSeeds;
                _autoCutoffState.LastGateRawMatches = _autoCutoffState.RawMatches;
                _autoCutoffState.LastGateSeeds = _localSeedsSearched;
            }

            Search.NotifyBatchBoundary();
            Search.PrintReport();
        }

        internal abstract void SearchProviderBatch();
        internal abstract void SearchSequentialBatch(long batchIdx);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long SnapshotMatchingSeeds() => Volatile.Read(ref _localMatchingSeeds);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long SnapshotBatchesCompleted() => Volatile.Read(ref _localBatchesCompleted);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long SnapshotSeedsSearched() => Volatile.Read(ref _localSeedsSearched);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long SnapshotNextBatch() => Volatile.Read(ref _nextBatch);

        internal double SnapshotSeedsPerSecond()
        {
            double seconds = _clock.Elapsed.TotalSeconds;
            return seconds > 0 ? Volatile.Read(ref _localSeedsSearched) / seconds : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void SearchSeeds(in MotelySearchContextParams searchContextParams)
        {
            Debug.Assert(!searchContextParams.IsAdditionalFilter);

            MotelyVectorSearchContext searchContext = new(
                in Search._searchParameters,
                in searchContextParams
            );

            VectorMask searchResultMask = Search._baseFilter.Filter(ref searchContext);

            searchContextParams.SeedHashCache->Reset();
            if (searchResultMask.IsPartiallyTrue())
            {
                if (Search._additionalFilters.Length == 0)
                {
                    ReportSeeds(searchResultMask, in searchContextParams);
                }
                else
                {
                    BatchSeeds(0, searchResultMask, in searchContextParams);
                }
            }
        }

        private void ReportSeeds(
            VectorMask searchResultMask,
            in MotelySearchContextParams searchParams
        )
        {
            Debug.Assert(
                searchResultMask.IsPartiallyTrue(),
                "Mask should be checked for partial truth before calling report seeds (for performance)."
            );

            if (Search.TryGetScoreProvider(out var scoreProvider))
            {
                MotelyVectorSearchContext searchContext = new(
                    in Search._searchParameters,
                    in searchParams
                );

                VectorMask scoredMask = scoreProvider.Score(
                    ref searchContext,
                    _resultBuffer,
                    searchResultMask
                );

                VectorMask reportedMask = ReportScoredResults(scoredMask, in searchParams);

                if (reportedMask.IsPartiallyTrue())
                    Search._analyzeProvider?.Analyze(ref searchContext, reportedMask, _resultBuffer);
            }
            else if (Search._seedRouter != null)
            {
                for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
                {
                    if (searchParams.IsLaneValid(lane))
                    {
                        MotelySingleSearchContext singleCtx = new(
                            in Search._searchParameters,
                            in searchParams,
                            lane
                        );
                        Search._seedRouter.InjectSingleSeedContext(in singleCtx);
                    }
                }
            }
            else
            {
                ReportBasicSeeds(searchResultMask, in searchParams);

                if (Search._analyzeProvider != null)
                {
                    MotelyVectorSearchContext searchContext = new(
                        in Search._searchParameters,
                        in searchParams
                    );
                    Search._analyzeProvider.Analyze(ref searchContext, searchResultMask, null);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private VectorMask ReportScoredResults(
            VectorMask resultMask,
            in MotelySearchContextParams searchParams
        )
        {
            uint reported = 0;
            for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
            {
                if (resultMask[lane] && searchParams.IsLaneValid(lane))
                {
                    if (Search._autoScoreCutoff)
                    {
                        int score = _resultBuffer[lane].Score;

                        _autoCutoffState.RawMatches++;

                        if (_autoCutoffState.Engaged && score < _autoCutoffState.LearnedCutoff)
                        {
                            _autoCutoffState.SeedsFiltered++;
                            continue;
                        }

                        if (score > _autoCutoffState.LearnedCutoff)
                            _autoCutoffState.LearnedCutoff = score;
                    }

                    Search._scoredResultCallback?.Invoke(_resultBuffer[lane]);
                    _localMatchingSeeds++;
                    reported |= 1u << lane;
                    Search.NoteMatchForStop();
                }
            }
            return new VectorMask(reported);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReportBasicSeeds(
            VectorMask searchResultMask,
            in MotelySearchContextParams searchParams
        )
        {
            char* seed = stackalloc char[MotelyGlobals.MaxSeedLength];

            for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
            {
                if (searchResultMask[lane] && searchParams.IsLaneValid(lane))
                {
                    int length = searchParams.GetSeed(lane, seed);

                    _localMatchingSeeds++;

                    string seedStr = new Span<char>(seed, length).ToString();
                    Search._seedMatchCallback?.Invoke(seedStr);
                    Search.NoteMatchForStop();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BatchSeeds(
            int filterIndex,
            VectorMask searchResultMask,
            in MotelySearchContextParams searchParams
        )
        {
            Debug.Assert(
                _filterSeedBatches != null
                    && Search._additionalFilters != null
                    && filterIndex >= 0
                    && filterIndex < Search._additionalFilters.Length,
                $"Invalid filterIndex {filterIndex}, _additionalFilters={(Search._additionalFilters == null ? "NULL" : $"Length={Search._additionalFilters.Length}")}"
            );

            Debug.Assert(searchParams.SeedHashCache != null, "SeedHashCache is null");

            FilterSeedBatch* filterBatch = &_filterSeedBatches[filterIndex];

            Debug.Assert(
                filterBatch->SeedHashes != null,
                $"filterBatch->SeedHashes is null for filterIndex {filterIndex}"
            );

            Debug.Assert(
                searchResultMask.IsPartiallyTrue(),
                "Mask should be checked for partial truth before calling enqueue seeds (for performance)."
            );

            for (int lane = 0; lane < Vector512<double>.Count; lane++)
            {
                if (searchResultMask[lane] && searchParams.IsLaneValid(lane))
                {
                    int seedBatchIndex = filterBatch->SeedCount;

                    if (seedBatchIndex == 0)
                    {
                        filterBatch->SeedLength = searchParams.SeedLength;
                        filterBatch->WaitStartMS = Environment.TickCount64;
                    }
                    else
                    {
                        if (filterBatch->SeedLength != searchParams.SeedLength)
                        {
                            SearchFilterBatch(filterIndex, filterBatch);

                            Debug.Assert(
                                filterBatch->SeedCount == 0,
                                "Searching the batch should have reset it."
                            );
                            seedBatchIndex = 0;

                            filterBatch->SeedLength = searchParams.SeedLength;
                            filterBatch->WaitStartMS = Environment.TickCount64;
                        }
                    }

                    ++filterBatch->SeedCount;

                    {
                        int i = 0;
                        for (; i < searchParams.SeedLastCharactersLength; i++)
                        {
                            ((double*)&filterBatch->SeedCharacters)[
                                i * Vector512<double>.Count + seedBatchIndex
                            ] = ((double*)searchParams.SeedLastCharacters)[
                                i * Vector512<double>.Count + lane
                            ];
                        }

                        for (
                            int firstCharIndex = 0;
                            firstCharIndex < searchParams.SeedFirstCharactersLength;
                            firstCharIndex++
                        )
                        {
                            ((double*)&filterBatch->SeedCharacters)[
                                (searchParams.SeedLastCharactersLength + firstCharIndex)
                                    * Vector512<double>.Count
                                    + seedBatchIndex
                            ] = searchParams.SeedFirstCharacters[firstCharIndex];
                        }
                    }

                    if (
                        Search._pseudoHashKeyLengths == null
                        || Search._pseudoHashKeyLengthCount <= 0
                    )
                        return;

                    for (int i = 0; i < Search._pseudoHashKeyLengthCount; i++)
                    {
                        int partialHashLength = Search._pseudoHashKeyLengths[i];

                        if (searchParams.SeedHashCache == null)
                            continue;

                        if (partialHashLength >= MotelyGlobals.MaxCachedPseudoHashKeyLength)
                            Console.WriteLine(
                                $"partialHashLength {partialHashLength} >= MotelyGlobals.MaxCachedPseudoHashKeyLength {MotelyGlobals.MaxCachedPseudoHashKeyLength}"
                            );

                        if (searchParams.SeedHashCache->Cache[partialHashLength] == null)
                            continue;

                        double sourceValue = (
                            (double*)searchParams.SeedHashCache->Cache[partialHashLength]
                        )[lane];

                        ((double*)filterBatch->SeedHashes)[
                            i * Vector512<double>.Count + seedBatchIndex
                        ] = sourceValue;
                    }

                    if (seedBatchIndex == Vector512<double>.Count - 1)
                    {
                        SearchFilterBatch(filterIndex, filterBatch);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SearchFilterBatch(int filterIndex, FilterSeedBatch* filterBatch)
        {
            Debug.Assert(filterBatch->SeedCount != 0);

            int count = filterBatch->SeedCount;
            if (count < MotelyGlobals.MaxVectorWidth)
            {
                double* chars = (double*)&filterBatch->SeedCharacters;
                for (int lane = count; lane < MotelyGlobals.MaxVectorWidth; lane++)
                {
                    chars[lane] = 0;
                }

                double* hashes = (double*)filterBatch->SeedHashes;
                int keyCount = Search._pseudoHashKeyLengthCount;
                for (int i = 0; i < keyCount; i++)
                {
                    for (int lane = count; lane < MotelyGlobals.MaxVectorWidth; lane++)
                    {
                        hashes[i * Vector512<double>.Count + lane] = 0;
                    }
                }
            }

            MotelySearchContextParams searchParams = new(
                &filterBatch->SeedHashCache,
                filterBatch->SeedLength,
                0,
                null,
                (Vector512<double>*)&filterBatch->SeedCharacters,
                isAdditionalFilter: true
            );

            MotelyVectorSearchContext searchContext = new(
                in Search._searchParameters,
                in searchParams
            );

            VectorMask searchResultMask = Search
                ._additionalFilters[filterIndex]
                .Filter(ref searchContext);

            if (searchResultMask.IsPartiallyTrue())
            {
                int nextFilterIndex = filterIndex + 1;

                if (nextFilterIndex == Search._additionalFilters.Length)
                {
                    ReportSeeds(searchResultMask, in searchParams);
                }
                else
                {
                    Debug.Assert(
                        nextFilterIndex < Search._additionalFilters.Length && nextFilterIndex >= 0,
                        $"nextFilterIndex {nextFilterIndex} >= _additionalFilters.Length {Search._additionalFilters.Length} or nextFilterIndex < 0   "
                    );
                    BatchSeeds(nextFilterIndex, searchResultMask, in searchParams);
                }
            }

            filterBatch->SeedHashCache.Reset();

            filterBatch->SeedCount = 0;
        }

        public void Dispose()
        {
            if (_filterSeedBatches != null)
            {
                for (int i = 0; i < Search._additionalFilters.Length; i++)
                {
                    _filterSeedBatches[i].SeedHashCache.Dispose();
                    if (_filterSeedBatches[i].SeedHashes != null)
                    {
                        Marshal.FreeHGlobal((nint)_filterSeedBatches[i].SeedHashes);
                    }
                }

                Marshal.FreeHGlobal((nint)_filterSeedBatches);
            }
        }
    }

    private sealed unsafe class MotelyProviderSearchPlan : MotelySearchPlan
    {
        public readonly IMotelySeedProvider SeedProvider;

        private readonly Vector512<double>* _hashes;
        private readonly PartialSeedHashCache* _hashCache;

        private readonly Vector512<double>* _seedCharacterMatrix;

        private readonly string[] _seedBatchBuffer;
        private int _bufferCount;
        private int _bufferPos;
        private const int MaxFetchChunk = MotelyGlobals.DefaultProviderBatchSeedCount;

        public MotelyProviderSearchPlan(
            MotelySearch<TBaseFilter> search,
            MotelySearchSettings<TBaseFilter> settings,
            int index
        )
            : base(search, index)
        {
            if (settings.SeedProvider == null)
                throw new ArgumentException(
                    "Cannot create a provider search without a seed provider."
                );

            SeedProvider = settings.SeedProvider;

            long seedCount = SeedProvider.SeedCount;
            long reportBatch = Math.Max(
                MotelyGlobals.MaxVectorWidth,
                (long)search._providerBatchSeedCount
            );
            MaxBatch =
                seedCount >= 0
                    ? (seedCount + reportBatch - 1) / reportBatch
                    : long.MaxValue / reportBatch;
            _seedBatchBuffer = new string[
                (int)Math.Clamp(reportBatch, MotelyGlobals.MaxVectorWidth, MaxFetchChunk)
            ];

            _hashes = (Vector512<double>*)
                Marshal.AllocHGlobal(sizeof(Vector512<double>) * search._pseudoHashKeyLengthCount);

            _hashCache = (PartialSeedHashCache*)Marshal.AllocHGlobal(sizeof(PartialSeedHashCache));
            *_hashCache = new PartialSeedHashCache(search, _hashes);

            _seedCharacterMatrix = (Vector512<double>*)
                Marshal.AllocHGlobal(sizeof(Vector512<double>) * MotelyGlobals.MaxSeedLength);
        }

        internal override void SearchProviderBatch()
        {
            if (_bufferPos >= _bufferCount)
            {
                _bufferCount = SeedProvider.NextSeeds(_seedBatchBuffer);
                _bufferPos = 0;
                if (_bufferCount == 0)
                {
                    _providerExhausted = true;
                    return;
                }
            }

            int packStart = _bufferPos;
            int fetched = Math.Min(MotelyGlobals.MaxVectorWidth, _bufferCount - packStart);
            _bufferPos = packStart + fetched;

            int* seedLengths = stackalloc int[MotelyGlobals.MaxVectorWidth];
            bool homogeneousSeedLength = true;
            int validCount = 0;

            for (int seedIdx = 0; seedIdx < fetched; seedIdx++)
            {
                ReadOnlySpan<char> seed = _seedBatchBuffer[packStart + seedIdx].AsSpan();

                if (
                    seed.IsEmpty
                    || seed.Length > MotelyGlobals.MaxSeedLength
                    || seed.IndexOf('0') >= 0
                )
                {
                    continue;
                }

                int lane = validCount;
                if (lane >= MotelyGlobals.MaxVectorWidth)
                    break;

                seedLengths[lane] = seed.Length;
                if (lane > 0 && seedLengths[0] != seed.Length)
                    homogeneousSeedLength = false;

                for (int i = 0; i < seed.Length; i++)
                {
                    ((double*)_seedCharacterMatrix)[i * MotelyGlobals.MaxVectorWidth + lane] =
                        seed[i];
                }
                for (int i = seed.Length; i < MotelyGlobals.MaxSeedLength; i++)
                {
                    ((double*)_seedCharacterMatrix)[i * MotelyGlobals.MaxVectorWidth + lane] = 0;
                }

                validCount++;
            }

            if (validCount == 0)
                return;

            for (int lane = validCount; lane < MotelyGlobals.MaxVectorWidth; lane++)
            {
                for (int i = 0; i < MotelyGlobals.MaxSeedLength; i++)
                {
                    ((double*)_seedCharacterMatrix)[i * MotelyGlobals.MaxVectorWidth + lane] = 0;
                }
            }

            _localSeedsSearched += validCount;

            if (homogeneousSeedLength)
            {
                int seedLength = seedLengths[0];

                for (
                    int pseudohashKeyIdx = 0;
                    pseudohashKeyIdx < Search._pseudoHashKeyLengthCount;
                    pseudohashKeyIdx++
                )
                {
                    int pseudohashKeyLength = Search._pseudoHashKeyLengths[pseudohashKeyIdx];

                    Vector512<double> numVector = Vector512<double>.One;

                    for (int i = seedLength - 1; i >= 0; i--)
                    {
                        numVector = Vector512.Divide(Vector512.Create(1.1239285023), numVector);

                        numVector = Vector512.Multiply(numVector, _seedCharacterMatrix[i]);

                        numVector = Vector512.Multiply(numVector, Math.PI);
                        numVector = Vector512.Add(
                            numVector,
                            Vector512.Create((i + pseudohashKeyLength + 1) * Math.PI)
                        );

                        Vector512<double> intPart = Vector512.Floor(numVector);
                        numVector = Vector512.Subtract(numVector, intPart);
                    }

                    _hashes[pseudohashKeyIdx] = numVector;
                }

                SearchSeeds(
                    new MotelySearchContextParams(
                        _hashCache,
                        seedLength,
                        0,
                        null,
                        _seedCharacterMatrix
                    )
                );
            }
            else
            {
                Span<char> seed = stackalloc char[MotelyGlobals.MaxSeedLength];

                for (int i = 0; i < validCount; i++)
                {
                    int seedLength = seedLengths[i];

                    for (int j = 0; j < seedLength; j++)
                    {
                        seed[j] = (char)
                            ((double*)_seedCharacterMatrix)[j * MotelyGlobals.MaxVectorWidth + i];
                    }

                    SearchSingleSeed(seed[..seedLength]);
                }
            }
        }

        internal override void SearchSequentialBatch(long batchIdx)
        {
            throw new InvalidOperationException(
                $"{nameof(MotelyProviderSearchPlan)} does not support sequential batch search."
            );
        }

        private void SearchSingleSeed(ReadOnlySpan<char> seed)
        {
            if (seed.IsEmpty)
                return;

            char* seedLastCharacters = stackalloc char[MotelyGlobals.MaxSeedLength - 1];

            for (
                int pseudohashKeyIdx = 0;
                pseudohashKeyIdx < Search._pseudoHashKeyLengthCount;
                pseudohashKeyIdx++
            )
            {
                int pseudohashKeyLength = Search._pseudoHashKeyLengths[pseudohashKeyIdx];

                double num = 1;

                for (int i = seed.Length - 1; i >= 0; i--)
                {
                    num =
                        (
                            1.1239285023 / num * seed[i] * Math.PI
                            + (i + pseudohashKeyLength + 1) * Math.PI
                        ) % 1;
                }

                _hashes[pseudohashKeyIdx] = Vector512.Create(num);
            }

            for (int i = 0; i < seed.Length - 1; i++)
            {
                seedLastCharacters[i] = seed[i + 1];
            }

            Vector512<double> firstCharacterVector = Vector512.CreateScalar((double)seed[0]);

            SearchSeeds(
                new MotelySearchContextParams(
                    _hashCache,
                    seed.Length,
                    seed.Length - 1,
                    seedLastCharacters,
                    &firstCharacterVector
                )
            );
        }

        public new void Dispose()
        {
            base.Dispose();

            _hashCache->Dispose();
            Marshal.FreeHGlobal((nint)_hashCache);

            Marshal.FreeHGlobal((nint)_hashes);
            Marshal.FreeHGlobal((nint)_seedCharacterMatrix);
        }
    }

    private sealed unsafe class MotelySequentialSearchPlan : MotelySearchPlan
    {
        private static readonly Vector512<double>[] SeedDigitVectors = new Vector512<double>[
            (MotelyGlobals.SeedDigits.Length + MotelyGlobals.MaxVectorWidth - 1)
                / MotelyGlobals.MaxVectorWidth
        ];

        static MotelySequentialSearchPlan()
        {
            Span<double> vector = stackalloc double[MotelyGlobals.MaxVectorWidth];

            for (int i = 0; i < SeedDigitVectors.Length; i++)
            {
                for (int j = 0; j < MotelyGlobals.MaxVectorWidth; j++)
                {
                    int index = i * MotelyGlobals.MaxVectorWidth + j;

                    if (index >= MotelyGlobals.SeedDigits.Length)
                    {
                        vector[j] = 0;
                    }
                    else
                    {
                        vector[j] = MotelyGlobals.SeedDigits[index];
                    }
                }

                SeedDigitVectors[i] = Vector512.Create<double>(vector);
            }
        }

        private readonly int _batchCharCount;
        private readonly int _nonBatchCharCount;

        private readonly char* _digits;
        private readonly Vector512<double>* _hashes;
        private readonly PartialSeedHashCache* _hashCache;

        public MotelySequentialSearchPlan(
            MotelySearch<TBaseFilter> search,
            MotelySearchSettings<TBaseFilter> settings,
            int index
        )
            : base(search, index)
        {
            _digits = (char*)Marshal.AllocHGlobal(sizeof(char) * MotelyGlobals.MaxSeedLength);

            _batchCharCount = settings.SequentialBatchCharacterCount;

            _nonBatchCharCount = MotelyGlobals.MaxSeedLength - _batchCharCount;
            MaxBatch = (long)Math.Pow(MotelyGlobals.SeedDigits.Length, _nonBatchCharCount);

            if (Search._pseudoHashKeyLengthCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Invalid pseudoHashKeyLengthCount: {Search._pseudoHashKeyLengthCount}. Search may not be properly initialized."
                );
            }

            _hashes = (Vector512<double>*)
                Marshal.AllocHGlobal(
                    sizeof(Vector512<double>)
                        * Search._pseudoHashKeyLengthCount
                        * (_batchCharCount + 1)
                );

            _hashCache = (PartialSeedHashCache*)Marshal.AllocHGlobal(sizeof(PartialSeedHashCache));
            *_hashCache = new PartialSeedHashCache(search, &_hashes[0]);
        }

        internal override void SearchSequentialBatch(long batchIdx)
        {
            for (int i = _nonBatchCharCount - 1; i >= 0; i--)
            {
                int charIndex = (int)(batchIdx % MotelyGlobals.SeedDigits.Length);
                _digits[MotelyGlobals.MaxSeedLength - i - 1] = MotelyGlobals.SeedDigits[charIndex];
                batchIdx /= MotelyGlobals.SeedDigits.Length;
            }

            Vector512<double>* hashes = &_hashes[
                _batchCharCount * Search._pseudoHashKeyLengthCount
            ];

            for (
                int pseudohashKeyIdx = 0;
                pseudohashKeyIdx < Search._pseudoHashKeyLengthCount;
                pseudohashKeyIdx++
            )
            {
                int pseudohashKeyLength = Search._pseudoHashKeyLengths[pseudohashKeyIdx];

                double num = 1;

                for (int i = MotelyGlobals.MaxSeedLength - 1; i > _batchCharCount - 1; i--)
                {
                    num =
                        (
                            1.1239285023 / num * _digits[i] * Math.PI
                            + (i + pseudohashKeyLength + 1) * Math.PI
                        ) % 1;
                }

                *(double*)&hashes[pseudohashKeyIdx] = num;
            }

            for (int vectorIndex = 0; vectorIndex < SeedDigitVectors.Length; vectorIndex++)
            {
                SearchVector(_batchCharCount - 1, SeedDigitVectors[vectorIndex], hashes, 0);
            }
        }

        internal override void SearchProviderBatch()
        {
            throw new InvalidOperationException(
                $"{nameof(MotelySequentialSearchPlan)} does not support provider batch search."
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SearchVector(
            int i,
            Vector512<double> seedDigitVector,
            Vector512<double>* nums,
            int numsLaneIndex
        )
        {
            if (
                Volatile.Read(ref Search._isDisposed) != 0
                || Search._cancellationToken.IsCancellationRequested
            )
            {
                return;
            }

            Vector512<double>* hashes = &_hashes[i * Search._pseudoHashKeyLengthCount];

            for (
                int pseudohashKeyIdx = 0;
                pseudohashKeyIdx < Search._pseudoHashKeyLengthCount;
                pseudohashKeyIdx++
            )
            {
                int pseudohashKeyLength = Search._pseudoHashKeyLengths[pseudohashKeyIdx];
                Vector512<double> calcVector = Vector512.Create(
                    1.1239285023 / ((double*)&nums[pseudohashKeyIdx])[numsLaneIndex]
                );

                calcVector = Vector512.Multiply(calcVector, seedDigitVector);

                calcVector = Vector512.Multiply(calcVector, Math.PI);
                calcVector = Vector512.Add(
                    calcVector,
                    Vector512.Create((i + pseudohashKeyLength + 1) * Math.PI)
                );

                Vector512<double> intPart = Vector512.Floor(calcVector);
                calcVector = Vector512.Subtract(calcVector, intPart);

                hashes[pseudohashKeyIdx] = calcVector;
            }

            if (i == 0)
            {
                _localSeedsSearched +=
                    MotelyGlobals.MaxVectorWidth
                    - BitOperations.PopCount(
                        Vector512
                            .Equals(seedDigitVector, Vector512<double>.Zero)
                            .ExtractMostSignificantBits()
                    );

                SearchSeeds(
                    new MotelySearchContextParams(
                        _hashCache,
                        MotelyGlobals.MaxSeedLength,
                        MotelyGlobals.MaxSeedLength - 1,
                        &_digits[1],
                        &seedDigitVector
                    )
                );
            }
            else
            {
                for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
                {
                    if (seedDigitVector[lane] == 0)
                        break;

                    _digits[i] = (char)seedDigitVector[lane];

                    for (int vectorIndex = 0; vectorIndex < SeedDigitVectors.Length; vectorIndex++)
                    {
                        SearchVector(i - 1, SeedDigitVectors[vectorIndex], hashes, lane);
                        if (
                            Volatile.Read(ref Search._isDisposed) != 0
                            || Search._cancellationToken.IsCancellationRequested
                        )
                        {
                            return;
                        }
                    }
                }
            }
        }

        public new void Dispose()
        {
            base.Dispose();

            _hashCache->Dispose();
            Marshal.FreeHGlobal((nint)_hashCache);

            Marshal.FreeHGlobal((nint)_digits);
            Marshal.FreeHGlobal((nint)_hashes);
        }
    }
}
