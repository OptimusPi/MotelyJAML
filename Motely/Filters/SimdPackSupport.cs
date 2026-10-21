using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters;

/// <summary>
/// Shared SIMD pack-walk helpers: ante-1 Hieroglyph/Petroglyph reachability and
/// per-lane pack-size PRNG masks so vector prefilters stop under-counting Jumbo/Mega
/// and stop counting ante-1 slots 4–5 on non-extended lanes.
/// </summary>
internal static class SimdPackSupport
{
    /// <summary>
    /// Lanes where ante-2 Hieroglyph or Petroglyph extends ante-1 pack slots to 4–7
    /// (same voucher path as <see cref="ClauseScoring"/> PrepareRunState, including the bonus
    /// voucher an ante-1 Hieroglyph draws before ante 2's voucher is rolled).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask Ante1PackExtensionMask(ref MotelyVectorSearchContext ctx)
    {
        var state = new MotelyVectorRunState();
        var ante1 = ctx.GetAnteFirstVoucher(1, state);
        state.ActivateVoucher(ante1);
        VectorMask ante1Reduces =
            VectorEnum256.Equals(ante1, MotelyVoucher.Hieroglyph)
            | VectorEnum256.Equals(ante1, MotelyVoucher.Petroglyph);
        if (ante1Reduces.IsPartiallyTrue())
        {
            var voucherStream = ctx.CreateVoucherStream(1);
            var bonus = ctx.GetNextVoucher(ref voucherStream, state);
            state.ActivateVoucher(bonus, ante1Reduces);
        }
        var ante2 = ctx.GetAnteFirstVoucher(2, state);
        return VectorEnum256.Equals(ante2, MotelyVoucher.Hieroglyph)
            | VectorEnum256.Equals(ante2, MotelyVoucher.Petroglyph);
    }

    /// <summary>
    /// Per-lane whether pack index is reachable this ante — the same ceiling scoring's
    /// <c>ClampBoosterPackSlotForAnte</c> applies: slots 0–5 on every ante but 1; ante 1 slots
    /// 0–3 always and 4–7 only with the Hieroglyph/Petroglyph extension.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask SlotReachableMask(
        int ante,
        int packIndex,
        VectorMask ante1Extended
    )
    {
        if (ante != 1)
            return packIndex <= MotelyGlobals.LateAntesMaxPackSlot
                ? VectorMask.AllBitsSet
                : VectorMask.NoBitsSet;
        if (packIndex <= MotelyGlobals.EarlyAnteMaxPackSlot)
            return VectorMask.AllBitsSet;
        return packIndex <= Ante1ExtendedMaxPackSlot ? ante1Extended : VectorMask.NoBitsSet;
    }

    /// <summary>Ante 1 with the extension walks both of its shops' packs twice over.</summary>
    public const int Ante1ExtendedMaxPackSlot = 2 * (MotelyGlobals.EarlyAnteMaxPackSlot + 1) - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> ToPrngMask(VectorMask mask) =>
        ExtendIntMaskToDouble(VectorMaskToConditionalSelectMask(mask));

    /// <summary>
    /// Whether the filter needs ante-1 extension masks (requested pack index past early ante cap).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool NeedsAnte1Extension(int maxBoosterPack) =>
        maxBoosterPack > MotelyGlobals.EarlyAnteMaxPackSlot;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void AddMatchCounts(
        VectorMask hit,
        ref Vector256<int> matchCounts
    )
    {
        if (hit.IsAllFalse())
            return;
        matchCounts = Vector256.Add(
            matchCounts,
            Vector256.ConditionalSelect(
                VectorMaskToConditionalSelectMask(hit),
                Vector256.Create(1),
                Vector256<int>.Zero
            )
        );
    }

    /// <summary>
    /// Vector form of match bounds: count ≥ min, and count ≤ max when max is set.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask MeetsMinMaxMask(Vector256<int> matchCounts, int min, int? max)
    {
        Debug.Assert(min > 0);
        var minOk = Vector256.GreaterThan(matchCounts, Vector256.Create(min - 1));
        if (max is null)
            return new VectorMask(VectorizedComparisonToMask(minOk));
        var maxOk = Vector256.LessThanOrEqual(matchCounts, Vector256.Create(max.Value));
        return new VectorMask(VectorizedComparisonToMask(Vector256.BitwiseAnd(minOk, maxOk)));
    }
}
