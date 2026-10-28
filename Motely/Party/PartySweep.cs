using Motely.Enums;
using Motely.Filters;

namespace Motely.Party;

/// <summary>A lease the engine can't run: its JAML doesn't load, or its deck/stake isn't a name.</summary>
public sealed class PartyLeaseException(string message) : Exception(message);

/// <param name="Seeds">What to report: the best <see cref="PartySweep.MaxReportSeeds"/> by score.</param>
/// <param name="Matched">Distinct seeds the engine matched in the range.</param>
public sealed record PartySweepResult(IReadOnlyList<string> Seeds, int Matched, long SeedsSearched, bool Cancelled);

/// <summary>
/// Runs one lease through the engine. The block → seed mapping is the engine's own sequential
/// batch numbering (<c>WithBatchCharacterCount(n).WithStartBatchIndex(b).WithEndBatchIndex(e)</c>),
/// so a lease means here exactly what it means to the coordinator.
/// </summary>
public static class PartySweep
{
    /// <summary>The coordinator takes at most this many seeds per report.</summary>
    public const int MaxReportSeeds = 1000;

    /// <summary>
    /// The engine settings for a lease, without callbacks. No score floor and no auto cutoff: the
    /// coordinator re-verifies candidates with none (any integer total), so neither a JAML
    /// <c>cutoff:</c> nor the engine's default auto cutoff (which, once finds flood in, passes only
    /// seeds that tie or beat the best so far) may drop a seed here that it would confirm there.
    /// </summary>
    public static IMotelySearchSettings CreateSettings(PartyLease lease, int threads, bool useGpu = false)
    {
        if (lease.BatchChars is < 1 or >= MotelyGlobals.MaxSeedLength)
            throw new PartyLeaseException($"batchChars {lease.BatchChars} is outside 1-{MotelyGlobals.MaxSeedLength - 1}.");
        if (lease.StartBlock < 0 || lease.BlockCount < 1 || lease.EndBlock > MotelyGlobals.SequentialBatchCount(lease.BatchChars))
            throw new PartyLeaseException(
                $"blocks [{lease.StartBlock}, {lease.EndBlock}) are outside the keyspace at batchChars {lease.BatchChars}."
            );

        if (!JamlConfigLoader.TryLoad(lease.Jaml, out var config, out var loadError) || config is null)
            throw new PartyLeaseException($"the party's JAML does not load: {loadError}");

        JamlSearchPlan plan;
        try
        {
            plan = MotelySearchBuilder.CreatePlan(config, int.MinValue);
        }
        catch (InvalidOperationException ex)
        {
            throw new PartyLeaseException($"the party's JAML does not build a search: {ex.Message}");
        }

        var settings = plan.Settings;
        try
        {
            if (lease.Deck is { } deck)
                settings = settings.WithDeck(JamlEnumName.Parse<MotelyDeck>(deck));
            if (lease.Stake is { } stake)
                settings = settings.WithStake(JamlEnumName.Parse<MotelyStake>(stake));
        }
        catch (FormatException ex)
        {
            throw new PartyLeaseException($"the lease's deck/stake: {ex.Message}");
        }

        return settings
            .WithSourceConfig(config)
            .WithThreadCount(Math.Max(1, threads))
            .WithBatchCharacterCount(lease.BatchChars)
            .WithSequentialSearch()
            .WithStartBatchIndex(lease.StartBlock)
            .WithEndBatchIndex(lease.EndBlock)
            .WithAutoScoreCutoff(false)
            .WithGpuSearch(useGpu)
            .WithQuietMode(true);
    }

    /// <summary>
    /// Sweeps the lease's blocks. Cancellation stops the sweep; the result then says
    /// <see cref="PartySweepResult.Cancelled"/> and must not be reported (the range is half done).
    /// </summary>
    public static Task<PartySweepResult> RunAsync(PartyLease lease, int threads, CancellationToken cancellationToken) =>
        RunAsync(lease, threads, useGpu: false, cancellationToken);

    /// <inheritdoc cref="RunAsync(PartyLease, int, CancellationToken)"/>
    public static async Task<PartySweepResult> RunAsync(
        PartyLease lease,
        int threads,
        bool useGpu,
        CancellationToken cancellationToken
    )
    {
        var settings = CreateSettings(lease, threads, useGpu);
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        void Add(string seed, int score)
        {
            lock (found)
            {
                if (!found.TryGetValue(seed, out int old) || score > old)
                    found[seed] = score;
            }
        }

        // A filter with no clauses has no score provider, and then finds arrive only on the
        // seed-match channel. With a provider they arrive on both, so listen to exactly one.
        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(r => Add(r.Seed, r.Score))
            : settings.WithSeedMatchCallback(line =>
            {
                int comma = line.IndexOf(',');
                Add(comma < 0 ? line : line[..comma], 0);
            });

        using var search = settings.Start(cancellationToken);
        bool cancelled = false;
        try
        {
            await search.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            // Let each worker thread finish the seeds it is on before the search is disposed.
            await search.WaitForCompletionAsync().ConfigureAwait(false);
        }

        lock (found)
        {
            return new PartySweepResult(Best(found, MaxReportSeeds), found.Count, search.TotalSeedsSearched, cancelled);
        }
    }

    /// <summary>The <paramref name="max"/> best seeds by score; ties go to the lower seed (ordinal).</summary>
    public static IReadOnlyList<string> Best(IEnumerable<KeyValuePair<string, int>> scored, int max) =>
        [
            .. scored
                .OrderByDescending(static kv => kv.Value)
                .ThenBy(static kv => kv.Key, StringComparer.Ordinal)
                .Take(max)
                .Select(static kv => kv.Key),
        ];
}
