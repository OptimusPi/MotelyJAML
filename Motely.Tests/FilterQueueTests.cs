using Motely.Distributed;
using Motely.Mcp;

namespace Motely.Tests;

/// <summary>
/// The home queue on a real DuckDB file: a filter is queued under its name's slug, slices go out
/// first-gap-first and round-robin across filters, a slice nobody reports is handed out again,
/// and everything done survives a restart.
/// </summary>
public sealed class FilterQueueTests : IDisposable
{
    private const string PerkeoJaml = """
        name: Negative Perkeo (ante 1)
        deck: Red
        stake: White
        must:
          - legendaryJoker: Perkeo
            antes: [1]
            edition: Negative
        """;

    private const string TriboulotJaml = """
        name: Triboulet
        must:
          - legendaryJoker: Triboulet
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("motely-home-").FullName;
    private readonly Clock _clock = new();

    private string DbPath => Path.Combine(_dir, "motely.duckdb");

    private FilterQueue Open() => new(DbPath, _clock);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static WorkDone Done(Work work, string worker = "pc", long searched = 0, params (string Seed, int Score)[] seeds) =>
        new(work.Filter, worker, work.Start, work.End, searched, [.. seeds.Select(s => new FoundSeed(s.Seed, s.Score))]);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void AFilterIsQueuedUnderTheSlugOfItsName()
    {
        using var queue = Open();

        var status = queue.Add(PerkeoJaml, batchChars: 4);

        Assert.Equal("negative-perkeo-ante-1", status.Filter);
        Assert.Equal("Negative Perkeo (ante 1)", status.Name);
        Assert.Equal(MotelyGlobals.SequentialBatchCount(4), status.Batches);
        Assert.Equal(0, status.BatchesDone);
        Assert.False(status.Complete);
        Assert.Equal(status, Assert.Single(queue.List()));
    }

    [Fact]
    public void TheSameJamlAgainKeepsItsProgressAndADifferentOneIsRefused()
    {
        using var queue = Open();
        queue.Add(PerkeoJaml, 3);
        var work = queue.Claim("pc")!;
        queue.Done(Done(work));

        Assert.Equal(1, queue.Add(PerkeoJaml, 3).BatchesDone);
        Assert.Throws<InvalidOperationException>(() => queue.Add(PerkeoJaml, 4));
        Assert.Throws<InvalidOperationException>(() => queue.Add(PerkeoJaml.Replace("edition: Negative", "edition: Foil"), 3));
        Assert.Throws<ArgumentException>(() => queue.Add("must:\n  - joker: Blueprint\n", 3));
    }

    [Fact]
    public void SlicesGoOutFirstGapFirstAndAReportRecordsThem()
    {
        using var queue = Open();
        queue.Add(PerkeoJaml, 3);

        var first = queue.Claim("pc")!;
        Assert.Equal(("negative-perkeo-ante-1", 3, 0L, 1L), (first.Filter, first.BatchChars, first.Start, first.End));
        Assert.Equal(PerkeoJaml, first.Jaml);
        Assert.Equal(["pc"], queue.Status(first.Filter)!.Workers);

        var second = queue.Claim("laptop")!;
        Assert.Equal((1L, 2L), (second.Start, second.End));

        Assert.True(queue.Done(Done(first, seeds: [("6GR89UQF", 3), ("AAAAAAAA", 1), ("6GR89UQF", 3)])));
        var status = queue.Status(first.Filter)!;
        Assert.Equal(1, status.BatchesDone);
        Assert.Equal(2, status.Finds);
        Assert.Equal(["laptop"], status.Workers);
        Assert.Equal("seed,score,worker\n6GR89UQF,3,pc\nAAAAAAAA,1,pc\n", queue.Seeds(first.Filter));
        Assert.Equal("seed,score,worker\n6GR89UQF,3,pc\n", queue.Seeds(first.Filter, top: 1));

        // Reporting the same slice twice, or a slice outside the filter, changes nothing.
        Assert.True(queue.Done(Done(first, "laptop", seeds: [("6GR89UQF", 9)])));
        Assert.False(queue.Done(new WorkDone(first.Filter, "pc", 5, 4, 0, [])));
        Assert.False(queue.Done(new WorkDone("nope", "pc", 0, 1, 0, [])));
        Assert.Equal(1, queue.Status(first.Filter)!.BatchesDone);
        Assert.Equal("seed,score,worker\n6GR89UQF,3,pc\nAAAAAAAA,1,pc\n", queue.Seeds(first.Filter));

        var third = queue.Claim("pc")!;
        Assert.Equal((2L, 3L), (third.Start, third.End));
    }

    [Fact]
    public void ASliceNobodyReportsIsHandedOutAgainAfterTheTtl()
    {
        using var queue = Open();
        queue.Add(PerkeoJaml, 3);
        var lost = queue.Claim("pc")!;
        var kept = queue.Claim("laptop")!;
        Assert.Equal((1L, 2L), (kept.Start, kept.End));

        _clock.Now += FilterQueue.ClaimTtl;

        var again = queue.Claim("laptop")!;
        Assert.Equal((lost.Start, lost.End), (again.Start, again.End));
        Assert.Equal(["laptop"], queue.Status(lost.Filter)!.Workers);
    }

    [Fact]
    public void AClaimIsSizedToTheWorkersLastRate()
    {
        using var queue = Open();
        queue.Add(PerkeoJaml, 2);
        long perBatch = MotelyGlobals.SeedsPerSequentialBatch(2);

        var first = queue.Claim("pc")!;
        Assert.Equal(1, first.End - first.Start);
        _clock.Now += TimeSpan.FromSeconds(1);
        queue.Done(Done(first, searched: perBatch * 10)); // 10 batches/s

        var next = queue.Claim("pc")!;
        Assert.Equal((1L, 301L), (next.Start, next.End));
        var newcomer = queue.Claim("newcomer")!;
        Assert.Equal(1, newcomer.End - newcomer.Start);
    }

    [Fact]
    public void FiltersAreGroundRoundRobinUntilEachIsComplete()
    {
        using var queue = Open();
        queue.Add(PerkeoJaml, 7);   // 35 batches
        queue.Add(TriboulotJaml, 7);

        Assert.Equal(["negative-perkeo-ante-1", "triboulet", "negative-perkeo-ante-1", "triboulet"],
            Enumerable.Range(0, 4).Select(_ => queue.Claim("pc")!.Filter));

        for (var work = queue.Claim("pc"); work is not null; work = queue.Claim("pc"))
            queue.Done(Done(work));
        _clock.Now += FilterQueue.ClaimTtl;
        for (var work = queue.Claim("pc"); work is not null; work = queue.Claim("pc"))
            queue.Done(Done(work));

        Assert.All(queue.List(), f => Assert.True(f.Complete, f.Filter));
        Assert.Null(queue.Claim("pc"));
    }

    [Fact]
    public void ProgressAndFindsSurviveARestart()
    {
        Work work;
        using (var queue = Open())
        {
            queue.Add(PerkeoJaml, 3);
            work = queue.Claim("pc")!;
            queue.Done(Done(work, seeds: [("6GR89UQF", 2)]));
            queue.Claim("pc"); // out on claim at shutdown: not done, so handed out again
        }

        using var reopened = Open();
        var status = Assert.Single(reopened.List());
        Assert.Equal((work.Filter, 1L, 1, Array.Empty<string>()), (status.Filter, status.BatchesDone, status.Finds, status.Workers));
        Assert.Equal("seed,score,worker\n6GR89UQF,2,pc\n", reopened.Seeds(work.Filter));
        var next = reopened.Claim("pc")!;
        Assert.Equal((1L, 2L), (next.Start, next.End));
    }

    [Fact]
    public void RemoveDeletesTheFilterItsProgressAndItsFinds()
    {
        using var queue = Open();
        queue.Add(PerkeoJaml, 3);
        var work = queue.Claim("pc")!;
        queue.Done(Done(work, seeds: [("6GR89UQF", 2)]));

        Assert.True(queue.Remove(work.Filter));
        Assert.False(queue.Remove(work.Filter));
        Assert.Empty(queue.List());
        Assert.Null(queue.Seeds(work.Filter));
        Assert.Equal(0, queue.Add(PerkeoJaml, 3).BatchesDone);
        Assert.Equal("seed,score,worker\n", queue.Seeds(work.Filter));
    }

    [Theory]
    [InlineData("Negative Perkeo (ante 1)", "negative-perkeo-ante-1")]
    [InlineData("  bench-negative-perkeo ", "bench-negative-perkeo")]
    [InlineData("Jimbo's Ante!!", "jimbo-s-ante")]
    [InlineData("---", "")]
    public void TheSlugIsTheNameInUrlForm(string name, string slug) =>
        Assert.Equal(slug, FilterSlug.Of(name));

    [Fact]
    public void TheBeaconMessageRoundTrips()
    {
        Assert.True(HomeBeacon.TryParse(HomeBeacon.Message(35036), out int port));
        Assert.Equal(35036, port);
        Assert.False(HomeBeacon.TryParse("motely-home:0"u8, out _));
        Assert.False(HomeBeacon.TryParse("hello"u8, out _));
    }
}
