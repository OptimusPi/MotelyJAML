using System.Diagnostics;
using System.Text.Json;

namespace Motely.Party;

public sealed class PartyWorkerOptions
{
    private readonly Func<PartyLease, int, CancellationToken, Task<PartySweepResult>>? _sweep;

    public int Threads { get; init; } = Environment.ProcessorCount;

    /// <summary>Enable OpenCL FP64 GPU acceleration for each leased block range.</summary>
    public bool UseGpu { get; init; }

    /// <summary>Stop after one lease is swept and reported.</summary>
    public bool Once { get; init; }

    /// <summary>The coordinator's lease TTL is 60 s; beat well inside it.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long to rest when every remaining block is leased to other workers.</summary>
    public TimeSpan FullyLeasedDelay { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Pause after a failed call before trying again.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Consecutive failed lease requests before the worker gives up.</summary>
    public int MaxConsecutiveErrors { get; init; } = 10;

    /// <summary>Tries for the final report of a lease.</summary>
    public int ReportAttempts { get; init; } = 3;

    /// <summary>"fully leased" answers in a row before stopping; null waits forever.</summary>
    public int? MaxFullyLeasedWaits { get; init; }

    /// <summary>Runs one lease; the engine sweep unless a test stands in for it.</summary>
    public Func<PartyLease, int, CancellationToken, Task<PartySweepResult>> Sweep
    {
        get => _sweep ?? ((lease, threads, ct) => PartySweep.RunAsync(lease, threads, UseGpu, ct));
        init => _sweep = value;
    }
}

public enum PartyWorkerOutcome
{
    /// <summary>The coordinator said stop: complete, exhausted, cancelled, ...</summary>
    Stopped,

    /// <summary>--once: one lease done.</summary>
    Once,

    /// <summary>Ctrl+C. A half-done lease was not reported.</summary>
    Interrupted,

    /// <summary>Too many "fully leased" answers in a row (<see cref="PartyWorkerOptions.MaxFullyLeasedWaits"/>).</summary>
    WaitLimit,

    /// <summary>The server kept failing, or a lease could not run.</summary>
    Failed,
}

public sealed record PartyWorkerSummary(
    PartyWorkerOutcome Outcome,
    string Reason,
    int Leases,
    long Blocks,
    long SeedsSearched,
    long Matches,
    long Reported,
    long Confirmed,
    TimeSpan Elapsed
);

/// <summary>
/// The Search Party worker loop: lease → sweep → report → next, until the coordinator gives a
/// stop reason. While a lease runs it is kept alive with heartbeats. Ctrl+C stops the sweep and
/// sends no report for the half-done lease: it expires on the server and is leased again.
/// </summary>
public sealed class PartyWorker(PartyClient client, string partyId, PartyWorkerOptions options, TextWriter log)
{
    /// <summary>The coordinator's "come back later" reason; every other done reason is final.</summary>
    public const string FullyLeased = "fully leased";

    /// <summary>The coordinator's answer for a party id it has never heard of.</summary>
    public const string UnknownParty = "unknown party";

    /// <summary>The process exit code for a run: 0 done, 1 failed or no such party, 130 interrupted.</summary>
    public static int ExitCode(PartyWorkerSummary summary) =>
        summary.Outcome switch
        {
            PartyWorkerOutcome.Interrupted => 130,
            PartyWorkerOutcome.Failed => 1,
            PartyWorkerOutcome.Stopped when summary.Reason == UnknownParty => 1,
            _ => 0,
        };

    private int _leases;
    private long _blocks, _seeds, _matches, _reported, _confirmed;

