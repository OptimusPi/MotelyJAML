using System.Collections.Concurrent;
using Motely;
using Motely.DataLake;
using Motely.DistributedWorker;
using Motely.Filters;
using Motely.Filters.Jaml;

/// <summary>
/// Motely Distributed Worker — AOT native Linux executable.
///
/// Connects to the seed-finder pool and claims one block (35^5 seeds) at a time.
/// <c>Motely.DataLake</c> writes each filter's seeds to a plain text file under the data root.
///
/// Usage:
///   MotelyWorker --pool https://www.seedfinder.app
///
/// Options:
///   --threads N           Motely search thread count for each claimed block (SIMD workers inside one block)
///   --worker-id id        Worker identifier (default: hostname-pid)
///   --filter filterId     Only claim blocks for this filter (optional; omit for any active filter)
///   --local-db ./dir      Seed lake data root (default: Seeds)
///                         Set to "-" to disable local saving.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // ── Parse args ──────────────────────────────────────────────────
        string? url = null, workerId = null, filterId = null, partyId = null, localDbDir = "Seeds";
        string serverUrl = "https://www.seedfinder.app";
        int threads = Environment.ProcessorCount;

        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--pool": url = args[++i]; break;
                case "--party": partyId = args[++i]; break;
                case "--server": serverUrl = args[++i]; break;
                case "--threads": threads = int.Parse(args[++i]); break;
                case "--worker-id":
                case "--workerid": // common typo / convenience
                    workerId = args[++i]; break;
                case "--filter": filterId = args[++i]; break;
                case "--local-db": localDbDir = args[++i]; break;
            }
        }

        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(partyId))
        {
            Console.Error.WriteLine("[MotelyWorker] --pool and --party are mutually exclusive — pick one mode.");
            return 1;
        }

        if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(partyId))
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  MotelyWorker --party <partyId> [--server https://www.seedfinder.app]");
            Console.Error.WriteLine("      Join a community Search Party (seedfinder.app party protocol).");
            Console.Error.WriteLine("  MotelyWorker --pool <helper-url>");
            Console.Error.WriteLine("      Claim blocks from a self-hosted Motely.HelperAPI pool.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --threads <N>        Search threads per claimed block (default: all cores)");
            Console.Error.WriteLine("  --worker-id <id>     Worker identifier (pool mode only, optional)");
            Console.Error.WriteLine("  --filter <filterId>  Only claim blocks for this filter (pool mode only)");
            Console.Error.WriteLine("  --local-db <dir>     Seed lake data root (default: Seeds)");
            Console.Error.WriteLine("                       Use '-' to disable local saving");
            return 1;
        }

        workerId ??= $"{Environment.MachineName}-{Environment.ProcessId}";
        if (localDbDir == "-") localDbDir = null;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        return partyId != null
            ? await RunPartyMode(serverUrl, partyId, threads, localDbDir, cts)
            : await RunPoolMode(url!, workerId, threads, filterId, localDbDir, cts);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PARTY MODE — community Search Party (seedfinder.app protocol)
    //  Lease a block range, grind it, heartbeat while grinding, report
    //  candidate seeds (the server re-verifies and scores them itself).
    // ═══════════════════════════════════════════════════════════════════
    static async Task<int> RunPartyMode(string serverUrl, string partyId, int threads, string? localDbDir, CancellationTokenSource cts)
    {
        using var party = new PartyClient(serverUrl);

        Console.Error.WriteLine($"[MotelyWorker] Party {partyId} @ {serverUrl} | Threads: {threads}");
        if (localDbDir != null)
            Console.Error.WriteLine($"[MotelyWorker] Local seed lake: {Path.GetFullPath(localDbDir)}");
        Console.Error.WriteLine();

        long totalSeedsSearched = 0;
        long totalConfirmed = 0;
        int leasesCompleted = 0;
        var startTime = DateTime.UtcNow;

        while (!cts.Token.IsCancellationRequested)
        {
            PartyLeaseEnvelopeDto env;
            try { env = await party.LeaseNextAsync(partyId, cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MotelyWorker] Lease failed: {ex.Message}. Retrying in 30s...");
                try { await Task.Delay(30_000, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            if (env.Done || env.Lease is null)
            {
                Console.Error.WriteLine($"[MotelyWorker] Party settled: {env.Reason ?? env.Error ?? "no work remaining"}");
                break;
            }
            var lease = env.Lease;
            Console.WriteLine(
                $"[MotelyWorker] Lease: blocks [{lease.StartBlock}, {lease.StartBlock + lease.BlockCount}) " +
                $"of {lease.TotalBlocks} | batchChars={lease.BatchChars}");

            if (!JamlConfigLoader.TryLoad(lease.Jaml, out var config, out var parseError) || config is null)
            {
                // The party's spec doesn't parse on this worker build — retrying
                // the same JAML forever helps nobody. Stop loudly instead.
                Console.Error.WriteLine($"[MotelyWorker] JAML parse error: {parseError}");
                return 1;
            }

            // Party blocks ARE engine batch indices at the lease's batchChars —
            // the lease maps 1:1 onto the [start, end) batch range (end exclusive).
            // Collection is capped AT the callback: the report accepts 1000 seeds
            // and the server records at most 500, so hoarding every match from a
            // permissive filter would only trade memory for nothing.
            const int ReportCap = 1000;
            var seeds = new List<string>(capacity: 256);
            long matchesFound = 0;
            var plan = JamlSearchBuilder.CreatePlan(config);
            // Every find also lands in the local seed lake under the party JAML's own id — the report
            // cap above only limits what crosses the wire, never what this machine keeps.
            using var lake = localDbDir is null ? null
                : new SeedLakeSink(localDbDir, config.Id, plan.ScoreTallyColumnCount > 0 ? plan.TallyLabels : null);
            var settings = plan.Settings
                .WithDeck(config.Deck)
                .WithStake(config.Stake)
                .WithThreadCount(threads)
                .WithBatchCharacterCount(lease.BatchChars)
                .WithStartBatchIndex(lease.StartBlock)
                .WithEndBatchIndex(lease.StartBlock + lease.BlockCount)
                .WithSequentialSearch();
            if (plan.ScoreTallyColumnCount > 0)
                settings = settings.WithScoredResultCallback(t =>
                {
                    lake?.OnScored(in t);
                    lock (seeds) { matchesFound++; if (seeds.Count < ReportCap) seeds.Add(t.Seed); }
                });
            else
                settings = settings.WithSeedMatchCallback(line =>
                {
                    int comma = line.IndexOf(',');
                    var seed = comma < 0 ? line : line[..comma];
                    lake?.OnSeed(seed);
                    lock (seeds) { matchesFound++; if (seeds.Count < ReportCap) seeds.Add(seed); }
                });

            long seedsSearched = 0;
            using (var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                // The server-side lease TTL is 60s; a 20s heartbeat keeps the
                // lease alive for however long the grind actually takes.
                var heartbeat = HeartbeatLoop(party, lease, heartbeatCts.Token);
                try
                {
                    using var search = settings.Start(cts.Token);
                    await search.WaitForCompletionAsync(cts.Token);
                    seedsSearched = search.TotalSeedsSearched;
                }
                catch (OperationCanceledException) { break; }
                finally
                {
                    heartbeatCts.Cancel();
                    try { await heartbeat; } catch { /* heartbeats are best-effort */ }
                }
            }

            string[] reportSeeds;
            long found;
            lock (seeds) { found = matchesFound; reportSeeds = seeds.Distinct().ToArray(); }
            if (found > reportSeeds.Length)
                Console.Error.WriteLine(
                    $"[MotelyWorker] {found} matches trimmed to the report cap of {ReportCap} " +
                    "(the server records at most 500 confirmed finds per party).");

            try
            {
                // An empty seeds array still completes the lease — always report.
                var result = await party.ReportAsync(new PartyReportRequestDto
                {
                    PartyId = lease.PartyId,
                    WorkerToken = lease.WorkerToken,
                    StartBlock = lease.StartBlock,
                    Seeds = reportSeeds,
                }, cts.Token);
                totalConfirmed += result.Confirmed;
                Console.WriteLine(
                    $"[MotelyWorker] Reported {reportSeeds.Length} seeds → " +
                    $"{result.Confirmed} confirmed, {result.Rejected} rejected.");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // The server will expire and re-lease this range; counting it as
                // completed here would make the summary lie about delivered work.
                Console.Error.WriteLine(
                    $"[MotelyWorker] Report failed: {ex.Message} — the lease will expire and be re-leased.");
                continue;
            }

            totalSeedsSearched += seedsSearched;
            leasesCompleted++;

            var elapsed = DateTime.UtcNow - startTime;
            double speed = elapsed.TotalSeconds > 0 ? totalSeedsSearched / elapsed.TotalSeconds : 0;
            Console.Error.Write(
                $"\r[MotelyWorker] Leases: {leasesCompleted} | Seeds: {totalSeedsSearched:N0} | " +
                $"Confirmed: {totalConfirmed} | {speed:N0} seeds/s  ");
        }

        PrintSummary($"party:{partyId}", leasesCompleted, totalSeedsSearched, totalConfirmed, startTime);
        return cts.Token.IsCancellationRequested ? 1 : 0;
    }

    /// <summary>Extend the current lease every 20s until cancelled. Best-effort: a missed
    /// beat only risks the lease expiring and being re-leased to another worker.</summary>
    static async Task HeartbeatLoop(PartyClient party, PartyLeaseDto lease, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(20_000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try
            {
                await party.ReportAsync(new PartyReportRequestDto
                {
                    PartyId = lease.PartyId,
                    WorkerToken = lease.WorkerToken,
                    StartBlock = lease.StartBlock,
                    HeartbeatOnly = true,
                }, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MotelyWorker] Heartbeat failed: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  POOL MODE — claim one block at a time from the help-wanted queue
    // ═══════════════════════════════════════════════════════════════════
    static async Task<int> RunPoolMode(
        string poolUrl, string workerId, int threads,
        string? targetFilterId, string? localDbDir,
        CancellationTokenSource cts)
    {
        using var pool = new PoolClient(poolUrl);

        Console.Error.WriteLine($"[MotelyWorker] → {poolUrl}");
        Console.Error.WriteLine($"[MotelyWorker] Worker: {workerId} | Threads: {threads}");
        if (targetFilterId != null)
            Console.Error.WriteLine($"[MotelyWorker] Targeting filter: {targetFilterId}");
        if (localDbDir != null)
            Console.Error.WriteLine($"[MotelyWorker] Local seed lake: {Path.GetFullPath(localDbDir)}");
        Console.Error.WriteLine("[MotelyWorker] Waiting for work...");
        Console.Error.WriteLine();

        long totalSeedsSearched = 0;
        long totalMatches = 0;
        int chunksCompleted = 0;
        var startTime = DateTime.UtcNow;

        while (!cts.Token.IsCancellationRequested)
        {
                // ── CLAIM from pool ──────────────────────────────────────
                PoolClaimResponseDto claim;
                try
                {
                    claim = await pool.ClaimAsync(workerId, targetFilterId, cts.Token);
                    if (!claim.Idle)
                        Console.WriteLine(
                            $"[MotelyWorker] Claim: filter={claim.FilterId} | block={claim.BatchIndex} | batchCharCount={claim.BatchCharCount}"
                        );
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MotelyWorker] Pool claim failed: {ex.Message}. Retrying in 60s...");
                    try { await Task.Delay(60000, cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                if (claim.Idle)
                {
                    Console.Error.Write("\r[MotelyWorker] Idle — no work available. Waiting...          ");
                    try { await Task.Delay(claim.RetryAfterMs, cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                if (string.IsNullOrEmpty(claim.Jaml) || string.IsNullOrEmpty(claim.FilterId))
                {
                    Console.Error.WriteLine("[MotelyWorker] Invalid pool claim response. Retrying...");
                    await Task.Delay(2000, cts.Token).ConfigureAwait(false);
                    continue;
                }

                // ── PARSE JAML ───────────────────────────────────────────
                if (!JamlConfigLoader.TryLoad(claim.Jaml, out var config, out var parseError) || config is null)
                {
                    Console.Error.WriteLine($"[MotelyWorker] JAML parse error: {parseError}");
                    await Task.Delay(2000, cts.Token).ConfigureAwait(false);
                    continue;
                }

                // ── SEARCH ───────────────────────────────────────────────
                var matchResults = new ConcurrentBag<SeedResultDto>();
                long seedsSearched = 0;
                long endBatchExclusive = claim.BatchIndex + Math.Max(1, claim.Remaining);

                var plan = JamlSearchBuilder.CreatePlan(config);
                var settings = plan.Settings
                    .WithDeck(config.Deck)
                    .WithStake(config.Stake)
                    .WithThreadCount(threads)
                    .WithBatchCharacterCount(claim.BatchCharCount)
                    .WithStartBatchIndex(claim.BatchIndex)
                    .WithEndBatchIndex(endBatchExclusive)
                    .WithSequentialSearch();

                // ── LOCAL SEED LAKE ──────────────────────────────────────
                // Every find streams into the shared lake under the pool's filter id as it is
                // found, so it is on disk whether or not the submit below ever succeeds.
                using var lake = localDbDir is null ? null
                    : new SeedLakeSink(localDbDir, claim.FilterId, plan.ScoreTallyColumnCount > 0 ? plan.TallyLabels : null);

                // Take the score off the typed tally rather than re-parsing it back out of the
                // formatted "seed,score,..." line — the text form drops the tally columns.
                if (plan.ScoreTallyColumnCount > 0)
                    settings = settings.WithScoredResultCallback(tally =>
                    {
                        lake?.OnScored(in tally);
                        matchResults.Add(new SeedResultDto { Seed = tally.Seed, Score = tally.Score });
                    });
                else
                    settings = settings.WithSeedMatchCallback(seed =>
                    {
                        lake?.OnSeed(seed);
                        matchResults.Add(new SeedResultDto { Seed = seed });
                    });

                try
                {
                    Console.WriteLine($"[MotelyWorker] Searching filter: {claim.FilterId}");
                    using var search = settings.Start(cts.Token);
                    await search.WaitForCompletionAsync(cts.Token);
                    seedsSearched = search.TotalSeedsSearched;
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("\n[MotelyWorker] Search cancelled.");
                    break;
                }

                totalSeedsSearched += seedsSearched;
                totalMatches += matchResults.Count;

                var results = matchResults.ToArray();

                // ── SUBMIT TO POOL ───────────────────────────────────────
                var submitBody = new SubmitResultsDto
                {
                    StartBatch = claim.BatchIndex,
                    EndBatch = endBatchExclusive,
                    Results = results,
                    SeedsSearched = seedsSearched,
                };

                try
                {
                    await pool.SubmitResultsAsync(claim.FilterId!, submitBody, cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"\n[MotelyWorker] Submit failed: {ex.Message}. Retrying...");
                    try
                    {
                        await Task.Delay(2000, cts.Token);
                        await pool.SubmitResultsAsync(claim.FilterId!, submitBody, cts.Token);
                    }
                    catch
                    {
                        Console.Error.WriteLine("[MotelyWorker] Submit retry failed. Results are saved locally but not submitted to pool.");
                    }
                }

                chunksCompleted++;

                var elapsed = DateTime.UtcNow - startTime;
                double speed = elapsed.TotalSeconds > 0 ? totalSeedsSearched / elapsed.TotalSeconds : 0;
                Console.Error.Write(
                    $"\r[MotelyWorker] Filter:{claim.FilterId} | Block:{claim.BatchIndex} | Seeds: {totalSeedsSearched:N0} | Matches: {totalMatches} | {speed:N0} seeds/s  "
                );
            }

        PrintSummary(workerId, chunksCompleted, totalSeedsSearched, totalMatches, startTime);
        return cts.Token.IsCancellationRequested ? 1 : 0;
    }

    static void PrintSummary(string workerId, int chunksCompleted, long totalSeedsSearched, long totalMatches, DateTime startTime)
    {
        var totalElapsed = DateTime.UtcNow - startTime;
        double finalSpeed = totalElapsed.TotalSeconds > 0 ? totalSeedsSearched / totalElapsed.TotalSeconds : 0;

        Console.Error.WriteLine();
        Console.Error.WriteLine();
        Console.Error.WriteLine("═══════════════════════════════════════════");
        Console.Error.WriteLine($"  Worker:   {workerId}");
        Console.Error.WriteLine($"  Chunks:   {chunksCompleted}");
        Console.Error.WriteLine($"  Seeds:    {totalSeedsSearched:N0}");
        Console.Error.WriteLine($"  Matches:  {totalMatches}");
        Console.Error.WriteLine($"  Time:     {totalElapsed:hh\\:mm\\:ss}");
        Console.Error.WriteLine($"  Speed:    {finalSpeed:N0} seeds/sec");
        Console.Error.WriteLine("═══════════════════════════════════════════");
    }
}
