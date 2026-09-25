using Motely.Analysis;
using Motely.Enums;

namespace Motely.JsonRender;

public sealed record RenderReport(
    RenderFilter Filter,
    MotelyDeck Deck,
    MotelyStake Stake,
    int EventRolls,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RenderSeed> Seeds
);

public sealed record RenderFilter(string Id, string? Name, string? Description, string? Author);

public sealed record RenderSeed(
    string Seed,
    int Score,
    IReadOnlyList<RenderAnte> Antes,
    MotelyJamlyzerEvents Events,
    MotelyJamlyzerStreamStates StreamStates,
    IReadOnlyList<RenderItem>? ErraticDeck
);

public sealed record RenderAnte(
    int Ante,
    MotelyBossBlind Boss,
    MotelyVoucher Voucher,
    MotelyTag SmallBlindTag,
    MotelyTag BigBlindTag,
    IReadOnlyList<RenderItem> ShopItems,
    IReadOnlyList<RenderPack> Packs,
    RenderPulls Pulls,
    RenderShopStreams ShopStreams
);

public sealed record RenderPack(MotelyBoosterPack Pack, IReadOnlyList<RenderItem> Items);

public sealed record RenderItem(
    string Name,
    MotelyItemType Type,
    MotelyItemTypeCategory TypeCategory,
    MotelyItemEdition Edition,
    MotelyItemEnhancement Enhancement,
    MotelyItemSeal Seal,
    IReadOnlyList<string> Stickers,
    string? Suit,
    string? Rank
);

public sealed record RenderPulls(
    IReadOnlyList<RenderItem> JudgementJokers,
    IReadOnlyList<RenderItem> WraithJokers,
    IReadOnlyList<RenderItem> EmperorTarots,
    IReadOnlyList<RenderItem> PurpleSealTarots,
    IReadOnlyList<RenderItem> SixthSenseSpectrals,
    IReadOnlyList<RenderItem> SeanceSpectrals,
    IReadOnlyList<RenderItem> RiffRaffJokers,
    IReadOnlyList<RenderItem> RareTagJokers,
    IReadOnlyList<RenderItem> UncommonTagJokers,
    IReadOnlyList<RenderItem> LegendaryJokers,
    IReadOnlyList<MotelyVoucher> VoucherSequence
);

public sealed record RenderShopStreams(
    IReadOnlyList<RenderItem> ShopJokers,
    IReadOnlyList<RenderItem> CommonShopJokers,
    IReadOnlyList<RenderItem> UncommonShopJokers,
    IReadOnlyList<RenderItem> RareShopJokers,
    IReadOnlyList<RenderItem> ShopTarots,
    IReadOnlyList<RenderItem> ShopPlanets,
    IReadOnlyList<RenderItem> ShopSpectrals
);
