namespace Motely.Filters;

/// <summary>YAML-facing properties on filter clause types (Nat's YAML shorthand keys).</summary>
public sealed partial class JokerClause
{
    public MotelyJoker[]? Joker { get => Jokers; set => Jokers = value ?? []; }
}

public sealed partial class CommonJokerClause
{
    public MotelyJokerCommon[]? CommonJoker { get => Jokers; set => Jokers = value ?? []; }
    public MotelyJokerCommon[]? CommonJokers { get => Jokers; set => Jokers = value ?? []; }
}

public sealed partial class UncommonJokerClause
{
    public MotelyJokerUncommon[]? UncommonJoker { get => Jokers; set => Jokers = value ?? []; }
    public MotelyJokerUncommon[]? UncommonJokers { get => Jokers; set => Jokers = value ?? []; }
}

public sealed partial class RareJokerClause
{
    public MotelyJokerRare[]? RareJoker { get => Jokers; set => Jokers = value ?? []; }
    public MotelyJokerRare[]? RareJokers { get => Jokers; set => Jokers = value ?? []; }
}

public sealed partial class LegendaryJokerClause
{
    public MotelyJoker[]? LegendaryJoker { get => Jokers; set => Jokers = value ?? []; }
    public MotelyJoker[]? LegendaryJokers { get => Jokers; set => Jokers = value ?? []; }
}

public sealed partial class LegendarySoulOrderClause
{
    public SoulOrderEntry[]? SoulOrder
    {
        get => Entries;
        set => Entries = RequireSoulOrder(value);
    }

    public SoulOrderEntry[]? SoulOrders
    {
        get => Entries;
        set => Entries = RequireSoulOrder(value);
    }

    // The plain-loader rewrite dropped JamlValidator, whose CheckSoulOrder rejected a soulOrder
    // list that can never match; keep that guard at the binding point, where the YAML value lands.
    private static SoulOrderEntry[] RequireSoulOrder(SoulOrderEntry[]? entries)
    {
        entries ??= [];
        if (entries.Length == 0)
            throw new InvalidOperationException("soulOrder needs at least one soul.");

        for (int i = 0; i < entries.Length; i++)
        {
            var legendary = entries[i].Legendary;
            if (
                ((MotelyJokerRarity)((int)legendary & MotelyGlobals.JokerRarityMask))
                != MotelyJokerRarity.Legendary
            )
                throw new InvalidOperationException(
                    $"soulOrder[{i}] names {legendary}, which is not a legendary."
                );
        }

        return entries;
    }
}

public sealed partial class PlanetCardClause
{
    public MotelyPlanetCard[]? PlanetCard { get => Planets; set => Planets = value ?? []; }
    public MotelyPlanetCard[]? PlanetCards { get => Planets; set => Planets = value ?? []; }
}

public sealed partial class SpectralCardClause
{
    public MotelySpectralCard[]? SpectralCard { get => Spectrals; set => Spectrals = value ?? []; }
    public MotelySpectralCard[]? SpectralCards { get => Spectrals; set => Spectrals = value ?? []; }
}

public sealed partial class TarotCardClause
{
    public MotelyTarotCard[]? TarotCard { get => Tarots; set => Tarots = value ?? []; }
    public MotelyTarotCard[]? TarotCards { get => Tarots; set => Tarots = value ?? []; }
}

public sealed partial class StandardCardClause
{
    public YamlStandardCardSlot? StandardCard
    {
        get => null;
        set => ApplyStandardCardSlot(value);
    }

    public YamlStandardCardSlot? StandardCards
    {
        get => null;
        set => ApplyStandardCardSlot(value);
    }

    private void ApplyStandardCardSlot(YamlStandardCardSlot? slot)
    {
        if (slot is null)
            return;
        if (slot.Card is { } card)
            Card = card;
        if (slot.Rank is { } rank)
            Rank = rank;
        if (slot.Suit is { } suit)
            Suit = suit;
        if (slot.Enhancement is { } enhancement)
            Enhancement = enhancement;
        if (slot.Seal is { } seal)
            Seal = seal;
        if (slot.Edition is { } edition)
            Edition = edition;
    }
}

