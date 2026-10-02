using System.Collections.Concurrent;
using Motely.Party;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// The Search Party worker against an in-memory coordinator and the real engine: a lease is
/// searched exactly like any other JAML search, reported exactly once (empty or not), and the
/// worker stops loudly on a lease or JAML it can never run instead of retrying it forever.
/// </summary>
public class PartyWorkerTests(ITestOutputHelper output)
{
    // The browser smoke filter: 6GR89UQF matches it, most of its neighbours do not.
    private const string PerkeoJaml = """
        name: bench-negative-perkeo
        deck: Red
        stake: White
        must:
          - or:
              - legendaryJoker: Perkeo
                antes: [1]
                edition: Negative
                sources:
                  arcanaPacks: [0, 1, 2]
                  spectralPacks: [0, 1, 2]
              - legendaryJoker: Perkeo
                antes: [2]
                edition: Negative
                sources:
                  arcanaPacks: [0, 1, 2, 3, 4, 5]
                  spectralPacks: [0, 1, 2, 3, 4, 5]
          - smallBlindTag: CharmTag
            antes: [1, 2]
        """;

    // Matches nearly every seed: any joker anywhere in ante 1.
    private const string PermissiveJaml = """
        name: permissive
        deck: Red
        stake: White
        must:
          - joker: []
            antes: [1]
        """;

    private const int BatchChars = 3;

    private static PartyLease Lease(string jaml, long startBlock, long blockCount = 1, int batchChars = BatchChars) =>
        new("party", jaml, batchChars, startBlock, blockCount, MotelyGlobals.SequentialBatchCount(batchChars), $"token-{startBlock}");

    /// <summary>Hands out the queued answers in order, then reports the party complete.</summary>
    private sealed class FakeCoordinator(params object[] answers) : IPartyCoordinator
    {
        private readonly Queue<object> _answers = new(answers);
        public readonly ConcurrentQueue<(PartyLease Lease, string[] Seeds)> Reports = new();
        public int Heartbeats;
        public Func<PartyLease, Exception?> ReportFailure = _ => null;

        public Task<PartyNext> NextAsync(string partyId, CancellationToken cancellationToken) =>
            !_answers.TryDequeue(out var answer) ? Task.FromResult(new PartyNext(null, "complete"))
            : answer is Exception ex ? Task.FromException<PartyNext>(ex)
            : Task.FromResult(new PartyNext((PartyLease)answer));

        public Task HeartbeatAsync(PartyLease lease, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Heartbeats);
            return Task.CompletedTask;
        }

