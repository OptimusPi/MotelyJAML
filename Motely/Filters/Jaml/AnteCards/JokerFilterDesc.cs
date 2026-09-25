using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("joker", "jokers",
    ValueEnum = typeof(MotelyJoker), SourceConfigType = typeof(JokerSourceConfig))]
[YamlObject]
public sealed partial class JokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyJoker[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }

    public LegendaryJokerSourceConfig? LegendarySources { get; set; }
}

public struct JokerFilterDesc(JokerClause clause)
    : IMotelySeedFilterDesc<JokerFilterDesc.JokerFilter>
{
    private readonly JokerClause _clause = clause;

    public static string[] Discriminators => ["joker", "jokers"];

    public static string[] ClauseKeys =>
        ["min", "max", "score", "label", "ante", "antes", "sources", "edition", "stickers"];

    internal static readonly JokerSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

    public JokerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
        }

        var jokers = JamlDisc.OrEmpty(_clause.Jokers);
        var targetTypes = new MotelyItemType[jokers.Length];
        for (int i = 0; i < jokers.Length; i++)
        {
            if (Enum.TryParse(jokers[i].ToString(), out MotelyItemType type))
            {
                targetTypes[i] = type;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Joker {jokers[i]} not found in MotelyItemType"
                );
            }
        }

        var sources = _clause.Sources ?? DefaultSources;
        var shopIndices = sources.ShopItems;
        var boosterIndices = sources.BoosterPacks;

        bool confirmPerSeed =
            UsesLegendaryPath(_clause)
            || sources.HasSpawnSources
            || sources.HasRawShopJokerSources;

        Debug.Assert(
            confirmPerSeed || shopIndices.Length > 0 || boosterIndices.Length > 0,
            "Joker clause should have non-empty default sources."
        );

        int maxShopItem = 0;
        foreach (var idx in shopIndices)
            if (idx > maxShopItem)
                maxShopItem = idx;

        int maxBoosterPack = 0;
        foreach (var idx in boosterIndices)
            if (idx > maxBoosterPack)
                maxBoosterPack = idx;

        return new JokerFilter(
            _clause,
            targetTypes,
            [.. shopIndices],
            [.. boosterIndices],
            maxShopItem,
            maxBoosterPack,
            sources.RequireMegaPack,
            confirmPerSeed
        );
    }

    private static bool UsesLegendaryPath(JokerClause clause)
    {
        if (JamlDisc.IsCategoryAny(clause.Jokers))
            return true;

        var jokers = clause.Jokers!;
        for (int i = 0; i < jokers.Length; i++)
        {
            if (
                ((MotelyJokerRarity)((int)jokers[i] & MotelyGlobals.JokerRarityMask))
                == MotelyJokerRarity.Legendary
            )
                return true;
        }

        return false;
    }

    public struct JokerFilter(
        JokerClause clause,
        MotelyItemType[] targetTypes,
        int[] shopIndices,
        int[] boosterIndices,
        int maxShopItem,
        int maxBoosterPack,
        bool requireMegaPack,
        bool confirmPerSeed
    ) : IMotelySeedFilter
    {
        private readonly JokerClause _clause = clause;
        private readonly MotelyItemType[] _targetTypes = targetTypes;
        private readonly int[] _shopIndices = shopIndices;
        private readonly int[] _boosterIndices = boosterIndices;
        private readonly int _maxShopItem = maxShopItem;
        private readonly int _maxBoosterPack = maxBoosterPack;
        private readonly bool _requireMegaPack = requireMegaPack;
        private readonly bool _confirmPerSeed = confirmPerSeed;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            int needed = _clause.Min;
            Debug.Assert(needed > 0, "JokerClause.Min must be > 0 — loader bug.");

            if (_confirmPerSeed)
            {
                var clause = _clause;
                return ctx.SearchIndividualSeeds(
                    (MotelySingleSearchContext singleCtx) =>
                        JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
                );
            }

            Vector256<int> matchCounts = Vector256<int>.Zero;

            var shopIndices = _shopIndices;
            var boosterIndices = _boosterIndices;

            VectorMask ante1Extended = VectorMask.NoBitsSet;
            if (boosterIndices.Length > 0 && JamlSimdPackSupport.NeedsAnte1Extension(_maxBoosterPack))
            {
                bool hasAnte1 = false;
                for (int i = 0; i < _clause.Antes.Length; i++)
                    if (_clause.Antes[i] == 1)
                    {
                        hasAnte1 = true;
                        break;
                    }
                if (hasAnte1)
                    ante1Extended = JamlSimdPackSupport.Ante1PackExtensionMask(ref ctx);
            }

            foreach (var ante in _clause.Antes)
            {
                if (shopIndices.Length > 0)
                {
                    var shopStream = ctx.CreateShopItemStream(ante);

                    for (int slot = 0; slot <= _maxShopItem; slot++)
                    {
                        var shopItem = ctx.GetNextShopItem(ref shopStream);
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

                        VectorMask jokerMatch = MatchJokers(shopItem);
                        if (jokerMatch.IsPartiallyTrue())
                        {
                            matchCounts = Vector256.Add(
                                matchCounts,
                                Vector256.ConditionalSelect(
                                    VectorMaskToConditionalSelectMask(jokerMatch),
                                    Vector256.Create(1),
                                    Vector256<int>.Zero
                                )
                            );
                        }
                    }
                }

                if (boosterIndices.Length > 0)
                {
                    var packStream = ctx.CreateBoosterPackStream(ante);
                    var jokerStream = ctx.CreateBuffoonPackJokerStream(ante);

                    for (int p = 0; p <= _maxBoosterPack; p++)
                    {
                        var pack = ctx.GetNextBoosterPack(ref packStream);
                        bool isTarget = false;
                        for (int i = 0; i < boosterIndices.Length; i++)
                        {
                            if (boosterIndices[i] == p)
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

                        VectorMask isBuffoon = VectorEnum256.Equals(
                            pack.GetPackType(),
                            MotelyBoosterPackType.Buffoon
                        );
                        if (isBuffoon.IsAllFalse())
                            continue;

                        if (_requireMegaPack)
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
                        VectorMask baseLanes = isBuffoon;
                        VectorMask extraLanes = isBuffoon & ~isNormal;
                        var baseMask = JamlSimdPackSupport.ToPrngMask(baseLanes);
                        var extraMask = JamlSimdPackSupport.ToPrngMask(extraLanes);

                        for (int c = 0; c < 2; c++)
                        {
                            var joker = ctx.GetNextJoker(ref jokerStream, baseMask);
                            if (countLanes.IsPartiallyTrue())
                                JamlSimdPackSupport.AddMatchCounts(
                                    MatchJokers(joker) & countLanes & baseLanes,
                                    ref matchCounts
                                );
                        }

                        if (extraLanes.IsPartiallyTrue())
                        {
                            for (int c = 0; c < 2; c++)
                            {
                                var joker = ctx.GetNextJoker(ref jokerStream, extraMask);
                                if (countLanes.IsPartiallyTrue())
                                    JamlSimdPackSupport.AddMatchCounts(
                                        MatchJokers(joker) & countLanes & extraLanes,
                                        ref matchCounts
                                    );
                            }
                        }
                    }
                }
            }

            return JamlSimdPackSupport.MeetsMinMaxMask(matchCounts, _clause.Min, _clause.Max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly VectorMask MatchJokers(in MotelyItemVector item)
        {
            VectorMask jokerMatch = VectorMask.NoBitsSet;
            for (int t = 0; t < _targetTypes.Length; t++)
                jokerMatch |= VectorEnum256.Equals(item.Type, _targetTypes[t]);

            if (_clause.Edition.HasValue)
                jokerMatch &= VectorEnum256.Equals(item.Edition, _clause.Edition.Value);

            for (int s = 0; s < _clause.Stickers.Length; s++)
            {
                switch (_clause.Stickers[s])
                {
                    case MotelyJokerSticker.Eternal:
                        jokerMatch &= item.IsEternal;
                        break;
                    case MotelyJokerSticker.Perishable:
                        jokerMatch &= item.IsPerishable;
                        break;
                    case MotelyJokerSticker.Rental:
                        jokerMatch &= item.IsRental;
                        break;
                }
            }

            return jokerMatch;
        }
    }
}

[JamlDiscriminator("commonJoker", "commonJokers",
    ValueEnum = typeof(MotelyJokerCommon), SourceConfigType = typeof(JokerSourceConfig))]
[YamlObject]
public sealed partial class CommonJokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyJokerCommon[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }
}

