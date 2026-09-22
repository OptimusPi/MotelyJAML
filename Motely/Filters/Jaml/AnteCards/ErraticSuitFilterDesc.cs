using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("erraticSuit", "erraticSuits",
    ValueEnum = typeof(MotelyStandardcardSuit))]
public sealed class ErraticSuitClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyStandardcardSuit Suit { get; set; }
}

public struct ErraticSuitFilterDesc(ErraticSuitClause clause)
    : IMotelySeedFilterDesc<ErraticSuitFilterDesc.ErraticSuitFilter>,
      IJamlClauseDesc<ErraticSuitClause>
{
    private readonly ErraticSuitClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["erraticSuit", "erraticSuits"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes"];

    /// <summary>Erratic-suit clauses carry no keys beyond the common set.</summary>
    public static bool Set(ErraticSuitClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(ErraticSuitClause clause, IJamlValueReader value)
    {
        if (!value.TryEnum<MotelyStandardcardSuit>(out var suit))
            return false;
        clause.Suit = suit;
        return true;
    }

    /// <summary>
    /// The erratic deck is 52 independent uniform draws of the 52 playing cards, so a suit's count
    /// is <c>Binomial(52, 13/52)</c> — with replacement, and antes play no part.
    /// </summary>
    public static double EstimateRarity(ErraticSuitClause clause, in JamlRarityContext ctx)
    {
        int deck = MotelyEnum<MotelyStandardCard>.ValueCount;
        int ofSuit = 0;
        foreach (var card in MotelyEnum<MotelyStandardCard>.Values)
            if (new MotelyItem(card).StandardcardSuit == clause.Suit)
                ofSuit++;

        return JamlCountDistribution.Window(
            JamlCountDistribution.Binomial(deck, ofSuit / (double)deck),
            clause.Min,
            clause.Max
        );
    }

    public ErraticSuitFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        ctx.CacheErraticDeckPrngStream();
        return new ErraticSuitFilter(_clause);
    }

    public struct ErraticSuitFilter(ErraticSuitClause clause) : IMotelySeedFilter
    {
        private readonly ErraticSuitClause _clause = clause;

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
                    VectorEnum256.Equals(card.StandardcardSuit, clause.Suit),
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
