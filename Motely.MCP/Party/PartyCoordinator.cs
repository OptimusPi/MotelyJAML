using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Motely.Enums;
using Motely.Filters;
using Motely.Gpu;
using Motely.MCP.Data;

namespace Motely.MCP.Party;

public sealed record CreatePartyRequest(
    string? FilterOrJaml,
    string? Name = null,
    string? Deck = null,
    string? Stake = null,
    int BatchChars = 3,
    long BlockSize = 10,
    long? TotalBlocks = null,
    int? TargetMatches = null
);

public sealed record PartyLeaseDto(
    [property: JsonPropertyName("partyId")] string PartyId,
    [property: JsonPropertyName("jaml")] string Jaml,
    [property: JsonPropertyName("deck")] string? Deck,
    [property: JsonPropertyName("stake")] string? Stake,
    [property: JsonPropertyName("batchChars")] int BatchChars,
    [property: JsonPropertyName("startBlock")] long StartBlock,
    [property: JsonPropertyName("blockCount")] long BlockCount,
    [property: JsonPropertyName("totalBlocks")] long TotalBlocks,
    [property: JsonPropertyName("workerToken")] string WorkerToken,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt
);

public sealed record PartyNextResponseDto(
    [property: JsonPropertyName("lease")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    PartyLeaseDto? Lease = null,
    [property: JsonPropertyName("done")] bool Done = false,
    [property: JsonPropertyName("reason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Reason = null,
    [property: JsonPropertyName("error")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Error = null
);

public sealed record PartyReportRequestDto(
    [property: JsonPropertyName("partyId")] string PartyId,
    [property: JsonPropertyName("workerToken")] string WorkerToken,
    [property: JsonPropertyName("startBlock")] long StartBlock,
    [property: JsonPropertyName("seeds")] IReadOnlyList<string>? Seeds = null,
    [property: JsonPropertyName("heartbeatOnly")] bool HeartbeatOnly = false
);

public sealed record PartyReportResponseDto(
    [property: JsonPropertyName("confirmed")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Confirmed = null,
    [property: JsonPropertyName("rejected")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Rejected = null,
    [property: JsonPropertyName("recorded")] int Recorded = 0,
    [property: JsonPropertyName("ok")] bool Ok = true,
    [property: JsonPropertyName("error")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Error = null
);

public sealed record PartyStatusDto(
    string PartyId,
    string Name,
    string Status,
    string? Deck,
    string? Stake,
    int BatchChars,
    long BlockSize,
    long TotalBlocks,
    long CompletedBlocks,
    int ActiveLeases,
    double PercentComplete,
    int? TargetMatches,
    int MatchesFound,
    IReadOnlyList<DiscoveredSeedRecord> TopFinds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public sealed record PartyGpuSweepResultDto(
    string PartyId,
    string Status,
    long StartBlock,
    long BlockCount,
    long SeedsSearched,
    int Matched,
    int Confirmed,
    int Recorded,
    bool GpuUsed,
    string? DeviceName,
    double ElapsedMs,
    double SeedsPerSecond,
    IReadOnlyList<string> ConfirmedSeeds
);

/// <summary>
/// Coordinates distributed and GPU-accelerated Community Search Party runs.
/// Compatible with <c>motely party</c> CLI workers (<c>/api/party/next</c> and <c>/api/party/report</c>).
/// </summary>
public sealed class PartyCoordinator
{
    public static readonly TimeSpan DefaultLeaseTtl = TimeSpan.FromSeconds(60);

    private readonly IMotelyStore _store;
    private readonly string _filtersDir;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, PartyRuntimeState> _parties = new(StringComparer.Ordinal);
    private bool _loaded;

    public TimeSpan LeaseTtl { get; set; } = DefaultLeaseTtl;

    public PartyCoordinator(IMotelyStore store, string filtersDir)
    {
        _store = store;
        _filtersDir = filtersDir;
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
                return;

            await _store.InitializeAsync(_filtersDir, cancellationToken).ConfigureAwait(false);
            var savedParties = await _store.GetPartiesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var p in savedParties)
            {
                _parties[p.PartyId] = new PartyRuntimeState(p);
            }
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PartyStatusDto> CreatePartyAsync(
        CreatePartyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.FilterOrJaml))
            throw new ArgumentException("Filter name or inline JAML is required.", nameof(request));

        var (config, jamlText, displayName) = await ResolveFilterAsync(
            request.FilterOrJaml,
            cancellationToken
        ).ConfigureAwait(false);

        // Validate that the JAML builds a valid search plan
        _ = MotelySearchBuilder.CreatePlan(config, int.MinValue);

        int batchChars = request.BatchChars is >= 1 and < MotelyGlobals.MaxSeedLength
            ? request.BatchChars
            : 3;
        long maxKeyspaceBlocks = MotelyGlobals.SequentialBatchCount(batchChars);
        long totalBlocks = request.TotalBlocks is > 0
            ? Math.Min(request.TotalBlocks.Value, maxKeyspaceBlocks)
            : maxKeyspaceBlocks;
        long blockSize = Math.Clamp(request.BlockSize > 0 ? request.BlockSize : 10, 1, totalBlocks);

        string? deckOverride = null;
        if (!string.IsNullOrWhiteSpace(request.Deck))
        {
            var d = ParseEnum<MotelyDeck>(request.Deck);
            deckOverride = d.ToString();
        }

        string? stakeOverride = null;
        if (!string.IsNullOrWhiteSpace(request.Stake))
        {
            var s = ParseEnum<MotelyStake>(request.Stake);
            stakeOverride = s.ToString();
        }

        string partyName = !string.IsNullOrWhiteSpace(request.Name)
            ? request.Name.Trim()
            : (!string.IsNullOrWhiteSpace(config.Name) ? config.Name : displayName);

        string slugBase = SanitizeId(partyName);
        string partyId = $"{slugBase}-{Guid.NewGuid().ToString("N")[..6]}";
        var now = DateTimeOffset.UtcNow;

        var record = new PartyRecord(
            PartyId: partyId,
            Name: partyName,
            Jaml: jamlText,
            Deck: deckOverride ?? config.Deck.ToString(),
            Stake: stakeOverride ?? config.Stake.ToString(),
            BatchChars: batchChars,
            BlockSize: blockSize,
            TotalBlocks: totalBlocks,
            NextUnassignedBlock: 0,
            CompletedBlocks: 0,
            TargetMatches: request.TargetMatches is > 0 ? request.TargetMatches : null,
            MatchesFound: 0,
            Status: "active",
            CompletedRanges: [],
            CreatedAtUtc: now,
            UpdatedAtUtc: now
        );

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            record = await _store.SavePartyAsync(record, cancellationToken).ConfigureAwait(false);
            _parties[partyId] = new PartyRuntimeState(record);
        }
        finally
        {
            _gate.Release();
        }

        return await GetPartyStatusAsync(partyId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Failed to retrieve created party.");
    }

    public async Task<IReadOnlyList<PartyStatusDto>> ListPartiesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        List<string> ids;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ids =
            [
                .. _parties
                    .Values.OrderByDescending(p => p.Record.CreatedAtUtc)
                    .Select(p => p.Record.PartyId),
            ];
        }
        finally
        {
            _gate.Release();
        }

        var result = new List<PartyStatusDto>(ids.Count);
        foreach (string id in ids)
        {
            if (await GetPartyStatusAsync(id, cancellationToken).ConfigureAwait(false) is { } status)
                result.Add(status);
        }
        return result;
    }

    public async Task<PartyStatusDto?> GetPartyStatusAsync(
        string partyId,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        PartyRecord record;
        int activeLeases;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_parties.TryGetValue(partyId, out var state))
                return null;

            if ( ReclaimExpiredLeasesUnlocked(state, DateTimeOffset.UtcNow) )
            {
                state.SyncToRecord();
                state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
            }
            record = state.Record;
            activeLeases = state.ActiveLeases.Count;
        }
        finally
        {
            _gate.Release();
        }

        var topFinds = await _store
            .GetLeaderboardAsync(filterSlug: $"party:{partyId}", limit: 25, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        double pct = record.TotalBlocks > 0
            ? Math.Round((double)record.CompletedBlocks / record.TotalBlocks * 100.0, 2)
            : 100.0;

        return new PartyStatusDto(
            PartyId: record.PartyId,
            Name: record.Name,
            Status: record.Status,
            Deck: record.Deck,
            Stake: record.Stake,
            BatchChars: record.BatchChars,
            BlockSize: record.BlockSize,
            TotalBlocks: record.TotalBlocks,
            CompletedBlocks: record.CompletedBlocks,
            ActiveLeases: activeLeases,
            PercentComplete: pct,
            TargetMatches: record.TargetMatches,
            MatchesFound: record.MatchesFound,
            TopFinds: topFinds,
            CreatedAtUtc: record.CreatedAtUtc,
            UpdatedAtUtc: record.UpdatedAtUtc
        );
    }

    /// <summary>
    /// Leases the next available block range for <paramref name="partyId"/> (`GET /api/party/next?partyId=...`).
    /// </summary>
    public async Task<PartyNextResponseDto> NextLeaseAsync(
        string? partyId,
        long? requestedBlocks = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(partyId))
            return new PartyNextResponseDto(Done: true, Reason: "unknown party");

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_parties.TryGetValue(partyId, out var state))
                return new PartyNextResponseDto(Done: true, Reason: "unknown party");

            var now = DateTimeOffset.UtcNow;
            bool reclaimedAny = ReclaimExpiredLeasesUnlocked(state, now);

            if (state.Record.Status != "active")
            {
                if (reclaimedAny)
                {
                    state.SyncToRecord();
                    state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
                }
                return new PartyNextResponseDto(Done: true, Reason: state.Record.Status);
            }

            if (state.Record.TargetMatches is { } target && state.Record.MatchesFound >= target)
            {
                state.Record = state.Record with { Status = "complete" };
                state.SyncToRecord();
                state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
                return new PartyNextResponseDto(Done: true, Reason: "complete");
            }

            if (state.Record.CompletedBlocks >= state.Record.TotalBlocks)
            {
                state.Record = state.Record with { Status = "exhausted" };
                state.SyncToRecord();
                state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
                return new PartyNextResponseDto(Done: true, Reason: "exhausted");
            }

            long startBlock;
            long blockCount;

            if (state.ReclaimedRanges.Count > 0)
            {
                var reclaimed = state.ReclaimedRanges.Dequeue();
                startBlock = reclaimed.StartBlock;
                blockCount = reclaimed.BlockCount;
            }
            else if (state.Record.NextUnassignedBlock < state.Record.TotalBlocks)
            {
                startBlock = state.Record.NextUnassignedBlock;
                long size = requestedBlocks is > 0 ? requestedBlocks.Value : state.Record.BlockSize;
                blockCount = Math.Min(size, state.Record.TotalBlocks - startBlock);
                state.Record = state.Record with
                {
                    NextUnassignedBlock = startBlock + blockCount,
                };
            }
            else
            {
                if (reclaimedAny)
                {
                    state.SyncToRecord();
                    state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
                }
                return new PartyNextResponseDto(Done: true, Reason: "fully leased");
            }

            string token = $"wrk-{Guid.NewGuid():N}"[..16];
            var expiresAt = now.Add(LeaseTtl);
            state.ActiveLeases[token] = new ActiveLease(token, startBlock, blockCount, expiresAt, now);
            state.SyncToRecord();
            state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);

            return new PartyNextResponseDto(
                Lease: new PartyLeaseDto(
                    PartyId: state.Record.PartyId,
                    Jaml: state.Record.Jaml,
                    Deck: state.Record.Deck,
                    Stake: state.Record.Stake,
                    BatchChars: state.Record.BatchChars,
                    StartBlock: startBlock,
                    BlockCount: blockCount,
                    TotalBlocks: state.Record.TotalBlocks,
                    WorkerToken: token,
                    ExpiresAt: expiresAt.UtcDateTime.ToString("O")
                ),
                Done: false
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Handles a worker heartbeat or final block report (`POST /api/party/report`).
    /// Re-verifies every candidate seed on the server and records confirmed hits to <see cref="IMotelyStore"/>.
    /// </summary>
    public async Task<(int StatusCode, PartyReportResponseDto Body)> ReportAsync(
        PartyReportRequestDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.PartyId) || string.IsNullOrWhiteSpace(request.WorkerToken))
        {
            return (400, new PartyReportResponseDto(Ok: false, Error: "partyId and workerToken are required"));
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        PartyRecord partySnapshot;
        ActiveLease lease;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_parties.TryGetValue(request.PartyId, out var state))
                return (404, new PartyReportResponseDto(Ok: false, Error: "unknown party"));

            var now = DateTimeOffset.UtcNow;
            bool reclaimedAny = ReclaimExpiredLeasesUnlocked(state, now);

            if (
                !state.ActiveLeases.TryGetValue(request.WorkerToken, out var active)
                || active.StartBlock != request.StartBlock
            )
            {
                if (reclaimedAny)
                {
                    state.SyncToRecord();
                    state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
                }

                // PartyClient.HeartbeatAsync expects 200 OK with {"ok": false} when the lease expired
                int missingStatus = request.HeartbeatOnly ? 200 : 400;
                return (
                    missingStatus,
                    new PartyReportResponseDto(
                        Ok: false,
                        Error: "no matching lease for this worker token and block"
                    )
                );
            }

            if (request.HeartbeatOnly)
            {
                state.ActiveLeases[request.WorkerToken] = active with
                {
                    ExpiresAt = now.Add(LeaseTtl),
                    LastHeartbeat = now,
                };
                state.SyncToRecord();
                state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
                return (200, new PartyReportResponseDto(Ok: true));
            }

            state.ActiveLeases.Remove(request.WorkerToken);
            partySnapshot = state.Record;
            lease = active;
        }
        finally
        {
            _gate.Release();
        }

        // Verify candidate seeds against the party's JAML outside the lock
        var candidateSeeds = (request.Seeds ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(1000)
            .ToArray();

        var (confirmed, rejected, scoredRecords) = VerifyCandidateSeeds(
            partySnapshot,
            lease,
            candidateSeeds,
            request.WorkerToken
        );

        int recorded = 0;
        if (scoredRecords.Count > 0)
        {
            recorded = await _store.RecordSeedsAsync(scoredRecords, cancellationToken).ConfigureAwait(false);
            // Also record under the filter's own slug so global leaderboards include party finds
            var globalRecords = scoredRecords.Select(r => r with { FilterSlug = SanitizeId(partySnapshot.Name) });
            await _store.RecordSeedsAsync(globalRecords, cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_parties.TryGetValue(request.PartyId, out var state))
            {
                var ranges = state.Record.CompletedRanges.ToList();
                ranges.Add(new BlockRangeRecord(lease.StartBlock, lease.BlockCount));

                long completedBlocks = Math.Min(
                    state.Record.TotalBlocks,
                    state.Record.CompletedBlocks + lease.BlockCount
                );
                int matchesFound = state.Record.MatchesFound + confirmed.Count;

                string status = state.Record.Status;
                if (status == "active")
                {
                    if (state.Record.TargetMatches is { } target && matchesFound >= target)
                        status = "complete";
                    else if (completedBlocks >= state.Record.TotalBlocks && state.ActiveLeases.Count == 0 && state.ReclaimedRanges.Count == 0)
                        status = "exhausted";
                }

                state.Record = state.Record with
                {
                    CompletedBlocks = completedBlocks,
                    MatchesFound = matchesFound,
                    Status = status,
                    CompletedRanges = ranges,
                };
                state.SyncToRecord();
                state.Record = await _store.SavePartyAsync(state.Record, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        return (
            200,
            new PartyReportResponseDto(
                Confirmed: confirmed,
                Rejected: rejected,
                Recorded: confirmed.Count,
                Ok: true
            )
        );
    }

    /// <summary>
    /// Leases and immediately sweeps <paramref name="blockCount"/> blocks for <paramref name="partyId"/>
    /// using the host GPU (NVIDIA GeForce RTX 4070 SUPER) when <paramref name="useGpu"/> is true,
    /// then reports and records all discovered seeds.
    /// </summary>
    public async Task<PartyGpuSweepResultDto> SweepBlocksAsync(
        string partyId,
        long? blockCount = null,
        bool useGpu = true,
        int? threads = null,
        CancellationToken cancellationToken = default
    )
    {
        var next = await NextLeaseAsync(partyId, blockCount, cancellationToken).ConfigureAwait(false);
        if (next.Lease is not { } lease)
        {
            return new PartyGpuSweepResultDto(
                PartyId: partyId,
                Status: next.Reason ?? "done",
                StartBlock: 0,
                BlockCount: 0,
                SeedsSearched: 0,
                Matched: 0,
                Confirmed: 0,
                Recorded: 0,
                GpuUsed: false,
                DeviceName: null,
                ElapsedMs: 0,
                SeedsPerSecond: 0,
                ConfirmedSeeds: []
            );
        }

        var config = JamlConfigLoader.FromJaml(lease.Jaml);
        var plan = MotelySearchBuilder.CreatePlan(config, int.MinValue);
        var settings = plan.Settings.WithSourceConfig(config);

        var deck = !string.IsNullOrWhiteSpace(lease.Deck)
            ? ParseEnum<MotelyDeck>(lease.Deck)
            : config.Deck;
        var stake = !string.IsNullOrWhiteSpace(lease.Stake)
            ? ParseEnum<MotelyStake>(lease.Stake)
            : config.Stake;

        settings = settings.WithDeck(deck).WithStake(stake);

        var gpuDevice = useGpu ? GpuDeviceInfo.GetBestDevice() : null;
        bool gpuActive = useGpu && gpuDevice is { SupportsFp64: true } && GpuFilterProgram.Compile(config).HasSelectiveOps;

        using var telemetry = SearchTelemetry.Start(partyId, deck, stake, "party");

        settings = settings
            .WithThreadCount(Math.Max(1, threads ?? Environment.ProcessorCount))
            .WithBatchCharacterCount(lease.BatchChars)
            .WithSequentialSearch()
            .WithStartBatchIndex(lease.StartBlock)
            .WithEndBatchIndex(lease.StartBlock + lease.BlockCount)
            .WithAutoScoreCutoff(false)
            .WithGpuSearch(gpuActive)
            .WithQuietMode(true)
            .WithProgressCallback(telemetry.Report);

        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string seed, int score)
        {
            lock (found)
            {
                if (!found.TryGetValue(seed, out int old) || score > old)
                    found[seed] = score;
            }
        }

        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(r => Add(r.Seed, r.Score))
            : settings.WithSeedMatchCallback(line =>
            {
                int comma = line.IndexOf(',');
                Add(comma < 0 ? line : line[..comma], 0);
            });

        var sw = Stopwatch.StartNew();
        using var search = settings.Start(cancellationToken);
        await search.WaitForCompletionAsync(cancellationToken).ConfigureAwait(false);
        telemetry.Finish(search, "finished");
        sw.Stop();

        string[] bestSeeds;
        int totalMatched;
        lock (found)
        {
            totalMatched = found.Count;
            bestSeeds =
            [
                .. found
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Take(1000)
                    .Select(kv => kv.Key),
            ];
        }

        var (_, reportRes) = await ReportAsync(
            new PartyReportRequestDto(
                PartyId: partyId,
                WorkerToken: lease.WorkerToken,
                StartBlock: lease.StartBlock,
                Seeds: bestSeeds,
                HeartbeatOnly: false
            ),
            cancellationToken
        ).ConfigureAwait(false);

        var status = await GetPartyStatusAsync(partyId, cancellationToken).ConfigureAwait(false);
        double elapsedMs = Math.Max(0.001, sw.Elapsed.TotalMilliseconds);
        double sps = search.TotalSeedsSearched / (elapsedMs / 1000.0);

        return new PartyGpuSweepResultDto(
            PartyId: partyId,
            Status: status?.Status ?? "active",
            StartBlock: lease.StartBlock,
            BlockCount: lease.BlockCount,
            SeedsSearched: search.TotalSeedsSearched,
            Matched: totalMatched,
            Confirmed: reportRes.Confirmed?.Count ?? 0,
            Recorded: reportRes.Recorded,
            GpuUsed: gpuActive,
            DeviceName: gpuActive ? gpuDevice?.Name : null,
            ElapsedMs: Math.Round(elapsedMs, 2),
            SeedsPerSecond: Math.Round(sps, 0),
            ConfirmedSeeds: reportRes.Confirmed ?? []
        );
    }

    private static (
        IReadOnlyList<string> Confirmed,
        IReadOnlyList<string> Rejected,
        IReadOnlyList<DiscoveredSeedRecord> Records
    ) VerifyCandidateSeeds(
        PartyRecord party,
        ActiveLease lease,
        string[] candidateSeeds,
        string workerToken
    )
    {
        if (candidateSeeds.Length == 0)
            return ([], [], []);

        // Validate that each candidate seed actually belongs to [lease.StartBlock, lease.StartBlock + lease.BlockCount)
        long endBlock = lease.StartBlock + lease.BlockCount;
        var validBlockSeeds = new List<string>(candidateSeeds.Length);
        var rejected = new List<string>();

        foreach (string seed in candidateSeeds)
        {
            if (TryGetSeedBlockIndex(seed, party.BatchChars, out long blockIdx)
                && blockIdx >= lease.StartBlock
                && blockIdx < endBlock)
            {
                validBlockSeeds.Add(seed);
            }
            else
            {
                rejected.Add(seed);
            }
        }

        if (validBlockSeeds.Count == 0)
            return ([], rejected, []);

        var config = JamlConfigLoader.FromJaml(party.Jaml);
        var plan = MotelySearchBuilder.CreatePlan(config, int.MinValue);
        var settings = plan.Settings;

        var deck = !string.IsNullOrWhiteSpace(party.Deck)
            ? ParseEnum<MotelyDeck>(party.Deck)
            : config.Deck;
        var stake = !string.IsNullOrWhiteSpace(party.Stake)
            ? ParseEnum<MotelyStake>(party.Stake)
            : config.Stake;

        settings = settings
            .WithDeck(deck)
            .WithStake(stake)
            .WithThreadCount(1)
            .WithSeedList([.. validBlockSeeds])
            .WithAutoScoreCutoff(false)
            .WithQuietMode(true);

        var matchedScores = new ConcurrentDictionary<string, (int Score, int[] Tallies)>(StringComparer.Ordinal);
        int tallyCols = plan.ScoreTallyColumnCount;

        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(r =>
            {
                var tallies = new int[tallyCols];
                for (int i = 0; i < tallyCols; i++)
                    tallies[i] = r.Tally[i];
                matchedScores[r.Seed] = (r.Score, tallies);
            })
            : settings.WithSeedMatchCallback(line =>
            {
                int comma = line.IndexOf(',');
                string s = comma < 0 ? line : line[..comma];
                matchedScores[s] = (0, []);
            });

        using var search = settings.Start();
        search.AwaitCompletion();

        var confirmed = new List<string>();
        var records = new List<DiscoveredSeedRecord>();
        var now = DateTimeOffset.UtcNow;

        foreach (string seed in validBlockSeeds)
        {
            if (matchedScores.TryGetValue(seed, out var hit))
            {
                confirmed.Add(seed);
                records.Add(
                    new DiscoveredSeedRecord(
                        Seed: seed,
                        FilterSlug: $"party:{party.PartyId}",
                        FilterName: party.Name,
                        Score: hit.Score,
                        Tallies: hit.Tallies,
                        Deck: deck.ToString(),
                        Stake: stake.ToString(),
                        PartyId: party.PartyId,
                        WorkerId: workerToken,
                        FoundAtUtc: now
                    )
                );
            }
            else
            {
                rejected.Add(seed);
            }
        }

        return (confirmed, rejected, records);
    }

    private static bool TryGetSeedBlockIndex(string seed, int batchChars, out long blockIdx)
    {
        blockIdx = 0;
        if (seed.Length != MotelyGlobals.MaxSeedLength)
            return false;

        for (int i = 0; i < MotelyGlobals.MaxSeedLength; i++)
        {
            if (Array.IndexOf(MotelyGlobals.SeedDigits, seed[i]) < 0)
                return false;
        }

        long idx = 0;
        for (int i = MotelyGlobals.MaxSeedLength - 1; i >= batchChars; i--)
        {
            int digit = Array.IndexOf(MotelyGlobals.SeedDigits, seed[i]);
            idx = idx * MotelyGlobals.SeedDigits.Length + digit;
        }
        blockIdx = idx;
        return true;
    }

    private static bool ReclaimExpiredLeasesUnlocked(PartyRuntimeState state, DateTimeOffset now)
    {
        var expired = state
            .ActiveLeases.Where(kv => kv.Value.ExpiresAt <= now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (string token in expired)
        {
            var lease = state.ActiveLeases[token];
            state.ActiveLeases.Remove(token);
            state.ReclaimedRanges.Enqueue(new BlockRangeRecord(lease.StartBlock, lease.BlockCount));
        }

        return expired.Count > 0;
    }

    private async Task<(JamlConfig Config, string JamlText, string DisplayName)> ResolveFilterAsync(
        string filterOrJaml,
        CancellationToken cancellationToken
    )
    {
        string trimmed = filterOrJaml.Trim();

        // Check saved filters in store first if it looks like a bare name
        if (!trimmed.Contains('\n') && !trimmed.Contains(':'))
        {
            string bareName = Path.GetFileNameWithoutExtension(trimmed);
            if (await _store.GetFilterAsync(bareName, cancellationToken).ConfigureAwait(false) is { } saved)
            {
                var cfg = JamlConfigLoader.FromJaml(saved.JamlContent);
                return (cfg, saved.JamlContent, saved.Name);
            }

            foreach (string ext in new[] { ".yaml", ".jaml" })
            {
                string candidate = Path.Combine(_filtersDir, bareName + ext);
                if (File.Exists(candidate))
                {
                    string text = await File.ReadAllTextAsync(candidate, cancellationToken).ConfigureAwait(false);
                    var cfg = JamlConfigLoader.FromJaml(text);
                    return (cfg, text, bareName);
                }
            }
        }

        var inlineCfg = JamlConfigLoader.FromJaml(trimmed);
        return (inlineCfg, trimmed, inlineCfg.Name ?? "inline");
    }

    private static T ParseEnum<T>(string value)
        where T : struct, Enum
    {
        string cleaned = value
            .Trim()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);
        if (Enum.TryParse<T>(cleaned, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        throw new FormatException($"Unknown {typeof(T).Name} '{value}'.");
    }

    private static string SanitizeId(string text)
    {
        var chars = text
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        string slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(slug) ? "party" : slug;
    }

    private sealed record ActiveLease(
        string WorkerToken,
        long StartBlock,
        long BlockCount,
        DateTimeOffset ExpiresAt,
        DateTimeOffset LastHeartbeat
    );

    private sealed class PartyRuntimeState
    {
        public PartyRecord Record { get; set; }
        public Dictionary<string, ActiveLease> ActiveLeases { get; } = new(StringComparer.Ordinal);
        public Queue<BlockRangeRecord> ReclaimedRanges { get; } = new();

        public PartyRuntimeState(PartyRecord record)
        {
            Record = record;
            if (record.ActiveLeases is { Count: > 0 })
            {
                foreach (var l in record.ActiveLeases)
                {
                    ActiveLeases[l.WorkerToken] = new ActiveLease(
                        l.WorkerToken,
                        l.StartBlock,
                        l.BlockCount,
                        l.ExpiresAtUtc,
                        l.LastHeartbeatUtc == default ? record.UpdatedAtUtc : l.LastHeartbeatUtc
                    );
                }
            }
            if (record.ReclaimedRanges is { Count: > 0 })
            {
                foreach (var r in record.ReclaimedRanges)
                {
                    ReclaimedRanges.Enqueue(r);
                }
            }
        }

        public void SyncToRecord()
        {
            Record = Record with
            {
                ActiveLeases =
                [
                    .. ActiveLeases.Values.Select(l => new PartyLeaseRecord(
                        l.WorkerToken,
                        l.StartBlock,
                        l.BlockCount,
                        l.ExpiresAt,
                        l.LastHeartbeat
                    )),
                ],
                ReclaimedRanges = [.. ReclaimedRanges],
            };
        }
    }
}
