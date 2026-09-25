using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("tarotCard", "tarotCards",
    ValueEnum = typeof(MotelyTarotCard), SourceConfigType = typeof(TarotCardSourceConfig))]
[YamlObject]
public sealed partial class TarotCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyTarotCard[] Tarots { get; set; } = [];

    public TarotCardSourceConfig? Sources { get; set; }
}

public struct TarotCardFilterDesc(TarotCardClause clause)
    : IMotelySeedFilterDesc<TarotCardFilterDesc.TarotCardFilter>
{
    private readonly TarotCardClause _clause = clause;

    public static string[] Discriminators => ["tarotCard", "tarotCards"];

    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources"];

    internal static readonly TarotCardSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

    public TarotCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var sources = _clause.Sources ?? DefaultSources;

        foreach (var ante in _clause.Antes)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
        }

        int maxShopItem = 0;
        for (int i = 0; i < sources.ShopItems.Length; i++)
        {
            if (sources.ShopItems[i] > maxShopItem)
                maxShopItem = sources.ShopItems[i];
        }

        int maxBoosterPack = 0;
        for (int i = 0; i < sources.BoosterPacks.Length; i++)
        {
            if (sources.BoosterPacks[i] > maxBoosterPack)
                maxBoosterPack = sources.BoosterPacks[i];
        }

        int maxEmperor = 0;
        for (int i = 0; i < sources.Emperor.Length; i++)
        {
            if (sources.Emperor[i] > maxEmperor)
                maxEmperor = sources.Emperor[i];
        }

        int maxPurpleSeal = 0;
        for (int i = 0; i < sources.PurpleSealOrEightBall.Length; i++)
        {
            if (sources.PurpleSealOrEightBall[i] > maxPurpleSeal)
                maxPurpleSeal = sources.PurpleSealOrEightBall[i];
        }

        return new TarotCardFilter(_clause, maxShopItem, maxBoosterPack, maxEmperor, maxPurpleSeal);
    }

    public struct TarotCardFilter(
        TarotCardClause clause,
        int maxShopItem,
        int maxBoosterPack,
        int maxEmperor,
        int maxPurpleSeal
    ) : IMotelySeedFilter
    {
        private readonly TarotCardClause _clause = clause;
        private readonly int _maxShopItem = maxShopItem;
        private readonly int _maxBoosterPack = maxBoosterPack;
        private readonly int _maxEmperor = maxEmperor;
        private readonly int _maxPurpleSeal = maxPurpleSeal;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;
            int maxShopItem = _maxShopItem;
            int maxBoosterPack = _maxBoosterPack;
            int maxEmperor = _maxEmperor;
            int maxPurpleSeal = _maxPurpleSeal;
            int needed = clause.Min;
            Debug.Assert(needed > 0, "TarotCardClause.Min must be > 0 — loader bug.");

            Vector256<int> matchCounts = Vector256<int>.Zero;
            var sources = clause.Sources ?? DefaultSources;

            if (sources.CharmTag)
            {
                return ctx.SearchIndividualSeeds(
                    (MotelySingleSearchContext single) =>
                        JamlScoring.ClauseMeetsMinForFilter(ref single, clause) ? 1 : 0
                );
            }

            var shopIndices = sources.ShopItems;
            var boosterPacks = sources.BoosterPacks;
            var emperorRolls = sources.Emperor;
            var sealRolls = sources.PurpleSealOrEightBall;

            VectorMask ante1Extended = VectorMask.NoBitsSet;
            if (boosterPacks.Length > 0 && JamlSimdPackSupport.NeedsAnte1Extension(maxBoosterPack))
            {
                bool hasAnte1 = false;
                for (int i = 0; i < clause.Antes.Length; i++)
                    if (clause.Antes[i] == 1)
                    {
                        hasAnte1 = true;
                        break;
                    }
                if (hasAnte1)
                    ante1Extended = JamlSimdPackSupport.Ante1PackExtensionMask(ref ctx);
            }

            foreach (var ante in clause.Antes)
            {
                if (shopIndices.Length > 0)
                {
                    var shopStream = ctx.CreateShopItemStream(ante);

                    for (int slot = 0; slot <= maxShopItem; slot++)
                    {
                        var item = ctx.GetNextShopItem(ref shopStream);
                        bool isTarget = false;
                        for (int i = 0; i < shopIndices.Length; i++)
                        {
                            if (shopIndices[i] == slot)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (!isTarget)
                            continue;

                        VectorMask isTarot = VectorEnum256.Equals(
                            item.TypeCategory,
                            MotelyItemTypeCategory.TarotCard
                        );
                        VectorMask match = MatchTarots(item, clause) & isTarot;

                        if (match.IsPartiallyTrue())
                        {
                            matchCounts = Vector256.Add(
                                matchCounts,
                                Vector256.ConditionalSelect(
                                    MotelyVectorUtils.VectorMaskToConditionalSelectMask(match),
                                    Vector256.Create(1),
                                    Vector256<int>.Zero
                                )
                            );
                        }
                    }
                }

                if (boosterPacks.Length > 0)
                {
                    var packStream = ctx.CreateBoosterPackStream(ante);
                    var tarotStream = ctx.CreateArcanaPackTarotStream(ante);

                    for (int p = 0; p <= maxBoosterPack; p++)
                    {
                        var pack = ctx.GetNextBoosterPack(ref packStream);
                        bool isTarget = false;
                        for (int i = 0; i < boosterPacks.Length; i++)
                        {
                            if (boosterPacks[i] == p)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        VectorMask reachable = JamlSimdPackSupport.SlotReachableMask(
                            ante,
                            p,
                            ante1Extended
                        );
                        VectorMask countLanes = isTarget
                            ? reachable
                            : VectorMask.NoBitsSet;

                        var packType = pack.GetPackType();
                        VectorMask isArcana = VectorEnum256.Equals(
                            packType,
                            MotelyBoosterPackType.Arcana
                        );
                        if (isArcana.IsAllFalse())
                            continue;

                        if (sources.RequireMegaPack)
                        {
                            countLanes &= VectorEnum256.Equals(
                                pack.GetPackSize(),
                                MotelyBoosterPackSize.Mega
                            );
                        }

                        VectorMask isNormal = VectorEnum256.Equals(
                            pack.GetPackSize(),
                            MotelyBoosterPackSize.Normal
                        );
                        VectorMask baseLanes = isArcana;
                        VectorMask extraLanes = isArcana & ~isNormal;
                        var baseMask = JamlSimdPackSupport.ToPrngMask(baseLanes);
                        var extraMask = JamlSimdPackSupport.ToPrngMask(extraLanes);

                        for (int c = 0; c < 3; c++)
                        {
                            var card = ctx.GetNextTarot(ref tarotStream, baseMask);
                            if (countLanes.IsPartiallyTrue())
                                JamlSimdPackSupport.AddMatchCounts(
                                    MatchTarots(card, clause) & countLanes & baseLanes,
                                    ref matchCounts
                                );
                        }

                        if (extraLanes.IsPartiallyTrue())
                        {
                            for (int c = 0; c < 2; c++)
                            {
                                var card = ctx.GetNextTarot(ref tarotStream, extraMask);
                                if (countLanes.IsPartiallyTrue())
                                    JamlSimdPackSupport.AddMatchCounts(
                                        MatchTarots(card, clause) & countLanes & extraLanes,
                                        ref matchCounts
                                    );
                            }
                        }
                    }
                }

                if (emperorRolls.Length > 0)
                {
                    var emperorStream = ctx.CreateEmperorTarotStream(ante);

                    for (int roll = 0; roll <= maxEmperor; roll++)
                    {
                        var tarots = ctx.GetNextEmperorTarots(ref emperorStream);
                        bool isTarget = false;
                        for (int i = 0; i < emperorRolls.Length; i++)
                        {
                            if (emperorRolls[i] == roll)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (isTarget)
                        {
                            VectorMask match1 = MatchTarots(tarots[0], clause);
                            VectorMask match2 = MatchTarots(tarots[1], clause);

                            if (match1.IsPartiallyTrue())
                                matchCounts = Vector256.Add(
                                    matchCounts,
                                    Vector256.ConditionalSelect(
                                        MotelyVectorUtils.VectorMaskToConditionalSelectMask(match1),
                                        Vector256.Create(1),
                                        Vector256<int>.Zero
                                    )
                                );
                            if (match2.IsPartiallyTrue())
                                matchCounts = Vector256.Add(
                                    matchCounts,
                                    Vector256.ConditionalSelect(
                                        MotelyVectorUtils.VectorMaskToConditionalSelectMask(match2),
                                        Vector256.Create(1),
                                        Vector256<int>.Zero
                                    )
                                );
                        }
                    }
                }

                if (sealRolls.Length > 0)
                {
                    var purpleSealStream = ctx.CreatePurpleSealTarotStream(ante);

                    for (int roll = 0; roll <= maxPurpleSeal; roll++)
                    {
                        var item = ctx.GetNextTarot(ref purpleSealStream);
                        bool isTarget = false;
                        for (int i = 0; i < sealRolls.Length; i++)
                        {
                            if (sealRolls[i] == roll)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (isTarget)
                        {
                            VectorMask match = MatchTarots(item, clause);
                            if (match.IsPartiallyTrue())
                            {
                                matchCounts = Vector256.Add(
                                    matchCounts,
                                    Vector256.ConditionalSelect(
                                        MotelyVectorUtils.VectorMaskToConditionalSelectMask(match),
                                        Vector256.Create(1),
                                        Vector256<int>.Zero
                                    )
                                );
                            }
                        }
                    }
                }
            }

            return JamlSimdPackSupport.MeetsMinMaxMask(matchCounts, needed, clause.Max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static VectorMask MatchTarots(MotelyItemVector items, TarotCardClause clause)
        {
            var tarots = JamlDisc.OrEmpty(clause.Tarots);
            if (JamlDisc.IsCategoryAny(tarots))
                return VectorEnum256.Equals(items.TypeCategory, MotelyItemTypeCategory.TarotCard);

            VectorMask mask = VectorMask.NoBitsSet;
            var itemTypes = items.Type;

            for (int i = 0; i < tarots.Length; i++)
            {
                var targetType = (int)MotelyItemTypeCategory.TarotCard | (int)tarots[i];
                mask |= VectorEnum256.Equals(itemTypes, (MotelyItemType)targetType);
            }

            return mask;
        }
    }
}

[YamlObject]
public sealed partial record TarotCardSourceConfig
{
    public static readonly string[] SourceKeys =
    [
        "shopItems",
        "boosterPacks",
        "emperor",
        "purpleSealOrEightBall",
        "charmTag",
        "requireMega",
        "requireMegaPack",
    ];

    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];
    public int[] Emperor { get; set; } = [];
    public int[] PurpleSealOrEightBall { get; set; } = [];

    public bool CharmTag { get; set; }

    public bool RequireMegaPack { get; set; }
}
