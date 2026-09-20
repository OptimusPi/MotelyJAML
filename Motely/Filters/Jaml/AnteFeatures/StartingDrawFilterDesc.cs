using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("startingDraw")]
[YamlObject]
public sealed partial class StartingDrawClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
}

public struct StartingDrawFilterDesc(StartingDrawClause clause)
    : IMotelySeedFilterDesc<StartingDrawFilterDesc.StartingDrawFilter>
{
    private readonly StartingDrawClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["startingDraw"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "rank", "suit"];

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
