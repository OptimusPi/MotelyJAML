using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("businessPayout", RollsAreInlineValue = true)]
public sealed class BusinessPayoutClause : IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Rolls { get; set; } = [];
    // No Luck. Business Card is flat 50/50 (Chance = 2) — one Oops saturates to
    // guaranteed, so luck is binary, not a dial. The field is gone by construction,
    // not inherited-then-forbidden.
}

public struct BusinessPayoutFilterDesc(BusinessPayoutClause clause)
    : IMotelySeedFilterDesc<BusinessPayoutFilterDesc.BusinessPayoutFilter>,
      IJamlClauseDesc<BusinessPayoutClause>
{
    private readonly BusinessPayoutClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["businessPayout"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label"];

    /// <inheritdoc/>
    public static bool Set(BusinessPayoutClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static double EstimateRarity(BusinessPayoutClause clause, in JamlRarityContext ctx) =>
        JamlRollRarity.Window(clause, JamlRollRarity.Rate(MotelyGlobals.JokerBusinessChance));

    public BusinessPayoutFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        Debug.Assert(
            _clause.Rolls.Length > 0,
            "Business clause must provide at least one roll index."
        );
        int[] sortedRolls = [.. _clause.Rolls];
        Array.Sort(sortedRolls);
        return new BusinessPayoutFilter(sortedRolls, _clause.Min);
    }

    public struct BusinessPayoutFilter(int[] sortedRolls, int min) : IMotelySeedFilter
    {
        private readonly int[] _sortedRolls = sortedRolls;
        private readonly int _min = min;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            int[] sorted = _sortedRolls;
            var stream = ctx.CreateBusinessPrngStream();
            int maxRoll = sorted[^1];
            int min = _min;

            var matchCounts = Vector256<int>.Zero;
            var minVector = Vector256.Create(min);
            int total = sorted.Length;
            int p = 0,
                seen = 0;

            for (int idx = 0; idx <= maxRoll; idx++)
            {
                VectorMask trigger = ctx.GetNextBusinessPayout(ref stream);

                if (p >= sorted.Length || idx != sorted[p])
                    continue;
                while (p < sorted.Length && sorted[p] == idx)
                    p++;

                seen++;
                matchCounts = Vector256.Add(
                    matchCounts,
                    Vector256.Create(
                        trigger[0] ? 1 : 0,
                        trigger[1] ? 1 : 0,
                        trigger[2] ? 1 : 0,
                        trigger[3] ? 1 : 0,
                        trigger[4] ? 1 : 0,
                        trigger[5] ? 1 : 0,
                        trigger[6] ? 1 : 0,
                        trigger[7] ? 1 : 0
                    )
                );

                if (total > 8)
                {
                    int rollsRemaining = total - seen;
                    var possibleMax = Vector256.Add(matchCounts, Vector256.Create(rollsRemaining));
                    var maskHit = Vector256.GreaterThanOrEqual(matchCounts, minVector);
                    var maskFail = Vector256.LessThan(possibleMax, minVector);
                    if (Vector256.BitwiseOr(maskHit, maskFail).ExtractMostSignificantBits() == 0xFF)
                        break;
                }
            }

            return new VectorMask(
                MotelyVectorUtils.VectorizedComparisonToMask(
                    Vector256.GreaterThan(matchCounts, Vector256.Create(min - 1))
                )
            );
        }
    }
}
