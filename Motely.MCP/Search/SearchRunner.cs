using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Motely;
using Motely.Filters;

namespace Motely.MCP.Search;

/// <summary>
/// What a search yields as it runs: live progress, each matched seed, a terminal summary, or an
/// error. The engine's callbacks are synchronous and fire on its own threads, so
/// <see cref="SearchRunner"/> bridges them to async callers over a channel.
/// </summary>
public abstract record SearchEvent
{
    private SearchEvent() { }

    public sealed record Progress(int Percent, long Searched, long Matched, double Rate) : SearchEvent;

    public sealed record Match(string Seed, double Score) : SearchEvent;

    public sealed record Done(
        long TotalSearched,
        long Matched,
        TimeSpan Elapsed,
        double Rate,
        bool StoppedOnLimit,
        IReadOnlyList<string> Matches
    ) : SearchEvent;

    public sealed record Error(string Message) : SearchEvent;
}

/// <summary>
/// Shared search execution behind both the MCP <c>search</c> tool and the AG-UI agent: resolve a
/// filter, load it, build a plan, apply a mode, then run the engine and stream progress + matches.
/// </summary>
public sealed class SearchRunner
{
    /// <summary>
    /// Yields progress, matches and a terminal summary for <paramref name="filter"/>. A null or
    /// empty <paramref name="mode"/> replays the filter's saved seeds (fast, deterministic);
    /// "keyword &lt;kw&gt;", "random &lt;n&gt;" and "sequential" select the other engines.
    /// </summary>
    public async IAsyncEnumerable<SearchEvent> RunAsync(
        string filter,
        string? mode,
        int? collect,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var path = FilterCheck.ResolvePath(filter);
        if (!File.Exists(path))
        {
            File.AppendAllText(
                @"C:\Users\pifre\AppData\Local\Temp\opencode\runnerdiag.txt",
                $"path={path} | dir={Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR")} | exists={File.Exists(path)} | cwd={Environment.CurrentDirectory}\n"
            );
            yield return new SearchEvent.Error($"no filter at {path}");
            yield break;
        }

        if (!TryLoadConfig(path, out var config, out var loadError))
        {
            yield return new SearchEvent.Error(loadError);
            yield break;
        }

        if (!TryCreatePlan(config, out var plan, out var planError))
        {
            yield return new SearchEvent.Error(planError);
            yield break;
        }

        var (keyword, random, replay, modeError) = ParseMode(mode);
        if (modeError is not null)
        {
            yield return new SearchEvent.Error(modeError);
            yield break;
        }

        var input = new CliSearchMode.Input(
            SeedsArgument: null,
            Replay: replay,
            JamlPath: path,
            FilterId: config.Id,
            JamlSeeds: config.Seeds,
            KeywordInputs: keyword is not null ? [keyword] : [],
            PaddingCharsOption: null,
            RandomCount: random,
            AestheticName: null,
            StartBatch: null,
            EndBatch: null,
            StartPercent: null,
            StartSeed: null,
            StopSeed: null,
            BatchCharacterCount: null
        );

        if (!CliSearchMode.TryApplySearchMode(plan.Settings, input, out var applyError, out var settings))
        {
            yield return new SearchEvent.Error(applyError);
            yield break;
        }

        if (collect is { } limit)
            settings = settings.StopAfter(limit);

        var channel = Channel.CreateUnbounded<SearchEvent>();
        var matches = new List<string>();

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
                lock (matches)
                {
                    matches.Add(r.Seed);
                }
                channel.Writer.TryWrite(new SearchEvent.Match(r.Seed, r.Score));
            });
        else
            settings = settings.WithSeedMatchCallback(line =>
            {
                lock (matches)
                {
                    matches.Add(line);
                }
                channel.Writer.TryWrite(new SearchEvent.Match(line, 0));
            });

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

        yield return new SearchEvent.Done(
            search.TotalSeedsSearched,
            search.MatchingSeeds,
            TimeSpan.FromMilliseconds(search.ElapsedMs),
            search.SeedsPerSecond,
            search.StoppedOnMatchLimit,
            [.. matches]
        );
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
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JamlSearchPlan? plan,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error
    )
    {
        plan = null;
        error = null;
        try
        {
            plan = MotelySearchBuilder.CreatePlan(config);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Splits a mode string into (keyword, random, replay); exactly one is set, or an error.</summary>
    private static (string? keyword, int? random, bool replay, string? error) ParseMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode == "replay")
            return (null, null, true, null);

        if (mode.StartsWith("keyword ", StringComparison.OrdinalIgnoreCase))
        {
            var kw = mode["keyword ".Length..].Trim();
            return kw.Length == 0
                ? (null, null, false, "Error: 'keyword' needs a seed word after it.")
                : (kw, null, false, null);
        }

        if (mode.StartsWith("random ", StringComparison.OrdinalIgnoreCase))
        {
            var n = mode["random ".Length..].Trim();
            return int.TryParse(n, out var count) && count > 0
                ? (null, count, false, null)
                : (null, null, false, $"Error: 'random' needs a positive count, got '{n}'.");
        }

        if (mode == "sequential")
            return (null, null, false, null);

        return (
            null,
            null,
            false,
            $"Error: unknown mode '{mode}'. Use replay, keyword <kw>, random <n>, or sequential."
        );
    }
}