    public async Task<PartyWorkerSummary> RunAsync(CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        int errors = 0, waits = 0;

        PartyWorkerSummary Finish(PartyWorkerOutcome outcome, string reason) =>
            new(outcome, reason, _leases, _blocks, _seeds, _matches, _reported, _confirmed, clock.Elapsed);

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
                return Finish(PartyWorkerOutcome.Interrupted, "interrupted");

            PartyNextResponse next;
            try
            {
                next = await client.NextAsync(partyId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(PartyWorkerOutcome.Interrupted, "interrupted");
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                errors++;
                log.WriteLine($"lease request failed ({errors}/{options.MaxConsecutiveErrors}): {ex.Message}");
                if (errors >= options.MaxConsecutiveErrors)
                    return Finish(PartyWorkerOutcome.Failed, $"lease requests kept failing: {ex.Message}");
                if (!await DelayAsync(options.RetryDelay, cancellationToken).ConfigureAwait(false))
                    return Finish(PartyWorkerOutcome.Interrupted, "interrupted");
                continue;
            }
            errors = 0;

            if (next.Lease is not { } lease)
            {
                string reason = next.Reason ?? next.Error ?? "no lease in the answer";
                if (!next.Done)
                    return Finish(PartyWorkerOutcome.Failed, reason);
                if (reason != FullyLeased)
                    return Finish(PartyWorkerOutcome.Stopped, reason);

                waits++;
                if (waits >= options.MaxFullyLeasedWaits)
                    return Finish(PartyWorkerOutcome.WaitLimit, FullyLeased);
                if (waits == 1)
                    log.WriteLine(
                        $"every open block is leased to other workers; asking again every {options.FullyLeasedDelay.TotalSeconds:0.#} s"
                    );
                if (!await DelayAsync(options.FullyLeasedDelay, cancellationToken).ConfigureAwait(false))
                    return Finish(PartyWorkerOutcome.Interrupted, "interrupted");
                continue;
            }
            waits = 0;

            var (done, failure) = await RunLeaseAsync(lease, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
                return Finish(PartyWorkerOutcome.Failed, failure);
            if (!done)
                return Finish(PartyWorkerOutcome.Interrupted, "interrupted");
            if (options.Once)
                return Finish(PartyWorkerOutcome.Once, "--once");
        }
    }

    /// <summary>Sweeps and reports one lease. done = false when interrupted before the report.</summary>
    private async Task<(bool Done, string? Failure)> RunLeaseAsync(PartyLease lease, CancellationToken cancellationToken)
    {
        long perBlock = MotelyGlobals.SeedsPerSequentialBatch(Math.Clamp(lease.BatchChars, 1, MotelyGlobals.MaxSeedLength - 1));
        log.Write($"lease blocks {lease.StartBlock}-{lease.EndBlock - 1} ({lease.BlockCount} × {perBlock:N0} seeds) … ");
        log.Flush();

        var notes = new List<string>();
        using var beating = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeats = HeartbeatLoopAsync(lease, notes, beating.Token);

        PartySweepResult sweep;
        try
        {
            sweep = await options.Sweep(lease, options.Threads, cancellationToken).ConfigureAwait(false);
        }
        catch (PartyLeaseException ex)
        {
            beating.Cancel();
            await heartbeats.ConfigureAwait(false);
            log.WriteLine("not run.");
            return (false, $"lease cannot run: {ex.Message}");
        }
        beating.Cancel();
        await heartbeats.ConfigureAwait(false);

        _seeds += sweep.SeedsSearched;
        if (sweep.Cancelled)
        {
            log.WriteLine(
                $"interrupted after {sweep.SeedsSearched:N0} seeds; no report sent (the lease expires and is leased again)."
            );
            return (false, null);
        }

        _leases++;
        _blocks += lease.BlockCount;
        _matches += sweep.Matched;
        string capped = sweep.Matched > sweep.Seeds.Count ? $" (best {sweep.Seeds.Count:N0} sent)" : "";

        string outcome = await ReportAsync(lease, sweep.Seeds, cancellationToken).ConfigureAwait(false);
        string extra = notes.Count > 0 ? $" [{string.Join("; ", notes.Distinct())}]" : "";
        log.WriteLine($"{sweep.Matched:N0} matches{capped}, {outcome}{extra}");
        return (true, null);
    }

    private async Task<string> ReportAsync(PartyLease lease, IReadOnlyList<string> seeds, CancellationToken cancellationToken)
    {
        string lastError = "";
        for (int attempt = 1; attempt <= options.ReportAttempts; attempt++)
        {
            try
            {
                // Not cancelled by Ctrl+C: the sweep is whole, so the report is worth sending.
                var answer = await client.ReportAsync(lease, seeds, CancellationToken.None).ConfigureAwait(false);
                int confirmed = answer.Confirmed?.Length ?? 0;
                int rejected = answer.Rejected?.Length ?? 0;
                _reported += seeds.Count;
                _confirmed += confirmed;
                return rejected > 0
                    ? $"reported: {confirmed:N0} confirmed, {rejected:N0} rejected"
                    : $"reported: {confirmed:N0} confirmed";
            }
            catch (PartyServerException ex) when (ex.StatusCode is >= 400 and < 500)
            {
                // The server refused it (e.g. the lease outlived its TTL and went to someone else):
                // trying again won't change that.
                return $"report refused: {ex.Message}";
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastError = ex.Message;
                if (attempt < options.ReportAttempts && !cancellationToken.IsCancellationRequested)
                    await Task.Delay(options.RetryDelay, CancellationToken.None).ConfigureAwait(false);
            }
        }
        return $"report failed: {lastError}";
    }

    private async Task HeartbeatLoopAsync(PartyLease lease, List<string> notes, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(options.HeartbeatInterval, token).ConfigureAwait(false);
                try
                {
                    if (!await client.HeartbeatAsync(lease, token).ConfigureAwait(false))
                        notes.Add("heartbeat: the server no longer holds this lease");
                }
                catch (Exception ex) when (IsTransient(ex) && !token.IsCancellationRequested)
                {
                    notes.Add($"heartbeat failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Network trouble, a timeout, a bad status or body: worth another try.</summary>
    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or PartyServerException or JsonException or TaskCanceledException or IOException;

    /// <summary>The closing lines, for stderr.</summary>
    public static void WriteSummary(TextWriter log, string partyId, PartyWorkerSummary summary)
    {
        string why = summary.Outcome switch
        {
            PartyWorkerOutcome.Stopped when summary.Reason == UnknownParty => "stopped: the coordinator has no such party",
            PartyWorkerOutcome.Stopped => $"stopped: the party is {summary.Reason}",
            PartyWorkerOutcome.Once => "stopped after one lease (--once)",
            PartyWorkerOutcome.Interrupted => "interrupted",
            PartyWorkerOutcome.WaitLimit => "stopped: every block stayed leased to other workers",
            _ => $"failed: {summary.Reason}",
        };
        log.WriteLine($"party {partyId}: {why}.");
        log.WriteLine(
            $"  {summary.Leases:N0} lease(s), {summary.Blocks:N0} blocks, {summary.SeedsSearched:N0} seeds searched in {summary.Elapsed:hh\\:mm\\:ss}"
        );
        log.WriteLine(
            $"  {summary.Matches:N0} matches, {summary.Reported:N0} reported, {summary.Confirmed:N0} confirmed by the server"
        );
    }
}
