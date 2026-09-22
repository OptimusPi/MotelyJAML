using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("voucher", "vouchers",
    ValueEnum = typeof(MotelyVoucher), RollsDefault = new[] { 0 })]
public sealed class VoucherClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyVoucher[] Vouchers { get; set; } = [];

    /// <summary>
    /// Voucher-stream indices per ante: 0 = ante award, 1+ = further draws on that ante's
    /// voucher stream (Hieroglyph bonus, voucher-tag shop extras, etc.).
    /// </summary>
    public int[] Rolls { get; set; } = [0];
}

public struct VoucherFilterDesc(VoucherClause clause)
    : IMotelySeedFilterDesc<VoucherFilterDesc.VoucherFilter>,
      IJamlClauseDesc<VoucherClause>
{
    private readonly VoucherClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["voucher", "vouchers"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "rolls"];

    /// <summary>Voucher clauses carry no keys beyond the common set.</summary>
    public static bool Set(VoucherClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(VoucherClause clause, IJamlValueReader value)
    {
        if (!value.TryEnumArray<MotelyVoucher>(out var vouchers))
            return false;
        clause.Vouchers = vouchers;
        return true;
    }

    /// <summary>
    /// Roll 0 is the ante's award: <c>GetAnteFirstVoucher</c> draws uniformly over the voucher
    /// enum and re-rolls until it lands on a base voucher not yet awarded, or an upgrade whose
    /// base has been. Sixteen bases, and each award swaps one base out for its upgrade, so the
    /// live pool stays at sixteen. At ante <c>A</c> a base voucher is therefore
    /// <c>(15/16)^(A−1) · 1/16</c> — it survived the earlier awards, then was drawn — and an upgrade
    /// is <c>(A−1) · (15/16)^(A−2) / 256</c>: its base awarded at one of the earlier antes, itself
    /// not drawn since, then drawn now. Rolls 1+ read the same pool from the same state.
    /// Holding the pool at sixteen is exact until an upgrade is itself awarded, which only
    /// shrinks it, so this slightly understates later antes.
    /// </summary>
    public static double EstimateRarity(VoucherClause clause, in JamlRarityContext ctx)
    {
        int basePool = MotelyEnum<MotelyVoucher>.ValueCount / 2;
        double draw = 1.0 / basePool;
        double survive = 1.0 - draw;
        int trials = JamlPoolRarity.Distinct(clause.Rolls);
        HashSet<MotelyVoucher> wanted = [.. clause.Vouchers];

        double[] pmf = JamlCountDistribution.Zero;
        foreach (int ante in clause.Antes)
        {
            double share = 0.0;
            foreach (var voucher in wanted)
            {
                bool upgrade = ((int)voucher & 1) == 1; // the odd vouchers need their prerequisite
                share += upgrade
                    ? (ante >= 2 ? (ante - 1) * Math.Pow(survive, ante - 2) * draw * draw : 0.0)
                    : Math.Pow(survive, ante - 1) * draw;
            }

            pmf = JamlCountDistribution.Convolve(pmf, JamlCountDistribution.Binomial(trials, share));
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    public readonly VoucherFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        int maxAnte = 0;
        for (int i = 0; i < _clause.Antes.Length; i++)
        {
            if (_clause.Antes[i] > maxAnte)
                maxAnte = _clause.Antes[i];
        }

        // Cache all antes 1..maxAnte so state-building passes on non-target antes work.
        for (int ante = 1; ante <= maxAnte; ante++)
            ctx.CacheAnteFirstVoucher(ante);

        return new VoucherFilter(_clause, maxAnte);
    }

    public struct VoucherFilter(VoucherClause clause, int maxAnte) : IMotelySeedFilter
    {
        private readonly VoucherClause _clause = clause;
        private readonly int _maxAnte = maxAnte;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            Debug.Assert(_clause.Vouchers.Length > 0);

            var clause = _clause;
            int maxAnte = _maxAnte;

            Vector256<int> matchCounts = Vector256<int>.Zero;
            var voucherState = new MotelyVectorRunState();

            for (int ante = 1; ante <= maxAnte; ante++)
            {
                bool isTarget = false;
                for (int i = 0; i < clause.Antes.Length; i++)
                {
                    if (clause.Antes[i] == ante)
                    {
                        isTarget = true;
                        break;
                    }
                }
                var vouchers = ctx.GetAnteFirstVoucher(ante, voucherState);
                voucherState.ActivateVoucher(vouchers);

                if (!isTarget)
                    continue;

                matchCounts = AccumulateVoucherRolls(
                    ref ctx,
                    ante,
                    ref voucherState,
                    vouchers,
                    clause,
                    matchCounts
                );
            }

            return JamlSimdPackSupport.MeetsMinMaxMask(matchCounts, clause.Min, clause.Max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> AccumulateVoucherRolls(
            ref MotelyVectorSearchContext ctx,
            int ante,
            ref MotelyVectorRunState voucherState,
            VectorEnum256<MotelyVoucher> anteVouchers,
            VoucherClause clause,
            Vector256<int> matchCounts
        )
        {
            // Walk the voucher stream once, index 0..maxRoll, mirroring the scalar
            // CountVoucherOccurrences. The old code only materialized draws 1 and 2, so any
            // requested roll index >= 3 (the clause doc says "1+") was silently dropped by SIMD
            // while scalar scoring still counted it — a SIMD/scalar completeness gap. Index 0 is
            // the ante award; 1+ are successive stream draws. (stackalloc of VectorEnum256 is the
            // same pattern TagFilter uses for its tag-stream draws.)
            int maxRoll = MapFeatureRolls.MaxRollIndex(clause.Rolls);
            Span<VectorEnum256<MotelyVoucher>> draws =
                stackalloc VectorEnum256<MotelyVoucher>[maxRoll + 1];
            draws[0] = anteVouchers;

            if (maxRoll >= 1)
            {
                var voucherStream = ctx.CreateVoucherStream(ante);
                for (int i = 1; i <= maxRoll; i++)
                    draws[i] = ctx.GetNextVoucher(ref voucherStream, voucherState);
            }

            foreach (var roll in clause.Rolls)
                matchCounts = AddVoucherMatches(matchCounts, draws[roll], clause);

            return matchCounts;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> AddVoucherMatches(
            Vector256<int> counts,
            VectorEnum256<MotelyVoucher> vouchers,
            VoucherClause clause,
            VectorMask? includeMask = null
        )
        {
            Vector256<int> matchMask = Vector256<int>.Zero;

            if (clause.Vouchers.Length == 1)
            {
                matchMask = VectorEnum256.Equals(vouchers, clause.Vouchers[0]);
            }
            else
            {
                foreach (var v in clause.Vouchers)
                    matchMask = Vector256.Max(matchMask, VectorEnum256.Equals(vouchers, v));
            }

            if (includeMask.HasValue)
            {
                matchMask = Vector256.BitwiseAnd(
                    matchMask,
                    MotelyVectorUtils.VectorMaskToConditionalSelectMask(includeMask.Value)
                );
            }

            return Vector256.Add(
                counts,
                Vector256.ConditionalSelect(matchMask, Vector256.Create(1), Vector256<int>.Zero)
            );
        }
    }
}

/// <summary>
/// Combines multiple <see cref="VoucherClause"/>s into a single ante-loop filter so voucher
/// PRNG state is built only once. Use when two or more voucher clauses appear in the same
/// Must/MustNot set (e.g. Telescope@ante1 + Observatory@ante2).
/// </summary>
public struct MultiVoucherFilterDesc(VoucherClause[] clauses)
    : IMotelySeedFilterDesc<MultiVoucherFilterDesc.MultiVoucherFilter>
{
    private readonly VoucherClause[] _clauses = clauses;

    public readonly MultiVoucherFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        int maxAnte = 0;
        foreach (var c in _clauses)
            for (int i = 0; i < c.Antes.Length; i++)
                if (c.Antes[i] > maxAnte)
                    maxAnte = c.Antes[i];

        for (int ante = 1; ante <= maxAnte; ante++)
            ctx.CacheAnteFirstVoucher(ante);

        return new MultiVoucherFilter(_clauses, maxAnte);
    }

    public struct MultiVoucherFilter(VoucherClause[] clauses, int maxAnte) : IMotelySeedFilter
    {
        private readonly VoucherClause[] _clauses = clauses;
        private readonly int _maxAnte = maxAnte;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clauses = _clauses;
            int maxAnte = _maxAnte;
            var voucherState = new MotelyVectorRunState();

            Span<Vector256<int>> matchCounts = stackalloc Vector256<int>[clauses.Length];

            for (int ante = 1; ante <= maxAnte; ante++)
            {
                var vouchers = ctx.GetAnteFirstVoucher(ante, voucherState);
                voucherState.ActivateVoucher(vouchers);

                // Determine which clauses target this ante
                bool anyTarget = false;
                for (int ci = 0; ci < clauses.Length; ci++)
                {
                    var anteList = clauses[ci].Antes;
                    for (int ai = 0; ai < anteList.Length; ai++)
                    {
                        if (anteList[ai] == ante)
                        {
                            anyTarget = true;
                            break;
                        }
                    }
                }

                if (!anyTarget)
                    continue;

                // Accumulate matches for each targeting clause
                for (int ci = 0; ci < clauses.Length; ci++)
                {
                    var clause = clauses[ci];
                    bool isTarget = false;
                    for (int ai = 0; ai < clause.Antes.Length; ai++)
                        if (clause.Antes[ai] == ante)
                        {
                            isTarget = true;
                            break;
                        }
                    if (!isTarget)
                        continue;

                    matchCounts[ci] = VoucherFilterDesc.VoucherFilter.AccumulateVoucherRolls(
                        ref ctx,
                        ante,
                        ref voucherState,
                        vouchers,
                        clause,
                        matchCounts[ci]
                    );
                }
            }

            // AND all clause pass masks together
            VectorMask result = VectorMask.AllBitsSet;
            for (int ci = 0; ci < clauses.Length; ci++)
            {
                var clause = clauses[ci];
                VectorMask clauseMask = Vector256.GreaterThan(
                    matchCounts[ci],
                    Vector256.Subtract(Vector256.Create(clause.Min), Vector256.Create(1))
                );
                result &= clauseMask;
            }
            return result;
        }
    }
}
