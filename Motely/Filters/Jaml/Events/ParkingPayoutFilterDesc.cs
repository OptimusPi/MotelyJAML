using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("parkingPayout", RollsAreInlineValue = true)]
public sealed class ParkingPayoutClause : IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Rolls { get; set; } = [];
    // No Luck. Reserved Parking is flat 50/50 (Chance = 2) — one Oops saturates to
    // guaranteed, so luck is binary, not a dial. The field is gone by construction,
    // not inherited-then-forbidden.
}

public struct ParkingPayoutFilterDesc(ParkingPayoutClause clause)
    : IMotelySeedFilterDesc<ParkingPayoutFilterDesc.ParkingPayoutFilter>,
      IJamlClauseDesc<ParkingPayoutClause>
{
    private readonly ParkingPayoutClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["parkingPayout"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label"];

    /// <inheritdoc/>
    public static bool Set(ParkingPayoutClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static double EstimateRarity(ParkingPayoutClause clause, in JamlRarityContext ctx) =>
        JamlRollRarity.Window(clause, JamlRollRarity.Rate(MotelyGlobals.JokerParkingChance));

    public ParkingPayoutFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        Debug.Assert(
            _clause.Rolls.Length > 0,
            "Parking clause must provide at least one roll index."
        );
        int[] sortedRolls = [.. _clause.Rolls];
        Array.Sort(sortedRolls);
        return new ParkingPayoutFilter(sortedRolls, _clause.Min);
    }

    public struct ParkingPayoutFilter(int[] sortedRolls, int min) : IMotelySeedFilter
    {
        private readonly int[] _sortedRolls = sortedRolls;
        private readonly int _min = min;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            int[] sorted = _sortedRolls;
            var stream = ctx.CreateParkingPrngStream();
            int maxRoll = sorted[^1];
            int min = _min;

            var matchCounts = Vector256<int>.Zero;
            var minVector = Vector256.Create(min);
            int total = sorted.Length;
            int p = 0,
                seen = 0;

            for (int idx = 0; idx <= maxRoll; idx++)
            {
                VectorMask trigger = ctx.GetNextParkingPayout(ref stream);

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
