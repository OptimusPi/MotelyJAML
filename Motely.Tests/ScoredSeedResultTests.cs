using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// <see cref="MotelyScoredSeedResult"/> is the per-seed tally the sinks hand out. It is reused
/// across seeds by design, so the case that matters is that <c>Reset</c> really forgets the
/// previous seed's tallies — a stale tally would attribute one seed's score to the next.
/// </summary>
public sealed class ScoredSeedResultTests
{
    [Fact]
    public void NewResult_StartsEmptyNotNull()
    {
        var result = new MotelyScoredSeedResult();

        Assert.Equal(string.Empty, result.Seed);
        Assert.Equal(0, result.Score);
        Assert.Equal(0, result.TallyCount);
        Assert.Empty(result.Tallies);
        Assert.Empty(result.Tally);
        Assert.True(result.TallyValuesSpan.IsEmpty);
    }

    [Fact]
    public void AddTally_AccumulatesInOrder()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("SEED", 12);

        result.AddTally(3);
        result.AddTally(0);
        result.AddTally(7);

        Assert.Equal("SEED", result.Seed);
        Assert.Equal(12, result.Score);
        Assert.Equal(3, result.TallyCount);
        Assert.Equal([3, 0, 7], result.Tallies);
        Assert.Equal([3, 0, 7], result.TallyValuesSpan.ToArray());
        Assert.Equal<byte>([3, 0, 7], result.Tally);
    }

    [Fact]
    public void GetTally_ReturnsZeroOutsideTheRecordedRange()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("SEED");
        result.AddTally(9);

        Assert.Equal(9, result.GetTally(0));
        Assert.Equal(0, result.GetTally(1));
        Assert.Equal(0, result.GetTally(-1));
        Assert.Equal(0, result.GetTally(MotelyScoredSeedResult.MAX_TALLY_COUNT));
    }

    /// <summary>The reuse contract: nothing from the previous seed survives a Reset.</summary>
    [Fact]
    public void Reset_ForgetsThePreviousSeedsTallies()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("FIRST", 100);
        result.AddTally(5);
        result.AddTally(6);

        result.Reset("SECOND", 3);

        Assert.Equal("SECOND", result.Seed);
        Assert.Equal(3, result.Score);
        Assert.Equal(0, result.TallyCount);
        Assert.Empty(result.Tallies);
        Assert.Equal(0, result.GetTally(0));

        result.AddTally(1);
        Assert.Equal([1], result.Tallies);
    }

    [Fact]
    public void Reset_DefaultsScoreToZero()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("SEED", 42);
        result.Reset("SEED2");

        Assert.Equal(0, result.Score);
    }

    /// <summary>Round-trips the tallies the way the WASM interop boundary does.</summary>
    [Fact]
    public void TalliesSetter_RestoresACapturedTally()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("SEED");

        result.Tallies = [4, 5, 6];

        Assert.Equal(3, result.TallyCount);
        Assert.Equal([4, 5, 6], result.Tallies);
        Assert.Equal(5, result.GetTally(1));
    }

    [Fact]
    public void TalliesSetter_TreatsNullAsEmpty()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("SEED");
        result.AddTally(1);

        result.Tallies = null!;

        Assert.Equal(0, result.TallyCount);
        Assert.Empty(result.Tallies);
    }

    [Fact]
    public void TalliesSetter_CapsAtTheFixedBufferSize()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("SEED");

        result.Tallies = Enumerable
            .Range(0, MotelyScoredSeedResult.MAX_TALLY_COUNT + 50)
            .ToArray();

        Assert.Equal(MotelyScoredSeedResult.MAX_TALLY_COUNT, result.TallyCount);
        Assert.Equal(0, result.GetTally(0));
        Assert.Equal(
            MotelyScoredSeedResult.MAX_TALLY_COUNT - 1,
            result.GetTally(MotelyScoredSeedResult.MAX_TALLY_COUNT - 1)
        );
    }

    /// <summary>Tally exposes bytes; values above 255 wrap, which callers should know.</summary>
    [Fact]
    public void Tally_NarrowsToBytes()
    {
        var result = new MotelyScoredSeedResult();
        result.Reset("SEED");
        result.AddTally(255);
        result.AddTally(256);

        Assert.Equal<byte>([255, 0], result.Tally);
        Assert.Equal([255, 256], result.Tallies);
    }
}
