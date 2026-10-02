using System.Collections.Concurrent;
using Motely.Distributed;
using Motely.DistributedWorker;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>MotelyWorker's loop against an in-memory home and the real engine.</summary>
public class HomeWorkerTests(ITestOutputHelper output)
{
    // 6GR89UQF matches; most of its neighbours do not.
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

    private const int BatchChars = 3;

    private static Work PerkeoBlock(long offset = 0)
    {
        long block = SeedMath.SeedToBatchIndex("6GR89UQF", BatchChars) + offset;
        return new Work("bench-negative-perkeo", PerkeoJaml, BatchChars, block, block + 1);
    }

    /// <summary>Hands out the queued answers in order; when they run out, it cancels the worker.</summary>
    private sealed class FakeHome(CancellationTokenSource stop, params object[] answers) : IHome
    {
        private readonly Queue<object> _answers = new(answers);
        public readonly ConcurrentQueue<WorkDone> Reports = new();

        public Task<Work?> ClaimAsync(string worker, CancellationToken cancellationToken)
        {
            if (!_answers.TryDequeue(out var answer))
            {
                stop.Cancel();
                return Task.FromResult<Work?>(null);
            }
            return answer is Exception ex ? Task.FromException<Work?>(ex) : Task.FromResult((Work?)answer);
        }

        public Task DoneAsync(WorkDone done, CancellationToken cancellationToken)
        {
            Reports.Enqueue(done);
            return Task.CompletedTask;
        }
    }

    private HomeWorker Worker(IHome home) =>
        new(home, "test")
        {
            Threads = 4,
            RetryDelay = TimeSpan.FromMilliseconds(10),
            IdleDelay = TimeSpan.FromMilliseconds(10),
            Log = output.WriteLine,
        };

    [Fact]
    public async Task AClaimIsGroundAndItsFindsReported()
    {
        using var stop = new CancellationTokenSource();
        var home = new FakeHome(stop, PerkeoBlock(), PerkeoBlock(1));
        var worker = Worker(home);

        Assert.True(await worker.RunAsync(stop.Token));

        Assert.Equal(2, home.Reports.Count);
        var hit = home.Reports.First();
        Assert.Equal(("bench-negative-perkeo", "test", PerkeoBlock().Start), (hit.Filter, hit.Worker, hit.Start));
        Assert.Contains("6GR89UQF", hit.Seeds.Select(s => s.Seed));
        Assert.Equal(MotelyGlobals.SeedsPerSequentialBatch(BatchChars), hit.SeedsSearched);
        Assert.Empty(home.Reports.Last().Seeds);
        Assert.Equal((2, 2L * MotelyGlobals.SeedsPerSequentialBatch(BatchChars)), (worker.Claims, worker.SeedsSearched));
    }

    [Fact]
    public async Task AnUnreachableHomeIsRetriedAndAnEmptyQueueIsWaitedOn()
    {
        using var stop = new CancellationTokenSource();
        var home = new FakeHome(stop, new HttpRequestException("down"), null!, PerkeoBlock());

        Assert.True(await Worker(home).RunAsync(stop.Token));

        Assert.Single(home.Reports);
    }

    [Fact]
    public async Task TheWorkerGivesUpAfterTheFailureLimit()
    {
        using var stop = new CancellationTokenSource();
        var home = new FakeHome(stop, new HttpRequestException("down"), new HttpRequestException("down"), PerkeoBlock());
        var worker = new HomeWorker(home, "test") { MaxConsecutiveFailures = 2, RetryDelay = TimeSpan.FromMilliseconds(10) };

        Assert.False(await worker.RunAsync(stop.Token));

        Assert.Empty(home.Reports);
    }

    [Fact]
    public async Task AJamlThisBuildCannotRunIsLoggedAndSkipped()
    {
        using var stop = new CancellationTokenSource();
        var logged = new List<string>();
        var home = new FakeHome(stop, new Work("x", "name: x\nmust:\n  - joker: NotAJoker\n", 3, 0, 1));
        var worker = new HomeWorker(home, "test") { RetryDelay = TimeSpan.FromMilliseconds(10), Log = logged.Add };

        Assert.True(await worker.RunAsync(stop.Token));

        Assert.Empty(home.Reports);
        Assert.Contains(logged, l => l.Contains("NotAJoker"));
    }

    [Fact]
    public async Task CancellingMidGrindReportsNothing()
    {
        using var stop = new CancellationTokenSource();
        var home = new FakeHome(stop, new Work("bench-negative-perkeo", PerkeoJaml, BatchChars, 0, 10_000));
        var worker = new HomeWorker(home, "test")
        {
            Threads = 1,
            Progress = _ => stop.Cancel(),
        };

        var run = worker.RunAsync(stop.Token);
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(60_000)));

        Assert.True(await run);
        Assert.Empty(home.Reports);
    }
}
