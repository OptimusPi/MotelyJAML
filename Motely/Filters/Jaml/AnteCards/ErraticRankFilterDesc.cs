using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("erraticRank", "erraticRanks",
    ValueEnum = typeof(MotelyStandardcardRank))]
public sealed class ErraticRankClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyStandardcardRank Rank { get; set; }
}

public struct ErraticRankFilterDesc(ErraticRankClause clause)
    : IMotelySeedFilterDesc<ErraticRankFilterDesc.ErraticRankFilter>,
      IJamlClauseDesc<ErraticRankClause>
{
    private readonly ErraticRankClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["erraticRank", "erraticRanks"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes"];

    /// <inheritdoc/>
    public static bool Set(ErraticRankClause clause, string key, IJamlValueReader value)
    {
        return false;
    }

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(ErraticRankClause clause, IJamlValueReader value)
    {
        if (!value.TryEnum<MotelyStandardcardRank>(out var rank)) return false;
        clause.Rank = rank;
        return true;
    }

    /// <summary>
    /// The erratic deck is 52 independent uniform draws of the 52 playing cards, so a rank's count
    /// is <c>Binomial(52, 4/52)</c> — with replacement, and antes play no part.
    /// </summary>
    public static double EstimateRarity(ErraticRankClause clause, in JamlRarityContext ctx)
    {
        int deck = MotelyEnum<MotelyStandardCard>.ValueCount;
        int ofRank = 0;
        foreach (var card in MotelyEnum<MotelyStandardCard>.Values)
            if (new MotelyItem(card).StandardcardRank == clause.Rank)
                ofRank++;

        return JamlCountDistribution.Window(
            JamlCountDistribution.Binomial(deck, ofRank / (double)deck),
            clause.Min,
            clause.Max
        );
    }

    public ErraticRankFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        ctx.CacheErraticDeckPrngStream();
        return new ErraticRankFilter(_clause);
    }

    public struct ErraticRankFilter(ErraticRankClause clause) : IMotelySeedFilter
    {
        private readonly ErraticRankClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;
            Vector256<int> count = Vector256<int>.Zero;
            var minVec = Vector256.Create(clause.Min);
            var stream = ctx.CreateErraticDeckPrngStream(true);
            for (int cardIndex = 0; cardIndex < 52; cardIndex++)
            {
                var card = ctx.GetNextErraticDeckCard(ref stream);
                count += Vector256.ConditionalSelect(
                    VectorEnum256.Equals(card.StandardcardRank, clause.Rank),
                    Vector256<int>.One,
                    Vector256<int>.Zero
                );
                if (
                    Vector256.GreaterThanOrEqual(count, minVec).ExtractMostSignificantBits() == 0xFF
                )
                    break;
            }
            return Vector256.GreaterThanOrEqual(count, minVec);
        }
    }
}
