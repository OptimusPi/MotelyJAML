using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("bloodstoneTrigger", RollsAreInlineValue = true)]
public sealed class BloodstoneTriggerClause : IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Rolls { get; set; } = [];
    // No Luck. Bloodstone is flat 50/50 (Chance = 2) — one Oops saturates to
    // guaranteed, so luck is binary, not a dial. The field is gone by construction,
    // not inherited-then-forbidden.
}

public struct BloodstoneTriggerFilterDesc(BloodstoneTriggerClause clause)
    : IMotelySeedFilterDesc<BloodstoneTriggerFilterDesc.BloodstoneTriggerFilter>,
      IJamlClauseDesc<BloodstoneTriggerClause>
{
    private readonly BloodstoneTriggerClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["bloodstoneTrigger"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label"];

    /// <inheritdoc/>
    public static bool Set(BloodstoneTriggerClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static double EstimateRarity(BloodstoneTriggerClause clause, in JamlRarityContext ctx) =>
        JamlRollRarity.Window(clause, JamlRollRarity.Rate(MotelyGlobals.JokerBloodstoneChance));

    public BloodstoneTriggerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        // Sort the requested roll indices ONCE here, never in the SIMD hot path below.
        Debug.Assert(
            _clause.Rolls.Length > 0,
            "Bloodstone clause must provide at least one roll index."
        );
        int[] sortedRolls = [.. _clause.Rolls];
        Array.Sort(sortedRolls);
        return new BloodstoneTriggerFilter(sortedRolls, _clause.Min);
    }

    public struct BloodstoneTriggerFilter(int[] sortedRolls, int min) : IMotelySeedFilter
    {
        private readonly int[] _sortedRolls = sortedRolls;
        private readonly int _min = min;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            int[] sorted = _sortedRolls;
            var stream = ctx.CreateBloodstonePrngStream();
            int maxRoll = sorted[^1];
            int min = _min;

            var matchCounts = Vector256<int>.Zero;
            var minVector = Vector256.Create(min);
            int total = sorted.Length;
            int p = 0,
                seen = 0;

            for (int idx = 0; idx <= maxRoll; idx++)
            {
                VectorMask trigger = ctx.GetNextBloodstoneTrigger(ref stream);

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
