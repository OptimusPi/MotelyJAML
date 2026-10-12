using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Motely.Enums;

namespace Motely.CLI;

/// <summary>
/// One search as OpenTelemetry signals: a <c>motely.search</c> span from start to finish, counters
/// for seeds searched and matched, and per-search gauges for speed and progress, fed by the same
/// progress reports the CLI prints. Under Aspire they land on the dashboard; with nothing listening
/// (no OTLP endpoint) every call is a no-op.
/// </summary>
internal sealed class SearchTelemetry : IDisposable
{
    public const string Name = "Motely";

    /// <summary>The MCP SDK's own spans and metrics: one per request, tool calls included.</summary>
    private const string McpSdk = "Experimental.ModelContextProtocol";

    /// <summary>What this process exports: the engine's searches and, under <c>motely mcp</c>, the SDK's requests.</summary>
    public static readonly string[] Sources = [Name, McpSdk];

    private static readonly string Version = typeof(SearchTelemetry).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly ActivitySource Source = new(Name, Version);
    private static readonly Meter Meter = new(Name, Version);
    private static readonly Counter<long> Seeds = Meter.CreateCounter<long>("motely.search.seeds", "{seed}", "Seeds searched.");
    private static readonly Counter<long> Matches = Meter.CreateCounter<long>(
        "motely.search.matches",
        "{seed}",
        "Seeds that passed the filter's must and mustNot clauses."
    );
    private static readonly UpDownCounter<long> Active = Meter.CreateUpDownCounter<long>("motely.search.active", "{search}", "Searches running.");

    // The gauges read every running search at collection time; each search is its own series.
    private static readonly ConcurrentDictionary<SearchTelemetry, byte> Running = new();
    private static int _nextId;

    static SearchTelemetry()
    {
        Meter.CreateObservableGauge(
            "motely.search.speed",
            static () => Running.Keys.Select(static s => new Measurement<double>(s.Latest?.SeedsPerMillisecond * 1000.0 ?? 0, s._seriesTags)),
            "{seed}/s",
            "Seeds per second, per running search."
        );
        Meter.CreateObservableGauge(
            "motely.search.progress",
            static () => Running.Keys.Select(static s => new Measurement<double>(s.Latest?.PercentComplete ?? 0, s._seriesTags)),
            "%",
            "How much of its input a running search has covered."
        );
    }

    private readonly Activity? _activity;
    private readonly TagList _tags;
    private readonly KeyValuePair<string, object?>[] _seriesTags;
    private MotelyProgress? _latest;
    private long _seedsReported;
    private long _matchesReported;
    private int _disposed;

    private SearchTelemetry(string filter, MotelyDeck deck, MotelyStake stake, string input)
    {
        _tags = new TagList
        {
            { "motely.filter", filter },
            { "motely.deck", deck.ToString() },
            { "motely.stake", stake.ToString() },
            { "motely.input", input },
        };
        _seriesTags = [.. _tags, new("motely.search.id", Interlocked.Increment(ref _nextId))];
        _activity = Source.StartActivity("motely.search", ActivityKind.Internal, parentContext: default, tags: _tags);
        Active.Add(1, _tags);
        Running.TryAdd(this, 0);
    }

    private MotelyProgress? Latest => Volatile.Read(ref _latest);

    /// <param name="filter">The filter's name, else its file name.</param>
    /// <param name="input">Which seeds: sequential, random, keywords, seeds, replay or aesthetic.</param>
    public static SearchTelemetry Start(string filter, MotelyDeck deck, MotelyStake stake, string input) =>
        new(filter, deck, stake, input);

    /// <summary>A progress report from the engine (its own thread, about once a second).</summary>
    public void Report(MotelyProgress progress)
    {
        Volatile.Write(ref _latest, progress);
        Advance(progress.SeedsSearched, progress.MatchingSeeds);
    }

    /// <summary>The final totals and how the search ended, onto the counters and the span.</summary>
    /// <param name="outcome">finished, stopped (a match or time limit), or interrupted.</param>
    public void Finish(IMotelySearch search, string outcome)
    {
        Advance(search.TotalSeedsSearched, search.MatchingSeeds);
        if (_activity is null)
            return;
        _activity.SetTag("motely.outcome", outcome);
        _activity.SetTag("motely.seeds_searched", search.TotalSeedsSearched);
        _activity.SetTag("motely.matches", search.MatchingSeeds);
        _activity.SetTag("motely.seeds_per_second", Math.Round(search.SeedsPerSecond));
        if (search.IsSequentialBatchSearch && search.ResumeBatchIndex >= 0)
            _activity.SetTag("motely.resume_batch", search.ResumeBatchIndex);
        _activity.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>Marks the span failed: the search threw instead of ending.</summary>
    public void Fail(Exception exception) => _activity?.AddException(exception).SetStatus(ActivityStatusCode.Error, exception.Message);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Running.TryRemove(this, out _);
        Active.Add(-1, _tags);
        _activity?.Dispose();
    }

    // Counters take deltas; the engine reports running totals. Reports can race the final one.
    private void Advance(long seeds, long matches)
    {
        AddDelta(Seeds, ref _seedsReported, seeds);
        AddDelta(Matches, ref _matchesReported, matches);
    }

    private void AddDelta(Counter<long> counter, ref long reported, long total)
    {
        long before = Volatile.Read(ref reported);
        while (total > before)
        {
            long seen = Interlocked.CompareExchange(ref reported, total, before);
            if (seen == before)
            {
                counter.Add(total - before, _tags);
                return;
            }
            before = seen;
        }
    }
}
