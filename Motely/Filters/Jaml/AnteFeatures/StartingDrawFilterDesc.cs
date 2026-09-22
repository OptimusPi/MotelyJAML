using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("startingDraw")]
public sealed class StartingDrawClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
}

public struct StartingDrawFilterDesc(StartingDrawClause clause)
    : IMotelySeedFilterDesc<StartingDrawFilterDesc.StartingDrawFilter>,
      IJamlClauseDesc<StartingDrawClause>
{
    private readonly StartingDrawClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["startingDraw"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "rank", "suit"];

    /// <summary>startingDraw carries its rank and suit as keys, not as a discriminator value.</summary>
    public static bool Set(StartingDrawClause clause, string key, IJamlValueReader value)
    {
        switch (key.ToLowerInvariant())
        {
            case "rank":
                if (!value.TryRank(out var rank))
                    return false;
                clause.Rank = rank;
                return true;
            case "suit":
                if (!value.TryEnum<MotelyStandardcardSuit>(out var suit))
                    return false;
                clause.Suit = suit;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Each ante deals the top eight of a freshly shuffled standard 52-card deck — the scorer
    /// builds that deck itself, whatever deck the run is on — so the count of matching cards in
    /// one ante is hypergeometric: eight drawn without replacement from fifty-two holding however
    /// many fit the rank and suit asked for. Antes shuffle independently and convolve.
    /// </summary>
    public static double EstimateRarity(StartingDrawClause clause, in JamlRarityContext ctx)
    {
        int deck = MotelyEnum<MotelyStandardCard>.ValueCount;
        int matching = 0;
        foreach (var card in MotelyEnum<MotelyStandardCard>.Values)
        {
            var item = new MotelyItem(card);
            if (clause.Rank.HasValue && item.StandardcardRank != clause.Rank.Value)
                continue;
            if (clause.Suit.HasValue && item.StandardcardSuit != clause.Suit.Value)
                continue;
            matching++;
        }

        const int HandSize = 8; // CountStartingDrawOccurrences: Math.Min(8, deck.Length)
        double[] hand = JamlCountDistribution.Hypergeometric(deck, matching, Math.Min(HandSize, deck));

        double[] pmf = JamlCountDistribution.Zero;
        foreach (int _ in clause.Antes)
            pmf = JamlCountDistribution.Convolve(pmf, hand);

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    public StartingDrawFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        return new StartingDrawFilter(_clause);
    }

    public struct StartingDrawFilter(StartingDrawClause clause) : IMotelySeedFilter
    {
        private readonly StartingDrawClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // Single match core: same CountStartingDrawOccurrences as should-scoring.
            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}
