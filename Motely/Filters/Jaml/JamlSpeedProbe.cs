using System.Diagnostics;

namespace Motely.Filters.Jaml;

/// <summary>
/// Times a filter on the machine it is about to run on, so every consumer — CLI, MCP, Wasm, TUI —
/// can print "~4m to the first match @ 38 M/s (measured)" instead of "unknown until a run has been
/// timed on this machine".
/// <para>
/// Motely is the calculator. One sequential batch at <see cref="ProbeBatchCharacterCount"/> is
/// 35⁴ = 1,500,625 seeds — well under a second on one thread — and it exercises the exact filter
/// chain and scorer the real search will run, so the figure already includes early-lane kills,
/// scoring cost, and everything the crunch model can only guess at. One thread is timed and
/// multiplied by the thread count the run will use; that is an upper bound under SMT, which is
/// why the report says "measured", not "guaranteed".
/// </para>
/// <para>
/// A throwaway 35³ batch runs first, untimed, to absorb JIT and pseudohash-cache warm-up so the
/// timed batch measures the filter and not the runtime.
/// </para>
/// <para>
/// No console, no filesystem, no reflection — this assembly is consumed by Motely.Wasm and
/// NativeAOT builds. Only static members; nothing new crosses the Bootsharp boundary.
/// </para>
/// </summary>
public static class JamlSpeedProbe
{
    /// <summary>35⁴ seeds — big enough to time, small enough never to be noticed.</summary>
    public const int ProbeBatchCharacterCount = 4;

    /// <summary>35³ seeds — the warm-up, untimed.</summary>
    public const int WarmupBatchCharacterCount = 3;

    /// <summary>
    /// Which batch to time. Any interior batch will do; a fixed one keeps the probe reproducible
    /// from run to run on the same machine. Reduced modulo the batch count for whatever character
    /// count is in play.
    /// </summary>
    public const long ProbeBatchIndex = 314_159;

    /// <summary>What one probe measured and what the report should use.</summary>
    /// <param name="SeedsSearched">Seeds the timed batch visited.</param>
    /// <param name="ElapsedSeconds">Wall-clock for the timed batch alone (warm-up excluded).</param>
    /// <param name="Threads">Thread count the real run will use; <see cref="Projected"/> scales by it.</param>
    public readonly record struct Result(long SeedsSearched, double ElapsedSeconds, int Threads)
    {
        /// <summary>Seeds per second on the single timed thread.</summary>
        public double PerThread => ElapsedSeconds > 0 ? SeedsSearched / ElapsedSeconds : 0;

        /// <summary>The figure the report uses: one thread's rate scaled to the run's thread count.</summary>
        public double Projected => PerThread * Threads;

        /// <summary>
        /// One report-style line saying exactly what was measured, in the same two-space /
        /// padded-label shape as <see cref="JamlRarityReport.Render"/>, so it slots in above it.
        /// </summary>
        public string Describe() =>
            $"  {"Probe:",-6} {SeedsSearched:N0} seeds on 1 thread in {ElapsedSeconds * 1000:N0} ms"
            + $" — {JamlRarityReport.Speed(PerThread)}/thread × {Threads} = {JamlRarityReport.Speed(Projected)}";
    }

    /// <summary>
    /// Run the probe. Returns null if the run was cancelled, the engine threw, or nothing was
    /// searched — the caller then falls back to the report's "unknown" wording, which is only
    /// honest when nothing was actually timed.
    /// </summary>
    /// <param name="config">The loaded JAML. A fresh plan is built from it, so the caller's real settings are never touched.</param>
    /// <param name="engineCutoff">Same score cutoff the real run pushes into the scorer, so the measured cost matches.</param>
    /// <param name="deck">Deck the real run will use.</param>
    /// <param name="stake">Stake the real run will use.</param>
    /// <param name="threads">Thread count the real run will use — what the single-thread rate is scaled by.</param>
    /// <param name="cancellationToken">Cancels the probe; a cancelled probe returns null.</param>
    public static async Task<Result?> MeasureAsync(
        JamlConfig config,
        int engineCutoff,
        MotelyDeck deck,
        MotelyStake stake,
        int threads,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using (var warm = Build(config, engineCutoff, deck, stake, WarmupBatchCharacterCount).Start(cancellationToken))
                await warm.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);

            var sw = Stopwatch.StartNew();
            using var timed = Build(config, engineCutoff, deck, stake, ProbeBatchCharacterCount).Start(cancellationToken);
            await timed.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            return Finish(timed.TotalSeedsSearched, sw.Elapsed.TotalSeconds, threads);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            // A probe must never take the real run down with it.
            return null;
        }
    }

    /// <summary>
    /// Synchronous twin of <see cref="MeasureAsync"/> for callers without an async path. Same
    /// contract: null when nothing was timed.
    /// </summary>
    public static Result? Measure(
        JamlConfig config,
        int engineCutoff,
        MotelyDeck deck,
        MotelyStake stake,
        int threads,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using (var warm = Build(config, engineCutoff, deck, stake, WarmupBatchCharacterCount).Start(cancellationToken))
                warm.AwaitCompletion();

            var sw = Stopwatch.StartNew();
            using var timed = Build(config, engineCutoff, deck, stake, ProbeBatchCharacterCount).Start(cancellationToken);
            timed.AwaitCompletion();
            sw.Stop();

            return Finish(timed.TotalSeedsSearched, sw.Elapsed.TotalSeconds, threads);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Result? Finish(long seeds, double seconds, int threads) =>
        seeds <= 0 || seconds <= 0 ? null : new Result(seeds, seconds, Math.Max(1, threads));

    /// <summary>
    /// A one-batch, one-thread, silent sequential search over the same filter chain. Built from a
    /// fresh plan every time because the settings builder mutates in place — sharing the real run's
    /// settings would leave it pinned to one thread and one batch.
    /// </summary>
    private static IMotelySearchSettings Build(
        JamlConfig config,
        int engineCutoff,
        MotelyDeck deck,
        MotelyStake stake,
        int batchCharacterCount
    )
    {
        long batches = (long)Math.Pow(35, 8 - batchCharacterCount);
        long index = ProbeBatchIndex % batches;

        return JamlSearchBuilder
            .CreatePlan(config, engineCutoff)
            .Settings.WithDeck(deck)
            .WithStake(stake)
            .WithSequentialSearch()
            .WithThreadCount(1)
            .WithBatchCharacterCount(batchCharacterCount)
            .WithStartBatchIndex(index)
            .WithEndBatchIndex(index + 1)
            .WithQuietMode(true);
    }
}
