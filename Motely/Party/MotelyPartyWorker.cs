using Motely.Filters;

namespace Motely.Party;

/// <summary>
/// One leased slice of a Search Party: engine batches [<see cref="StartBlock"/>,
/// <see cref="StartBlock"/> + <see cref="BlockCount"/>) at <see cref="BatchChars"/>, end exclusive.
/// A party block is an engine batch index, so the lease maps 1:1 onto
/// <c>WithStartBatchIndex</c> / <c>WithEndBatchIndex</c>; one block is
/// <see cref="MotelyGlobals.SeedsPerSequentialBatch"/> seeds.
/// </summary>
public sealed record PartyLease(
    string PartyId,
    string Jaml,
    int BatchChars,
    long StartBlock,
    long BlockCount,
    long TotalBlocks,
    string WorkerToken
);

/// <summary>A lease, or the party is settled (<see cref="Reason"/>: complete, exhausted, cancelled…).</summary>
public sealed record PartyNext(PartyLease? Lease, string? Reason = null);

public sealed record PartyReportResult(int Confirmed, int Rejected);

/// <summary>What a worker did before it stopped. <see cref="Error"/> is set when it stopped on a
/// problem it cannot retry past (a JAML or lease this build cannot run).</summary>
public sealed record PartyRunSummary(
    int Leases,
    long SeedsSearched,
    int Confirmed,
    string StopReason,
    string? Error
);

/// <summary>
/// Where leases come from and reports go: seedfinder.app over HTTP in the CLI
/// (<c>HttpPartyCoordinator</c>), a fake in tests. The server re-runs every reported seed against
/// the party's JAML, so a report carries seed strings only, never local scores.
/// </summary>
public interface IPartyCoordinator
{
    Task<PartyNext> NextAsync(string partyId, CancellationToken cancellationToken);

    /// <summary>Extends the lease's server-side TTL (60s) while it is being searched.</summary>
    Task HeartbeatAsync(PartyLease lease, CancellationToken cancellationToken);

