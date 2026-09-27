using System.Runtime.Intrinsics;

namespace Motely.Tests;

/// <summary>
/// R3 parity for the PRNG. <see cref="VectorLuaRandom"/> runs eight seeds at once;
/// <see cref="LuaRandom"/> runs one. Lane <c>i</c> of the vector form must produce exactly what a
/// scalar generator seeded with the same value produces — value for value, in order.
///
/// Order within a stream is law (CLAUDE.md 12), so these tests advance both forms in lockstep for
/// several draws rather than checking a single value. A vector path that drifted after the first
/// call would pass a one-shot test and corrupt every search.
/// </summary>
public sealed class VectorLuaRandomParityTests
{
    /// <summary>Eight unlike seeds, including 0 and a negative, so no lane mirrors another.</summary>
    private static readonly double[] Seeds =
        [0d, 1d, 0.5d, 123.456d, -7.25d, 1e-6d, 42d, 98765.4321d];

    private static Vector512<double> SeedVector() =>
        Vector512.Create(
            Seeds[0],
            Seeds[1],
            Seeds[2],
            Seeds[3],
            Seeds[4],
            Seeds[5],
            Seeds[6],
            Seeds[7]
        );

    [Fact]
    public void RandInt_MatchesScalarForEveryLaneAcrossManyDraws()
    {
        var vector = new VectorLuaRandom(SeedVector());
        var scalars = Seeds.Select(static s => new LuaRandom(s)).ToArray();

        for (int draw = 0; draw < 8; draw++)
        {
            var vectorDraw = vector.RandInt();
            for (int lane = 0; lane < Seeds.Length; lane++)
                Assert.Equal(scalars[lane].RandInt(), vectorDraw[lane]);
        }
    }

    [Fact]
    public void RandDblMem_MatchesScalarForEveryLaneAcrossManyDraws()
    {
        var vector = new VectorLuaRandom(SeedVector());
        var scalars = Seeds.Select(static s => new LuaRandom(s)).ToArray();

        for (int draw = 0; draw < 8; draw++)
        {
            var vectorDraw = vector.RandDblMem();
            for (int lane = 0; lane < Seeds.Length; lane++)
                Assert.Equal(scalars[lane].RandDblMem(), vectorDraw[lane]);
        }
    }

    [Fact]
    public void Random_MatchesScalarForEveryLaneAcrossManyDraws()
    {
        var vector = new VectorLuaRandom(SeedVector());
        var scalars = Seeds.Select(static s => new LuaRandom(s)).ToArray();

        for (int draw = 0; draw < 8; draw++)
        {
            var vectorDraw = vector.Random();
            for (int lane = 0; lane < Seeds.Length; lane++)
                Assert.Equal(scalars[lane].Random(), vectorDraw[lane]);
        }
    }

    [Fact]
    public void Random_StaysInTheUnitInterval()
    {
        var vector = new VectorLuaRandom(SeedVector());

        for (int draw = 0; draw < 16; draw++)
        {
            var values = vector.Random();
            for (int lane = 0; lane < Seeds.Length; lane++)
            {
                Assert.InRange(values[lane], 0d, 1d);
            }
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 8)]
    [InlineData(1, 5)]
    [InlineData(-3, 3)]
    public void RandIntRange_MatchesScalarAndStaysInRange(int min, int max)
    {
        var vector = new VectorLuaRandom(SeedVector());
        var scalars = Seeds.Select(static s => new LuaRandom(s)).ToArray();

        for (int draw = 0; draw < 4; draw++)
        {
            var vectorDraw = vector.RandInt(min, max);
            for (int lane = 0; lane < Seeds.Length; lane++)
            {
                Assert.Equal(scalars[lane].RandInt(min, max), vectorDraw[lane]);
                Assert.InRange(vectorDraw[lane], min, max);
            }
        }
    }

    // ── the static single-shot forms ──

    [Fact]
    public void StaticRandInt_MatchesScalarPerLane()
    {
        var actual = VectorLuaRandom.RandInt(SeedVector());

        for (int lane = 0; lane < Seeds.Length; lane++)
            Assert.Equal(LuaRandom.RandInt(Seeds[lane]), actual[lane]);
    }

    [Fact]
    public void StaticRandDblMem_MatchesScalarPerLane()
    {
        var actual = VectorLuaRandom.RandDblMem(SeedVector());

        for (int lane = 0; lane < Seeds.Length; lane++)
            Assert.Equal(LuaRandom.RandDblMem(Seeds[lane]), actual[lane]);
    }

    [Fact]
    public void StaticRandom_MatchesScalarPerLane()
    {
        var actual = VectorLuaRandom.Random(SeedVector());

        for (int lane = 0; lane < Seeds.Length; lane++)
            Assert.Equal(LuaRandom.Random(Seeds[lane]), actual[lane]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 8)]
    [InlineData(2, 9)]
    public void StaticRandIntRange_MatchesScalarPerLane(int min, int max)
    {
        var actual = VectorLuaRandom.RandInt(SeedVector(), min, max);

        for (int lane = 0; lane < Seeds.Length; lane++)
        {
            Assert.Equal(LuaRandom.RandInt(Seeds[lane], min, max), actual[lane]);
            Assert.InRange(actual[lane], min, max);
        }
    }

    /// <summary>
    /// The static form is the first draw of the instance form. If these diverged, a filter that
    /// used one shape would silently disagree with a filter that used the other.
    /// </summary>
    [Fact]
    public void StaticForm_EqualsFirstDrawOfInstanceForm()
    {
        var seedVector = SeedVector();
        var instance = new VectorLuaRandom(seedVector);

        Assert.Equal(VectorLuaRandom.RandInt(seedVector), instance.RandInt());
    }

    /// <summary>Same seed in, same stream out — the whole search depends on it.</summary>
    [Fact]
    public void SameSeed_ProducesTheSameStream()
    {
        var a = new VectorLuaRandom(SeedVector());
        var b = new VectorLuaRandom(SeedVector());

        for (int draw = 0; draw < 8; draw++)
            Assert.Equal(a.RandInt(), b.RandInt());
    }

    /// <summary>Different seeds must not collapse to one stream.</summary>
    [Fact]
    public void DifferentSeeds_ProduceDifferentLanes()
    {
        var values = new VectorLuaRandom(SeedVector()).RandInt();

        var distinct = new HashSet<ulong>();
        for (int lane = 0; lane < Seeds.Length; lane++)
            distinct.Add(values[lane]);

        Assert.True(
            distinct.Count > 1,
            "eight unlike seeds collapsed to a single value — lanes are not independent"
        );
    }
}
