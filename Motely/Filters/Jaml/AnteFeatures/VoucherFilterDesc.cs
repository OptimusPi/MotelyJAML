using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("voucher", "vouchers",
    ValueEnum = typeof(MotelyVoucher), RollsDefault = new[] { 0 })]
[YamlObject]
public sealed partial class VoucherClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyVoucher[] Vouchers { get; set; } = [];

    public int[] Rolls { get; set; } = [0];
}

public struct VoucherFilterDesc(VoucherClause clause)
    : IMotelySeedFilterDesc<VoucherFilterDesc.VoucherFilter>
{
    private readonly VoucherClause _clause = clause;

    public static string[] Discriminators => ["voucher", "vouchers"];

    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "rolls"];

    public readonly VoucherFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        int maxAnte = 0;
        for (int i = 0; i < _clause.Antes.Length; i++)
        {
            if (_clause.Antes[i] > maxAnte)
                maxAnte = _clause.Antes[i];
        }

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
            foreach (var v in clause.Vouchers)
                matchMask = Vector256.BitwiseOr(matchMask, VectorEnum256.Equals(vouchers, v));

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
