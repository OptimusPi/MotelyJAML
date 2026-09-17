using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("erraticRank", "erraticRanks",
    ValueEnum = typeof(MotelyStandardcardRank))]
[YamlObject]
public sealed partial class ErraticRankClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
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
