using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("cavendishExtinct", RollsAreInlineValue = true)]
public sealed class CavendishExtinctClause : IRollScopedClause, IWithScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Rolls { get; set; } = [];
    public JamlWith With { get; set; } = new();
}

public struct CavendishExtinctFilterDesc(CavendishExtinctClause clause)
    : IMotelySeedFilterDesc<CavendishExtinctFilterDesc.CavendishExtinctFilter>,
      IJamlClauseDesc<CavendishExtinctClause>
{
    private readonly CavendishExtinctClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["cavendishExtinct"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "with"];

    /// <inheritdoc/>
    public static bool Set(CavendishExtinctClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static double EstimateRarity(CavendishExtinctClause clause, in JamlRarityContext ctx) =>
        JamlRollRarity.Window(
            clause,
            JamlRollRarity.Rate(MotelyGlobals.JokerCavendishChance, (double)clause.With.Luck)
        );

    public CavendishExtinctFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        Debug.Assert(
            _clause.Rolls.Length > 0,
            "Cavendish clause must provide at least one roll index."
        );
        int[] sortedRolls = [.. _clause.Rolls];
        Array.Sort(sortedRolls);
        return new CavendishExtinctFilter(sortedRolls, _clause.Min, (double)_clause.With.Luck);
    }

    public struct CavendishExtinctFilter(int[] sortedRolls, int min, double luck)
        : IMotelySeedFilter
    {
        private readonly int[] _sortedRolls = sortedRolls;
        private readonly int _min = min;
        private readonly double _luck = luck;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            int[] sorted = _sortedRolls;
            var stream = ctx.CreateCavendishPrngStream(false);
            int maxRoll = sorted[^1];
            int min = _min;
            double luck = _luck;

            var matchCounts = Vector256<int>.Zero;
            var minVector = Vector256.Create(min);
            int total = sorted.Length;
            int p = 0,
                seen = 0;

            for (int idx = 0; idx <= maxRoll; idx++)
            {
                VectorMask trigger = ctx.GetNextCavendishExtinct(ref stream, luck);

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
