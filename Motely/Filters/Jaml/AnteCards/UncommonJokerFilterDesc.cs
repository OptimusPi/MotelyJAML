using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters.Jaml;

public struct UncommonJokerFilterDesc(UncommonJokerClause clause)
    : IMotelySeedFilterDesc<UncommonJokerFilterDesc.UncommonJokerFilter>,
      IJamlClauseDesc<UncommonJokerClause>
{
    private readonly UncommonJokerClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["uncommonJoker", "uncommonJokers"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => JokerFilterDesc.ClauseKeys;

    /// <inheritdoc/>
    public static bool Set(UncommonJokerClause clause, string key, IJamlValueReader value)
    {
        switch (key.ToLowerInvariant())
        {
            case "edition":
                if (!value.TryEnum<MotelyItemEdition>(out var edition)) return false;
                clause.Edition = edition;
                return true;
            case "stickers":
                if (!value.TryEnumArray<MotelyJokerSticker>(out var stickers)) return false;
                clause.Stickers = stickers;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(UncommonJokerClause clause, IJamlValueReader value)
    {
        // Empty disc (null / "" / []) = category match. No "Any" token.
        if (string.IsNullOrWhiteSpace(value.Text))
            return true;
        if (!value.TryEnumArray<MotelyJokerUncommon>(out var jokers))
            return false;
        clause.Jokers = jokers;
        return true;
    }

    /// <summary>Defaults when a clause specifies no <c>sources:</c> block — shop slots only.
    /// Packs and specialty streams need an explicit <c>sources:</c> block. Applied only when <c>Sources</c> is null.</summary>
    /// <inheritdoc cref="JokerFilterDesc.DefaultSources"/>
    internal static readonly JokerSourceConfig DefaultSources = JokerFilterDesc.DefaultSources;

    /// <summary>Uncommon names are 0.25 of a rarity poll then 1 of the uncommon pool; a wildcard is the 0.25 alone. See <see cref="JamlJokerRarity"/>.</summary>
    public static double EstimateRarity(UncommonJokerClause clause, in JamlRarityContext ctx) =>
        JamlJokerRarity.EstimateFixedRarity(
            clause.Antes, clause.Sources, clause.Jokers, MotelyJokerRarity.Uncommon,
            clause.Edition, clause.Stickers, clause.Min, clause.Max, in ctx
        );

    public readonly UncommonJokerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var sources = _clause.Sources ?? DefaultSources;
        foreach (var ante in _clause.Antes)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
            if (sources.CommonShopJokers.Length > 0)
                ctx.CacheCommonShopJokerStream(ante);
            if (sources.UncommonShopJokers.Length > 0)
                ctx.CacheUncommonShopJokerStream(ante);
            if (sources.RareShopJokers.Length > 0)
                ctx.CacheRareShopJokerStream(ante);
            if (sources.AllShopJokers.Length > 0)
                ctx.CacheShopJokerStream(ante);
        }

        // Pre-calculate target item types to avoid bitwise logic in the hot loop
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
                    $"Uncommon joker {jokers[i]} not found in MotelyItemType"
                );
            }
        }

        // Extract source indices from config
        var shopIndices = sources.ShopItems;
        var boosterIndices = sources.BoosterPacks;
        var commonShopJokerIndices = sources.CommonShopJokers;
        var uncommonShopJokerIndices = sources.UncommonShopJokers;
        var rareShopJokerIndices = sources.RareShopJokers;
        var allShopJokerIndices = sources.AllShopJokers;

        int maxShopItem = 0;
        foreach (var idx in shopIndices)
            if (idx > maxShopItem)
                maxShopItem = idx;

        int maxBoosterPack = 0;
        foreach (var idx in boosterIndices)
            if (idx > maxBoosterPack)
                maxBoosterPack = idx;

        int maxCommonShopJoker = 0;
        foreach (var idx in commonShopJokerIndices)
            if (idx > maxCommonShopJoker)
                maxCommonShopJoker = idx;

        int maxUncommonShopJoker = 0;
        foreach (var idx in uncommonShopJokerIndices)
            if (idx > maxUncommonShopJoker)
                maxUncommonShopJoker = idx;

        int maxRareShopJoker = 0;
        foreach (var idx in rareShopJokerIndices)
            if (idx > maxRareShopJoker)
                maxRareShopJoker = idx;

        int maxAllShopJoker = 0;
        foreach (var idx in allShopJokerIndices)
            if (idx > maxAllShopJoker)
                maxAllShopJoker = idx;

        return new UncommonJokerFilter(
            _clause,
            targetTypes,
            [.. shopIndices],
            [.. boosterIndices],
            [.. commonShopJokerIndices],
            [.. uncommonShopJokerIndices],
            [.. rareShopJokerIndices],
            [.. allShopJokerIndices],
            maxShopItem,
            maxBoosterPack,
            maxCommonShopJoker,
            maxUncommonShopJoker,
            maxRareShopJoker,
            maxAllShopJoker,
            sources.RequireMegaPack
        );
    }

    public struct UncommonJokerFilter(
        UncommonJokerClause clause,
        MotelyItemType[] targetTypes,
        int[] shopIndices,
        int[] boosterIndices,
        int[] commonShopJokerIndices,
        int[] uncommonShopJokerIndices,
        int[] rareShopJokerIndices,
        int[] allShopJokerIndices,
        int maxShopItem,
        int maxBoosterPack,
        int maxCommonShopJoker,
        int maxUncommonShopJoker,
        int maxRareShopJoker,
        int maxAllShopJoker,
        bool requireMegaPack
    ) : IMotelySeedFilter
    {
        private readonly UncommonJokerClause _clause = clause;
        private readonly MotelyItemType[] _targetTypes = targetTypes;
        private readonly int[] _shopIndices = shopIndices;
        private readonly int[] _boosterIndices = boosterIndices;
        private readonly int[] _commonShopJokerIndices = commonShopJokerIndices;
        private readonly int[] _uncommonShopJokerIndices = uncommonShopJokerIndices;
        private readonly int[] _rareShopJokerIndices = rareShopJokerIndices;
        private readonly int[] _allShopJokerIndices = allShopJokerIndices;
        private readonly bool _requireMegaPack = requireMegaPack;
        private readonly int _maxShopItem = maxShopItem;
        private readonly int _maxBoosterPack = maxBoosterPack;
        private readonly int _maxCommonShopJoker = maxCommonShopJoker;
        private readonly int _maxUncommonShopJoker = maxUncommonShopJoker;
        private readonly int _maxRareShopJoker = maxRareShopJoker;
        private readonly int _maxAllShopJoker = maxAllShopJoker;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // empty Jokers = category any
            int needed = _clause.Min;
            Debug.Assert(needed > 0, "UncommonJokerClause.Min must be > 0 — loader bug.");
            Vector256<int> matchCounts = Vector256<int>.Zero;

            var shopIndices = _shopIndices;
            var boosterIndices = _boosterIndices;
            var commonShopJokerIndices = _commonShopJokerIndices;
            var uncommonShopJokerIndices = _uncommonShopJokerIndices;
            var rareShopJokerIndices = _rareShopJokerIndices;
            var allShopJokerIndices = _allShopJokerIndices;

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
                // ── Raw common shop joker stream SIMD ──
                if (commonShopJokerIndices.Length > 0)
                {
                    var commonShopStream = ctx.CreateCommonShopJokerStream(ante);

                    for (int idx = 0; idx <= _maxCommonShopJoker; idx++)
                    {
                        var shopJoker = ctx.GetNextJoker(ref commonShopStream);
                        bool isTarget = false;
                        for (int i = 0; i < commonShopJokerIndices.Length; i++)
                        {
                            if (commonShopJokerIndices[i] == idx)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (!isTarget)
                            continue;

                        VectorMask jokerMatch = MatchJokers(shopJoker);
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

                // ── Raw uncommon shop joker stream SIMD ──
                if (uncommonShopJokerIndices.Length > 0)
                {
                    var uncommonShopStream = ctx.CreateUncommonShopJokerStream(ante);

                    for (int idx = 0; idx <= _maxUncommonShopJoker; idx++)
                    {
                        var shopJoker = ctx.GetNextJoker(ref uncommonShopStream);
                        bool isTarget = false;
                        for (int i = 0; i < uncommonShopJokerIndices.Length; i++)
                        {
                            if (uncommonShopJokerIndices[i] == idx)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (!isTarget)
                            continue;

                        VectorMask jokerMatch = MatchJokers(shopJoker);
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

                // ── Raw rare shop joker stream SIMD ──
                if (rareShopJokerIndices.Length > 0)
                {
                    var rareShopStream = ctx.CreateRareShopJokerStream(ante);

                    for (int idx = 0; idx <= _maxRareShopJoker; idx++)
                    {
                        var shopJoker = ctx.GetNextJoker(ref rareShopStream);
                        bool isTarget = false;
                        for (int i = 0; i < rareShopJokerIndices.Length; i++)
                        {
                            if (rareShopJokerIndices[i] == idx)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (!isTarget)
                            continue;

                        VectorMask jokerMatch = MatchJokers(shopJoker);
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

                // ── Raw all-rarity shop joker stream SIMD ──
                if (allShopJokerIndices.Length > 0)
                {
                    var allShopStream = ctx.CreateShopJokerStream(ante);

                    for (int idx = 0; idx <= _maxAllShopJoker; idx++)
                    {
                        var shopJoker = ctx.GetNextJoker(ref allShopStream);
                        bool isTarget = false;
                        for (int i = 0; i < allShopJokerIndices.Length; i++)
                        {
                            if (allShopJokerIndices[i] == idx)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (!isTarget)
                            continue;

                        VectorMask jokerMatch = MatchJokers(shopJoker);
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

                // ── Shop items SIMD ──
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

                // ── Buffoon packs SIMD ──
                // Per-lane size (Normal=2, Jumbo/Mega=4) + ante-1 slot reachability.
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
            VectorMask jokerMatch;
            if (JamlDisc.IsCategoryAny(_clause.Jokers))
            {
                jokerMatch = VectorEnum256.Equals(item.TypeCategory, MotelyItemTypeCategory.Joker);
                var rarityVec = new VectorEnum256<MotelyJokerRarity>(
                    Vector256.BitwiseAnd(
                        item.Value,
                        Vector256.Create(MotelyGlobals.JokerRarityMask)
                    )
                );
                jokerMatch &= VectorEnum256.Equals(rarityVec, MotelyJokerRarity.Uncommon);
            }
            else
            {
                jokerMatch = VectorMask.NoBitsSet;
                for (int t = 0; t < _targetTypes.Length; t++)
                    jokerMatch |= VectorEnum256.Equals(item.Type, _targetTypes[t]);
            }

            if (_clause.Edition.HasValue)
                jokerMatch &= VectorEnum256.Equals(item.Edition, _clause.Edition.Value);

            if (_clause.Stickers.Length > 0)
            {
                VectorMask stickerMatch = VectorMask.NoBitsSet;
                for (int s = 0; s < _clause.Stickers.Length; s++)
                {
                    switch (_clause.Stickers[s])
                    {
                        case MotelyJokerSticker.Eternal:
                            stickerMatch |= item.IsEternal;
                            break;
                        case MotelyJokerSticker.Perishable:
                            stickerMatch |= item.IsPerishable;
                            break;
                        case MotelyJokerSticker.Rental:
                            stickerMatch |= item.IsRental;
                            break;
                    }
                }
                jokerMatch &= stickerMatch;
            }

            return jokerMatch;
        }
    }
}
