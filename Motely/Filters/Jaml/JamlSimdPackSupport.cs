using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters.Jaml;

internal static class JamlSimdPackSupport
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask Ante1PackExtensionMask(ref MotelyVectorSearchContext ctx)
    {
        var state = new MotelyVectorRunState();
        var ante1 = ctx.GetAnteFirstVoucher(1, state);
        state.ActivateVoucher(ante1);
        var ante2 = ctx.GetAnteFirstVoucher(2, state);
        return VectorEnum256.Equals(ante2, MotelyVoucher.Hieroglyph)
            | VectorEnum256.Equals(ante2, MotelyVoucher.Petroglyph);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VectorMask SlotReachableMask(
        int ante,
        int packIndex,
        VectorMask ante1Extended
    )
    {
        if (ante != 1 || packIndex <= MotelyGlobals.EarlyAnteMaxPackSlot)
            return VectorMask.AllBitsSet;
        return ante1Extended;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector512<double> ToPrngMask(VectorMask mask) =>
        ExtendIntMaskToDouble(VectorMaskToConditionalSelectMask(mask));

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
