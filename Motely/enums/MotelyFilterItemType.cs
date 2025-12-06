namespace Motely;

/// <summary>
/// Types of items that can be filtered in searches
/// </summary>
public enum MotelyFilterItemType
{
    Joker,
    SoulJoker,
    TarotCard,
    PlanetCard,
    SpectralCard,
    SmallBlindTag,
    BigBlindTag,
    Voucher,
    PlayingCard,
    Boss,
    Event, // Random events (Lucky, Wheel of Fortune, Bananas, Misprint)
    ErraticRank, // Erratic Deck starting composition - rank filter
    ErraticSuit, // Erratic Deck starting composition - suit filter
    And, // Logical AND - all nested clauses must match
    Or, // Logical OR - at least one nested clause must match
}