[JamlDiscriminator("uncommonJoker", "uncommonJokers",
    ValueEnum = typeof(MotelyJokerUncommon), SourceConfigType = typeof(JokerSourceConfig))]
[YamlObject]
public sealed partial class UncommonJokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyJokerUncommon[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }
}

[JamlDiscriminator("rareJoker", "rareJokers",
    ValueEnum = typeof(MotelyJokerRare), SourceConfigType = typeof(JokerSourceConfig))]
[YamlObject]
public sealed partial class RareJokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyJokerRare[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }
}

[YamlObject]
public sealed partial record JokerSourceConfig
{
    public static readonly string[] SourceKeys =
    [
        "shopItems", "boosterPacks", "judgement", "wraith", "riffRaff", "rareTag", "uncommonTag",
        "commonShopJokers", "uncommonShopJokers", "rareShopJokers", "allShopJokers",
        "requireMega", "requireMegaPack",
    ];

    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];

    public bool RequireMegaPack { get; set; }

    public int[] Judgement { get; set; } = [];

    public int[] Wraith { get; set; } = [];

    public int[] RiffRaff { get; set; } = [];

    public int[] RareTag { get; set; } = [];

    public int[] UncommonTag { get; set; } = [];

    internal bool HasSpawnSources =>
        Judgement.Length > 0
        || Wraith.Length > 0
        || RiffRaff.Length > 0
        || RareTag.Length > 0
        || UncommonTag.Length > 0;

    internal bool HasRawShopJokerSources =>
        CommonShopJokers.Length > 0
        || UncommonShopJokers.Length > 0
        || RareShopJokers.Length > 0
        || AllShopJokers.Length > 0;

    public int[] CommonShopJokers { get; set; } = [];

    public int[] UncommonShopJokers { get; set; } = [];

    public int[] RareShopJokers { get; set; } = [];

    public int[] AllShopJokers { get; set; } = [];
}