public sealed partial class ErraticRankClause
{
    public MotelyStandardcardRank? ErraticRank
    {
        get => Rank;
        set => Rank = value ?? default;
    }

    public MotelyStandardcardRank? ErraticRanks
    {
        get => Rank;
        set => Rank = value ?? default;
    }
}

public sealed partial class ErraticSuitClause
{
    public MotelyStandardcardSuit? ErraticSuit
    {
        get => Suit;
        set => Suit = value ?? default;
    }

    public MotelyStandardcardSuit? ErraticSuits
    {
        get => Suit;
        set => Suit = value ?? default;
    }
}

public sealed partial class VoucherClause
{
    public MotelyVoucher[]? Voucher { get => Vouchers; set => Vouchers = value ?? []; }
}

public partial class TagClause
{
    public MotelyTag[]? Tag { get => Tags; set => Tags = value ?? []; }
}

public sealed partial class SmallBlindTagClause
{
    public MotelyTag[]? SmallBlindTag { get => Tags; set => Tags = value ?? []; }
}

public sealed partial class BigBlindTagClause
{
    public MotelyTag[]? BigBlindTag { get => Tags; set => Tags = value ?? []; }
}

public sealed partial class BossClause
{
    public MotelyBossBlind[]? Boss { get => Bosses; set => Bosses = value ?? []; }
}

public sealed partial class BoosterPackClause
{
    public MotelyBoosterPack[]? BoosterPack { get => Packs; set => Packs = value ?? []; }
    public MotelyBoosterPack[]? BoosterPacks { get => Packs; set => Packs = value ?? []; }
}

public sealed partial class PokerHandClause
{
    public MotelyPokerHand[]? PokerHand { get => PokerHands; set => PokerHands = value ?? []; }
}

public sealed partial class StartingDrawClause
{
    public MotelyStandardCard? StartingDraw
    {
        get => Card;
        set => Card = value;
    }
}

public sealed partial class LuckyMoneyClause
{
    public YamlEventRolls LuckyMoney
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class LuckyMultClause
{
    public YamlEventRolls LuckyMult
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class WheelOfFortuneClause
{
    public YamlEventRolls WheelOfFortune
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class WheelStaysFlippedClause
{
    public YamlEventRolls WheelStaysFlipped
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class GlassDestroyClause
{
    public YamlEventRolls GlassDestroy
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class CavendishExtinctClause
{
    public YamlEventRolls CavendishExtinct
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class GrosMichelExtinctClause
{
    public YamlEventRolls GrosMichelExtinct
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class SpaceLevelupClause
{
    public YamlEventRolls SpaceLevelup
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class BusinessPayoutClause
{
    public YamlEventRolls BusinessPayout
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class ParkingPayoutClause
{
    public YamlEventRolls ParkingPayout
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class BloodstoneTriggerClause
{
    public YamlEventRolls BloodstoneTrigger
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class MisprintMultClause
{
    public YamlEventRolls MisprintMult
    {
        get => new() { Rolls = Rolls };
        set => Rolls = value.ToArray();
    }
}

public sealed partial class AndClause
{
    public AndPayload And
    {
        get => new() { Mode = Mode, Clauses = Clauses, Label = Label, Min = Min, Max = Max, Score = Score };
        set
        {
            Mode = value.Mode;
            Clauses = value.Clauses;
            Label = value.Label;
            Min = value.Min;
            Max = value.Max;
            Score = value.Score;
        }
    }
}

public sealed partial class OrClause
{
    public OrPayload Or
    {
        get => new() { Mode = Mode, Clauses = Clauses, Label = Label, Min = Min, Max = Max, Score = Score };
        set
        {
            Mode = value.Mode;
            Clauses = value.Clauses;
            Label = value.Label;
            Min = value.Min;
            Max = value.Max;
            Score = value.Score;
        }
    }
}
