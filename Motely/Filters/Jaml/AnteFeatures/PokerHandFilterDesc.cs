using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

/// <summary>
/// Starting-hand poker category for a given ante/round (deck shuffle → last 8 cards → best hand).
/// Scalar-per-seed via <see cref="MotelyVectorSearchContext.SearchIndividualSeeds"/> — same shape as
/// <c>startingDraw</c> / native ShuffleFinder.
/// </summary>
[JamlDiscriminator(
    "pokerHand",
    "pokerHands",
    ValueEnum = typeof(MotelyPokerHand),
    RollsDefault = new[] { 0 }
)]
public sealed class PokerHandClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyPokerHand[] PokerHands { get; set; } = [];

    /// <summary>
    /// Which blinds of the ante to look at, as advances into that ante's single <c>nr{ante}</c>
    /// shuffle stream: 0 = first blind played that ante, 1 = second, 2 = third.
    /// Because Lua only shuffles on a blind that is actually played
    /// (<c>DRAW_TO_HAND</c>, state_events.lua:344), these are *blinds played*, not
    /// Small/Big/Boss positions — skipping Small makes Big the 0th draw.
    /// Defaults to <c>[0]</c>, which is the behaviour every pre-<c>rolls</c> config had.
    /// </summary>
    public int[] Rolls { get; set; } = [0];
}

public struct PokerHandFilterDesc(PokerHandClause clause)
    : IMotelySeedFilterDesc<PokerHandFilterDesc.PokerHandFilter>,
      IJamlClauseDesc<PokerHandClause>
{
    private readonly PokerHandClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["pokerHand", "pokerHands"];

    /// <inheritdoc/>
    public static string[] ClauseKeys =>
        ["min", "max", "score", "label", "ante", "antes", "rolls"];

    /// <summary>Small, Big, Boss — blinds in one pass through an ante.</summary>
    public const int BlindsPerAntePass = 3;

    /// <summary>
    /// Ceiling on <see cref="PokerHandClause.Rolls"/>: the most times one <c>nr{ante}</c> stream
    /// can be advanced in a run.
    ///
    /// The key is the ante counter, not the round (<c>'nr'..G.GAME.round_resets.ante</c>,
    /// state_events.lua:344), and Hieroglyph and Petroglyph each call <c>ease_ante(-1)</c>
    /// (card.lua:1958). Every reduction puts the counter back on a value it already sat on, so
    /// that value's stream keeps advancing through another pass of blinds — the key is revisited,
    /// not skipped. Two reduction vouchers, three blinds per pass:
    /// <c>3 * (1 + 2) = 9</c>.
    /// </summary>
    public const int MaxBlindsPerAnte = BlindsPerAntePass * (1 + AnteReductionVouchers);

    /// <summary>Hieroglyph and Petroglyph — the vouchers that call <c>ease_ante(-1)</c>.</summary>
    public const int AnteReductionVouchers = 2;

    /// <inheritdoc/>
    public static bool Set(PokerHandClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(PokerHandClause clause, IJamlValueReader value)
    {
        if (!value.TryEnumArray<MotelyPokerHand>(out var hands))
            return false;
        clause.PokerHands = hands;
        return true;
    }

    public PokerHandFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
        new PokerHandFilter(_clause);

    public struct PokerHandFilter(PokerHandClause clause) : IMotelySeedFilter
    {
        private readonly PokerHandClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // Empty PokerHands = any hand, same convention as BoosterPackClause.Packs /
            // JokerClause.Jokers. Every 8-card draw has some best hand, so "any" always matches.
            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}
