using System.Collections.Concurrent;
using Motely.Filters;

namespace Motely.MCP;

/// <summary>One running or finished search, plus the best seeds it has found so far.</summary>
public sealed class SearchRun
{
    // Finds land from every worker thread. Keep the best Keep; trim only when the list doubles.
    private const int Keep = 100;
    private readonly List<SeedFind> _finds = [];
    private readonly Lock _gate = new();
    private MotelyProgress? _progress;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required MotelyDeck Deck { get; init; }
    public required MotelyStake Stake { get; init; }
    public required string Mode { get; init; }
    public required IReadOnlyList<string> TallyLabels { get; init; }
    public required CancellationTokenSource Cancel { get; init; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public IMotelySearch? Search { get; set; }
    public bool Stopped { get; set; }

    public MotelyProgress? Progress => Volatile.Read(ref _progress);

    public void Report(MotelyProgress progress) => Volatile.Write(ref _progress, progress);

    public void Add(string seed, int score, int[] tallies)
    {
        lock (_gate)
        {
            _finds.Add(new(seed, score, tallies));
            if (_finds.Count < Keep * 2)
                return;
            _finds.Sort(static (a, b) => b.Score.CompareTo(a.Score));
            _finds.RemoveRange(Keep, _finds.Count - Keep);
        }
    }

    public SeedFind[] Top(int count)
    {
        lock (_gate)
            return [.. _finds.OrderByDescending(static f => f.Score).Take(count)];
    }

    public string State =>
        Stopped ? "stopped"
        : Search is null ? "starting"
        : Search.IsCompleted ? (Search.StoppedOnMatchLimit ? "stopped-at-limit" : "finished")
        : "running";
}

public sealed record SeedFind(string Seed, int Score, int[] Tallies);

/// <summary>Every search this process has started. Searches live until the process exits.</summary>
public sealed class Searches
{
    private readonly ConcurrentDictionary<string, SearchRun> _runs = new();
    private int _next;

    public IEnumerable<SearchRun> All => _runs.Values.OrderBy(static r => r.StartedUtc);

    public SearchRun Get(string id) =>
        _runs.TryGetValue(id, out var run)
            ? run
            : throw new ToolException($"No search '{id}'. Running ones: {string.Join(", ", _runs.Keys)}.");

    public SearchRun Start(SearchRequest request)
    {
        var config = YamlConfigLoader.FromYaml(request.Jaml);
        if (!MotelyScoreCutoff.TryParse(request.Cutoff, out var cutoff, out var cutoffError))
            throw new ToolException($"cutoff: {cutoffError}");

        IMotelySearchSettings baseSettings;
        try
        {
            baseSettings = MotelySearchBuilder.CreateSettings(config, cutoff.EngineCutoff);
        }
        catch (InvalidOperationException ex)
        {
            throw new ToolException(ex.Message);
        }

        var deck = request.Deck ?? config.Deck;
        var stake = request.Stake ?? config.Stake;
        var run = new SearchRun
        {
            Id = $"s{Interlocked.Increment(ref _next)}",
            Name = config.Name ?? "unnamed filter",
            Deck = deck,
            Stake = stake,
            Mode = request.Mode,
            TallyLabels = [.. config.Should.Select(MotelySearchBuilder.DefaultTallyLabel)],
            Cancel = new CancellationTokenSource(),
        };

        var settings = ApplyMode(
            baseSettings.WithDeck(deck).WithStake(stake).WithThreadCount(request.Threads),
            request
        );

        // A filter with no clauses has no score provider; then finds arrive only on the
        // seed-match channel. With one they arrive on both, so listen to exactly one.
        settings = settings.SeedScoreDesc is not null
            ? settings
                .WithAutoScoreCutoff(cutoff.IsAuto)
                .WithScoredResultCallback(result =>
                {
                    if (cutoff.ShouldEmit(result.Score))
                        run.Add(result.Seed, result.Score, result.TallyValuesSpan.ToArray());
                })
            : settings.WithSeedMatchCallback(line =>
            {
                int comma = line.IndexOf(',');
                run.Add(comma < 0 ? line : line[..comma], 0, []);
            });

        settings = settings.WithProgressReportIntervalMs(1000).WithProgressCallback(run.Report);
        if (request.StopAfter is { } limit)
            settings = settings.StopAfter(limit);

        run.Search = settings.Start(run.Cancel.Token);
        _runs[run.Id] = run;
        return run;
    }

    private static IMotelySearchSettings ApplyMode(IMotelySearchSettings settings, SearchRequest request) =>
        request.Mode switch
        {
            "random" => settings.WithRandomSearch(request.Count ?? 1_000_000),
            "keyword" => settings.WithKeywordSearch(Seeds.Normalize(request.Keywords, "keywords"), MotelyGlobals.SeedDigits),
            "seeds" => settings.WithSeedList(Seeds.Normalize(request.Seeds, "seeds")),
            "sequential" => Sequential(settings, request),
            _ => throw new ToolException($"mode '{request.Mode}': use sequential, random, keyword or seeds."),
        };

    private static IMotelySearchSettings Sequential(IMotelySearchSettings settings, SearchRequest request)
    {
        const int batchCharacterCount = 3;
        long total = MotelyGlobals.SequentialBatchCount(batchCharacterCount);
        long start = request.StartBatch ?? 0;
        if (start < 0 || start >= total)
            throw new ToolException($"startBatch must be 0..{total - 1}.");
        settings = settings.WithBatchCharacterCount(batchCharacterCount).WithSequentialSearch().WithStartBatchIndex(start);
        if (request.EndBatch is { } end)
        {
            if (end <= start)
                throw new ToolException($"endBatch {end} must be greater than startBatch {start}.");
            settings = settings.WithEndBatchIndex(end);
        }
        return settings;
    }
}

public sealed record SearchRequest(
    string Jaml,
    string Mode,
    MotelyDeck? Deck,
    MotelyStake? Stake,
    int Threads,
    string? Cutoff,
    long? StopAfter,
    int? Count,
    string[]? Keywords,
    string[]? Seeds,
    long? StartBatch,
    long? EndBatch
);

public static class Seeds
{
    /// <summary>Trim, upper-case, 0 to O, then 1-8 characters of 1-9/A-Z or a clear error.</summary>
    public static string[] Normalize(string[]? input, string field)
    {
        if (input is not { Length: > 0 })
            throw new ToolException($"{field}: give at least one.");
        var result = new string[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            var seed = MotelyGlobals.NormalizeSeed(input[i]);
            if (
                seed.Length is < 1 or > MotelyGlobals.MaxSeedLength
                || !seed.All(static c => Array.IndexOf(MotelyGlobals.SeedDigits, c) >= 0)
            )
                throw new ToolException($"{field}: '{input[i]}' is not 1-8 characters of 1-9 and A-Z.");
            result[i] = seed;
        }
        return result;
    }
}

/// <summary>A mistake in the tool call: reported back to the model as an error result, not a crash.</summary>
public sealed class ToolException(string message) : Exception(message);