        public Task<PartyReportResult> ReportAsync(PartyLease lease, IReadOnlyList<string> seeds, CancellationToken cancellationToken)
        {
            if (ReportFailure(lease) is { } ex)
                return Task.FromException<PartyReportResult>(ex);
            Reports.Enqueue((lease, [.. seeds]));
            return Task.FromResult(new PartyReportResult(seeds.Count, 0));
        }
    }

    private sealed class CountingSink : IMotelyResultSink
    {
        public int Finds;
        public void OnSeed(string seed) => Interlocked.Increment(ref Finds);
        public void OnScored(in MotelyScoredSeedResult tally) => Interlocked.Increment(ref Finds);
        public void Flush() { }
        public void Dispose() { }
    }

    private MotelyPartyWorker Worker(IPartyCoordinator coordinator, IMotelyResultSink? sink = null) =>
        new(coordinator)
        {
            RetryDelay = TimeSpan.FromMilliseconds(10),
            Configure = s => s.WithThreadCount(4),
            CreateSink = sink is null ? null : _ => sink,
            Log = output.WriteLine,
        };

    [Fact]
    public async Task ALeaseReportsTheSeedsTheEngineFindsInItsBlocks()
    {
        long block = SeedMath.SeedToBatchIndex("6GR89UQF", BatchChars);
        var coordinator = new FakeCoordinator(Lease(PerkeoJaml, block));

        var summary = await Worker(coordinator).RunAsync("party", CancellationToken.None);

        var report = Assert.Single(coordinator.Reports);
        Assert.Contains("6GR89UQF", report.Seeds);
        Assert.Equal(1, summary.Leases);
        Assert.Equal(MotelyGlobals.SeedsPerSequentialBatch(BatchChars), summary.SeedsSearched);
        Assert.Equal("complete", summary.StopReason);
        Assert.Null(summary.Error);
    }

    [Fact]
    public async Task EveryLeaseIsReportedOnceEvenWithNoFinds()
    {
        long block = SeedMath.SeedToBatchIndex("6GR89UQF", BatchChars);
        var coordinator = new FakeCoordinator(Lease(PerkeoJaml, block), Lease(PerkeoJaml, block + 1));

        var summary = await Worker(coordinator).RunAsync("party", CancellationToken.None);

        Assert.Equal(2, summary.Leases);
        Assert.Equal([block, block + 1], coordinator.Reports.Select(r => r.Lease.StartBlock));
        Assert.Empty(coordinator.Reports.Last().Seeds);
    }

    [Fact]
    public async Task AReportCarriesAtMostTheCapOfDistinctSeedsWhileTheSinkSeesEveryFind()
    {
        var sink = new CountingSink();
        var coordinator = new FakeCoordinator(Lease(PermissiveJaml, 0));

        await Worker(coordinator, sink).RunAsync("party", CancellationToken.None);

        var report = Assert.Single(coordinator.Reports);
        Assert.Equal(MotelyPartyWorker.ReportCap, report.Seeds.Length);
        Assert.Equal(report.Seeds.Length, report.Seeds.Distinct().Count());
        Assert.True(sink.Finds > MotelyPartyWorker.ReportCap, $"sink saw {sink.Finds} finds");
    }

    [Fact]
    public async Task AnUnreachableCoordinatorIsRetried()
    {
        long block = SeedMath.SeedToBatchIndex("6GR89UQF", BatchChars);
        var coordinator = new FakeCoordinator(new HttpRequestException("down"), Lease(PerkeoJaml, block));

        var summary = await Worker(coordinator).RunAsync("party", CancellationToken.None);

        Assert.Equal(1, summary.Leases);
        Assert.Single(coordinator.Reports);
    }

    [Fact]
    public async Task AFailedReportLeavesTheLeaseUncountedAndMovesOn()
    {
        long block = SeedMath.SeedToBatchIndex("6GR89UQF", BatchChars);
        var coordinator = new FakeCoordinator(Lease(PerkeoJaml, block), Lease(PerkeoJaml, block + 1))
        {
            ReportFailure = lease => lease.StartBlock == block ? new HttpRequestException("500") : null,
        };

        var summary = await Worker(coordinator).RunAsync("party", CancellationToken.None);

        Assert.Equal(1, summary.Leases);
        Assert.Equal(block + 1, Assert.Single(coordinator.Reports).Lease.StartBlock);
    }

    [Fact]
    public async Task AJamlThisBuildCannotLoadStopsTheWorker()
    {
        var coordinator = new FakeCoordinator(Lease("name: x\nmust:\n  - joker: NotAJoker\n", 0));

        var summary = await Worker(coordinator).RunAsync("party", CancellationToken.None);

        Assert.Equal("bad JAML", summary.StopReason);
        Assert.Contains("NotAJoker", summary.Error);
        Assert.Empty(coordinator.Reports);
    }

    [Theory]
    [InlineData(9, 0L, 1L)]
    [InlineData(BatchChars, -1L, 1L)]
    [InlineData(BatchChars, 0L, 0L)]
    [InlineData(BatchChars, 52_521_875L, 1L)]
    public async Task ALeaseOutsideTheSeedSpaceStopsTheWorker(int batchChars, long startBlock, long blockCount)
    {
        var coordinator = new FakeCoordinator(
            new PartyLease("party", PerkeoJaml, batchChars, startBlock, blockCount, 0, "t")
        );

        var summary = await Worker(coordinator).RunAsync("party", CancellationToken.None);

        Assert.Equal("bad lease", summary.StopReason);
        Assert.NotNull(summary.Error);
        Assert.Empty(coordinator.Reports);
    }

    [Fact]
    public async Task TheLeaseIsHeartbeatWhileItIsSearched()
    {
        var coordinator = new FakeCoordinator(Lease(PermissiveJaml, 0, blockCount: 8));
        var worker = new MotelyPartyWorker(coordinator)
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(1),
            Configure = s => s.WithThreadCount(1),
        };

        await worker.RunAsync("party", CancellationToken.None);

        Assert.True(coordinator.Heartbeats > 0, "no heartbeat during the search");
    }

    [Fact]
    public async Task CancellingMidSearchStopsWithoutReporting()
    {
        using var cts = new CancellationTokenSource();
        var coordinator = new FakeCoordinator(Lease(PermissiveJaml, 0, blockCount: 1000));
        var worker = new MotelyPartyWorker(coordinator)
        {
            Configure = s => s.WithThreadCount(1).WithProgressReportIntervalMs(0).WithProgressCallback(_ => cts.Cancel()),
        };

        var run = worker.RunAsync("party", cts.Token);
        var done = await Task.WhenAny(run, Task.Delay(60_000));
        Assert.Same(run, done);

        var summary = await run;
        Assert.Equal("cancelled", summary.StopReason);
        Assert.Empty(coordinator.Reports);
    }
}
