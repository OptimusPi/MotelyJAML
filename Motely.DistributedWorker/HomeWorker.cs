using System.Diagnostics;
using Motely.Distributed;
using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.DistributedWorker;

/// <summary>Where claims come from and results go: MotelyHome over HTTP, or a fake in tests.</summary>
public interface IHome
{
    /// <summary>The next slice, or null when nothing is queued.</summary>
    Task<Work?> ClaimAsync(string worker, CancellationToken cancellationToken);

    Task DoneAsync(WorkDone done, CancellationToken cancellationToken);
}

/// <summary>
/// Claim, grind, report, repeat, forever: idle when the queue is empty, retry when home is
/// unreachable. A slice that was cancelled mid-grind is never reported; home hands it out again.
/// </summary>
public sealed class HomeWorker(IHome home, string name)
{
    public int Threads { get; init; } = Environment.ProcessorCount;

    /// <summary>How long to wait before asking again when nothing is queued.</summary>
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait after home could not be reached, or a slice could not be run.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Give up (return false from <see cref="RunAsync"/>) after this many claim failures in a row.</summary>
    public int MaxConsecutiveFailures { get; init; } = int.MaxValue;

    public Action<string>? Log { get; init; }
    public Action<MotelyProgress>? Progress { get; init; }

    public int Claims { get; private set; }
    public long SeedsSearched { get; private set; }
    public long Finds { get; private set; }

    /// <summary>False when home stopped answering for <see cref="MaxConsecutiveFailures"/> claims; true when cancelled.</summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        int failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            Work? work;
            try
            {
                work = await home.ClaimAsync(name, cancellationToken);
                failures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception ex)
            {
                if (++failures >= MaxConsecutiveFailures)
                {
                    Log?.Invoke($"Home unreachable {failures} times in a row: {ex.Message}");
                    return false;
                }
                Log?.Invoke($"Home unreachable: {ex.Message}. Retrying in {RetryDelay.TotalSeconds:0}s.");
                if (!await DelayAsync(RetryDelay, cancellationToken))
                    return true;
                continue;
            }

            if (work is null)
            {
                if (!await DelayAsync(IdleDelay, cancellationToken))
                    return true;
                continue;
            }

            WorkDone done;
            var clock = Stopwatch.StartNew();
            try
            {
                done = await GrindAsync(work, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception ex)
            {
                // Home validated the JAML when it was queued, so this is a version mismatch or a
                // bad range: say so and keep asking; the slice goes back on the pile when it expires.
                Log?.Invoke($"Cannot run {work.Filter} [{work.Start}, {work.End}): {ex.Message}");
                if (!await DelayAsync(RetryDelay, cancellationToken))
                    return true;
                continue;
            }

            try
            {
                await home.DoneAsync(done, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Report failed: {ex.Message}. Home will hand the slice out again.");
                continue;
            }

            Claims++;
            SeedsSearched += done.SeedsSearched;
            Finds += done.Seeds.Length;
            double seconds = Math.Max(clock.Elapsed.TotalSeconds, 0.001);
            Log?.Invoke(
                $"{work.Filter} [{work.Start}, {work.End}): {done.SeedsSearched:N0} seeds in {seconds:0.0}s ({done.SeedsSearched / seconds:N0}/s), {done.Seeds.Length} finds"
            );
        }
        return true;
    }

    private async Task<WorkDone> GrindAsync(Work work, CancellationToken cancellationToken)
    {
        var config = JamlConfigLoader.FromJaml(work.Jaml);
        var seeds = new Dictionary<string, int>(StringComparer.Ordinal);
        bool capped = false;
        void Found(string seed, int score)
        {
            lock (seeds)
            {
                if (seeds.Count >= WorkDone.MaxSeeds)
                    capped = true;
                else
                    seeds.TryAdd(seed, score);
            }
        }

        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSequentialSearch()
            .WithBatchCharacterCount(work.BatchChars)
            .WithStartBatchIndex(work.Start)
            .WithEndBatchIndex(work.End)
            .WithThreadCount(Threads)
            .WithQuietMode(true);
        if (Progress is not null)
            settings = settings.WithProgressCallback(Progress);

        // With a score provider every find also arrives, unscored, on the seed-match channel:
        // listen to exactly one.
        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(t => Found(t.Seed, t.Score))
            : settings.WithSeedMatchCallback(line =>
            {
                int comma = line.IndexOf(',');
                Found(comma < 0 ? line : line[..comma], 0);
            });

        using var search = settings.Start(cancellationToken);
        await search.WaitForCompletionAsync(cancellationToken);

        if (capped)
            Log?.Invoke($"{work.Filter} [{work.Start}, {work.End}): more than {WorkDone.MaxSeeds:N0} finds; reporting the first {WorkDone.MaxSeeds:N0}. Tighten the filter.");
        lock (seeds)
            return new WorkDone(work.Filter, name, work.Start, work.End, search.TotalSeedsSearched,
                [.. seeds.Select(kv => new FoundSeed(kv.Key, kv.Value))]);
    }

    /// <summary>False when the token fired during the wait.</summary>
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
