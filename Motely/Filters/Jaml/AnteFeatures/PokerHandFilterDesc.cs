using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

[JamlDiscriminator(
    "pokerHand",
    "pokerHands",
    ValueEnum = typeof(MotelyPokerHand),
    RollsDefault = new[] { 0 }
)]
[YamlObject]
public sealed partial class PokerHandClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyPokerHand[] PokerHands { get; set; } = [];

    public int[] Rolls { get; set; } = [0];
}

public struct PokerHandFilterDesc(PokerHandClause clause)
    : IMotelySeedFilterDesc<PokerHandFilterDesc.PokerHandFilter>
{
    private readonly PokerHandClause _clause = clause;

    public static string[] Discriminators => ["pokerHand", "pokerHands"];

    public static string[] ClauseKeys =>
        ["min", "max", "score", "label", "ante", "antes", "rolls"];

    public const int BlindsPerAntePass = 3;

    public const int MaxBlindsPerAnte = BlindsPerAntePass * (1 + AnteReductionVouchers);

    public const int AnteReductionVouchers = 2;

    public PokerHandFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
        new PokerHandFilter(_clause);

    public struct PokerHandFilter(PokerHandClause clause) : IMotelySeedFilter
    {
        private readonly PokerHandClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}
