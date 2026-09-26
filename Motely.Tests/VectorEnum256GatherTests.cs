using System.Runtime.Intrinsics;

namespace Motely.Tests;

/// <summary>
/// Table-lookup Create(indices, values) must match per-lane scalar indexing.
/// On AVX2 hosts this path is a gather; on Apple Silicon / non-AVX2 it is the scalar fallback.
/// Both must produce identical lanes.
/// </summary>
public sealed class VectorEnum256GatherTests
{
    [Fact]
    public void CreateFromIndices_MatchesScalarTableLookup()
    {
        MotelyVoucher[] values =
        [
            MotelyVoucher.Overstock,
            MotelyVoucher.ClearanceSale,
            MotelyVoucher.Hone,
            MotelyVoucher.RerollSurplus,
            MotelyVoucher.CrystalBall,
            MotelyVoucher.Telescope,
            MotelyVoucher.Grabber,
            MotelyVoucher.Wasteful,
            MotelyVoucher.TarotMerchant,
            MotelyVoucher.PlanetMerchant,
        ];

        var indices = Vector256.Create(0, 3, 9, 1, 7, 2, 5, 4);
        var got = VectorEnum256.Create(indices, values);

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal(values[indices[lane]], got[lane]);
    }

    [Fact]
    public void CreateFromIndices_RepeatedIndex_IsLegal()
    {
        MotelyTarotCard[] values =
        [
            MotelyTarotCard.TheFool,
            MotelyTarotCard.TheMagician,
            MotelyTarotCard.TheHighPriestess,
            MotelyTarotCard.TheEmpress,
        ];

        // All lanes point at the same slot — gather and scalar both handle this.
        var indices = Vector256.Create(2);
        var got = VectorEnum256.Create(indices, values);

        for (int lane = 0; lane < Vector256<int>.Count; lane++)
            Assert.Equal(MotelyTarotCard.TheHighPriestess, got[lane]);
    }
}
