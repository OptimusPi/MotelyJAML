using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("misprintMult", RollsAreInlineValue = true)]
[YamlObject]
public sealed partial class MisprintMultClause : IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Rolls { get; set; } = [];

    /// <summary>
    /// Minimum Mult to hit for the filter to succeed each roll.
    /// </summary>
    public int Mult { get; set; }
}

public struct MisprintMultFilterDesc(MisprintMultClause clause)
    : IMotelySeedFilterDesc<MisprintMultFilterDesc.MisprintMultFilter>
{
    private readonly MisprintMultClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["misprintMult"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "mult", "value"];

    public MisprintMultFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        Debug.Assert(
            _clause.Rolls.Length > 0,
            "Misprint clause must provide at least one roll index."
        );
        int[] sortedRolls = [.. _clause.Rolls];
        Array.Sort(sortedRolls);
        // Broadcast the threshold to all 8 lanes ONCE here, never per-roll in the hot path.
        Vector256<int> minMult = Vector256.Create(_clause.Mult);
        return new MisprintMultFilter(sortedRolls, _clause.Min, minMult);
    }

    public struct MisprintMultFilter(int[] sortedRolls, int min, Vector256<int> minMult)
        : IMotelySeedFilter
    {
        private readonly int[] _sortedRolls = sortedRolls;
        private readonly int _min = min;
        private readonly Vector256<int> _minMult = minMult;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            int[] sorted = _sortedRolls;
            var stream = ctx.CreateMisprintPrngStream();
            int maxRoll = sorted[^1];
            int min = _min;
            Vector256<int> minMult = _minMult;

            var matchCounts = Vector256<int>.Zero;
            var minVector = Vector256.Create(min);
            int total = sorted.Length;
            int p = 0,
                seen = 0;

            for (int idx = 0; idx <= maxRoll; idx++)
            {
                // The roll yields an int mult (0–23). Matched = it meets the minimum Mult threshold.
                Vector256<int> mult = ctx.GetNextMisprintMult(ref stream);
                VectorMask trigger = new VectorMask(
                    MotelyVectorUtils.VectorizedComparisonToMask(
                        Vector256.GreaterThanOrEqual(mult, minMult)
                    )
                );

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