    /// <summary>Completes the lease. Sent even with no seeds: an unreported lease waits out its TTL.</summary>
    Task<PartyReportResult> ReportAsync(
        PartyLease lease,
        IReadOnlyList<string> seeds,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// Grinds a Search Party: lease, search the leased batches with the same
/// <see cref="MotelySearchBuilder"/> settings every other host uses, heartbeat while it runs,
/// report the finds, repeat until the party is settled or the token fires.
/// </summary>
public sealed class MotelyPartyWorker(IPartyCoordinator coordinator)
{
    /// <summary>The most seeds one report may carry; the server records at most 500 per party.</summary>
    public const int ReportCap = 1000;

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Wait before asking again after the coordinator could not be reached.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Applied to every lease's settings after the batch range: threads, progress, cutoff.</summary>
    public Func<IMotelySearchSettings, IMotelySearchSettings>? Configure { get; init; }

    /// <summary>
    /// Builds the sink that sees every find as it is found (the report cap never limits it), from
    /// the lease's JAML so it can label the tally columns. Built once per distinct JAML.
    /// </summary>
    public Func<JamlConfig, IMotelyResultSink>? CreateSink { get; init; }

    private IMotelyResultSink? _sink;
    private string? _sinkJaml;

    public Action<string>? Log { get; init; }

    public async Task<PartyRunSummary> RunAsync(string partyId, CancellationToken cancellationToken)
    {
        try
        {
            return await RunLeasesAsync(partyId, cancellationToken);
        }
        finally
        {
            _sink?.Dispose();
            _sink = null;
            _sinkJaml = null;
        }
    }

    private async Task<PartyRunSummary> RunLeasesAsync(string partyId, CancellationToken cancellationToken)
    {
        int leases = 0;
        long seedsSearched = 0;
        int confirmed = 0;

        PartyRunSummary Stop(string reason, string? error = null) =>
            new(leases, seedsSearched, confirmed, reason, error);

        while (!cancellationToken.IsCancellationRequested)
        {
            PartyNext next;
            try
            {
                next = await coordinator.NextAsync(partyId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Lease failed: {ex.Message}. Retrying in {RetryDelay.TotalSeconds:0}s.");
                if (!await DelayAsync(RetryDelay, cancellationToken))
                    break;
                continue;
            }

            if (next.Lease is not { } lease)
                return Stop(next.Reason ?? "no work remaining");

            // A lease or JAML this build cannot run would fail the same way on every retry.
            if (LeaseError(lease) is { } leaseError)
                return Stop("bad lease", leaseError);
            if (!JamlConfigLoader.TryLoad(lease.Jaml, out var config, out var jamlError) || config is null)
                return Stop("bad JAML", jamlError);

            Log?.Invoke(
                $"Lease: blocks [{lease.StartBlock}, {lease.StartBlock + lease.BlockCount}) of {lease.TotalBlocks} at batchChars {lease.BatchChars}"
            );

            List<string> seeds;
            long searched;
            try
            {
                (seeds, searched) = await GrindAsync(lease, config, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Unreported, the lease expires and is handed to another worker.
                break;
            }

            try
            {
                var result = await coordinator.ReportAsync(lease, seeds, cancellationToken);
                confirmed += result.Confirmed;
                Log?.Invoke($"Reported {seeds.Count} seeds: {result.Confirmed} confirmed, {result.Rejected} rejected.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Not counted: the server expires the lease and re-leases the range.
                Log?.Invoke($"Report failed: {ex.Message}. The lease will expire and be re-leased.");
                continue;
            }

            leases++;
            seedsSearched += searched;
        }

        return Stop("cancelled");
    }

    private async Task<(List<string> Seeds, long Searched)> GrindAsync(
        PartyLease lease,
        JamlConfig config,
        CancellationToken cancellationToken
    )
    {
        if (CreateSink is not null && lease.Jaml != _sinkJaml)
        {
            _sink?.Dispose();
            _sink = CreateSink(config);
            _sinkJaml = lease.Jaml;
        }
        var sink = _sink;

        var seeds = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        void Collect(string seed)
        {
            lock (seeds)
            {
                if (seeds.Count < ReportCap && unique.Add(seed))
                    seeds.Add(seed);
            }
        }

        var settings = MotelySearchBuilder
            .CreateSettings(config)
            .WithSequentialSearch()
            .WithBatchCharacterCount(lease.BatchChars)
            .WithStartBatchIndex(lease.StartBlock)
            .WithEndBatchIndex(lease.StartBlock + lease.BlockCount)
            .WithQuietMode(true);
        if (Configure is not null)
            settings = Configure(settings);

        // With a score provider every find also arrives, unscored, on the seed-match channel, so
        // listen to exactly one channel (the same rule the browser host follows).
        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(t =>
            {
                sink?.OnScored(in t);
                Collect(t.Seed);
            })
            : settings.WithSeedMatchCallback(line =>
            {
                sink?.OnSeed(line);
                int comma = line.IndexOf(',');
                Collect(comma < 0 ? line : line[..comma]);
            });

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = HeartbeatLoopAsync(lease, heartbeatCts.Token);
        try
        {
            using var search = settings.Start(cancellationToken);
            await search.WaitForCompletionAsync(cancellationToken);
            sink?.Flush();
            lock (seeds)
                return (seeds, search.TotalSeedsSearched);
        }
        finally
        {
            heartbeatCts.Cancel();
            await heartbeat;
        }
    }

    private async Task HeartbeatLoopAsync(PartyLease lease, CancellationToken cancellationToken)
    {
        while (await DelayAsync(HeartbeatInterval, cancellationToken))
        {
            try
            {
                await coordinator.HeartbeatAsync(lease, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Best effort: a missed beat only risks the lease expiring and being re-leased.
                Log?.Invoke($"Heartbeat failed: {ex.Message}");
            }
        }
    }

    /// <summary>Why the engine cannot search this lease, or null when it can.</summary>
    internal static string? LeaseError(PartyLease lease)
    {
        if (lease.BatchChars is < 1 or >= MotelyGlobals.MaxSeedLength)
            return $"batchChars {lease.BatchChars} is outside 1-{MotelyGlobals.MaxSeedLength - 1}.";
        long total = MotelyGlobals.SequentialBatchCount(lease.BatchChars);
        if (lease.StartBlock < 0 || lease.BlockCount <= 0 || lease.StartBlock > total - lease.BlockCount)
            return $"blocks [{lease.StartBlock}, {lease.StartBlock} + {lease.BlockCount}) are outside the {total:N0} batches at batchChars {lease.BatchChars}.";
        return null;
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
