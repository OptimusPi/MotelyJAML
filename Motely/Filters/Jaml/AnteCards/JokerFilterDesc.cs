using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("joker", "jokers",
    ValueEnum = typeof(MotelyJoker), SourceConfigType = typeof(JokerSourceConfig))]
public sealed class JokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyJoker[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }

    /// <summary>Legendary-path sources for any Legendary names in this mixed clause. Null = apply
    /// <see cref="LegendaryJokerFilterDesc.DefaultSources"/> (same convention as <see cref="Sources"/>).</summary>
    public LegendaryJokerSourceConfig? LegendarySources { get; set; }
}

public struct JokerFilterDesc(JokerClause clause)
    : IMotelySeedFilterDesc<JokerFilterDesc.JokerFilter>,
      IJamlClauseDesc<JokerClause>
{
    private readonly JokerClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["joker", "jokers"];

    /// <inheritdoc/>
    public static string[] ClauseKeys =>
        ["min", "max", "score", "label", "ante", "antes", "sources", "edition", "stickers"];

    /// <inheritdoc/>
    public static bool Set(JokerClause clause, string key, IJamlValueReader value)
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
    public static bool SetDiscriminatorValue(JokerClause clause, IJamlValueReader value)
    {
        // Empty disc (null / "" / []) = category match. No "Any" token.
        if (string.IsNullOrWhiteSpace(value.Text))
            return true;
        if (!value.TryEnumArray<MotelyJoker>(out var jokers))
            return false;
        clause.Jokers = jokers;
        return true;
    }

    /// <summary>
    /// Filter-layer default when <see cref="JokerClause.Sources"/> is null (no <c>sources:</c> in JAML).
    /// The loader leaves Sources null — this is not parse/language. Shop slots only; packs and
    /// specialty streams require an explicit <c>sources:</c> block (wholesale, no merge).
    /// </summary>
    internal static readonly JokerSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

    /// <summary>
    /// Shop slots, buffoon packs and the specialty streams for the ordinary names, the soul path
    /// for any legendary ones, the two convolved under one window — the same split
    /// <c>CountJokerClauseOccurrences</c> makes. See <see cref="JamlJokerRarity"/>.
    /// </summary>
    public static double EstimateRarity(JokerClause clause, in JamlRarityContext ctx) =>
        JamlJokerRarity.EstimateJoker(clause, in ctx);

    public JokerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
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
                    $"Joker {jokers[i]} not found in MotelyItemType"
                );
            }
        }

        // null sources → filter default (shop only). Loader never fills Sources.
        var sources = _clause.Sources ?? DefaultSources;
        var shopIndices = sources.ShopItems;
        var boosterIndices = sources.BoosterPacks;

        Debug.Assert(
            shopIndices.Length > 0 || boosterIndices.Length > 0,
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
            sources.RequireMegaPack
        );
    }

    public struct JokerFilter(
        JokerClause clause,
        MotelyItemType[] targetTypes,
        int[] shopIndices,
        int[] boosterIndices,
        int maxShopItem,
        int maxBoosterPack,
        bool requireMegaPack
    ) : IMotelySeedFilter
    {
        private readonly JokerClause _clause = clause;
        private readonly MotelyItemType[] _targetTypes = targetTypes;
        private readonly int[] _shopIndices = shopIndices;
        private readonly int[] _boosterIndices = boosterIndices;
        private readonly int _maxShopItem = maxShopItem;
        private readonly int _maxBoosterPack = maxBoosterPack;
        private readonly bool _requireMegaPack = requireMegaPack;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // empty Jokers = category any
            int needed = _clause.Min;
            Debug.Assert(needed > 0, "JokerClause.Min must be > 0 — loader bug.");

            if (UsesLegendaryPath(_clause))
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
    }
}

// ── Rarity-specific joker clauses ──

[JamlDiscriminator("commonJoker", "commonJokers",
    ValueEnum = typeof(MotelyJokerCommon), SourceConfigType = typeof(JokerSourceConfig))]
public sealed class CommonJokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyJokerCommon[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }
}

[JamlDiscriminator("uncommonJoker", "uncommonJokers",
    ValueEnum = typeof(MotelyJokerUncommon), SourceConfigType = typeof(JokerSourceConfig))]
public sealed class UncommonJokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyJokerUncommon[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }
}

[JamlDiscriminator("rareJoker", "rareJokers",
    ValueEnum = typeof(MotelyJokerRare), SourceConfigType = typeof(JokerSourceConfig))]
public sealed class RareJokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyJokerRare[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }
}

/// <summary>
/// <c>sources:</c> block for joker / common / uncommon / rare clauses. Lives with the joker
/// desc family (T5) — not on the dumb <see cref="JamlConfig"/> bag.
/// </summary>
public sealed record JokerSourceConfig
{
    /// <summary>
    /// This class's settable properties, camelCased — the single list JamlConfigLoader
    /// ValidateKeys and Motely.Schema both read. <c>emperor</c> lives on
    /// <see cref="TarotCardSourceConfig"/>, not here.
    /// </summary>
    /// <summary>requireMega/requireMegaPack: both real aliases for RequireMegaPack below.</summary>
    public static readonly string[] SourceKeys =
    [
        "shopItems", "boosterPacks", "judgement", "wraith", "riffRaff", "rareTag", "uncommonTag",
        "commonShopJokers", "uncommonShopJokers", "rareShopJokers", "allShopJokers",
        "requireMega", "requireMegaPack",
    ];

    /// <summary>Assembled shop slots via the full shop item stream (any item type).</summary>
    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];

    /// <summary>When true, only Mega-sized Buffoon packs count (Normal/Jumbo still advance the stream).</summary>
    public bool RequireMegaPack { get; set; }

    /// <summary>Ante-1 pack-slot cap. Default 3 (normal gameplay). Raise to 5 for Hieroglyph scans.</summary>
    public int[] Judgement { get; set; } = [];
    public int[] Wraith { get; set; } = [];
    public int[] RiffRaff { get; set; } = [];
    public int[] RareTag { get; set; } = [];
    public int[] UncommonTag { get; set; } = [];

    /// <summary>0..n rolls on the common shop joker PRNG only (fast path).</summary>
    public int[] CommonShopJokers { get; set; } = [];

    /// <summary>0..n rolls on the uncommon shop joker PRNG only (fast path; not the same indices as <see cref="ShopItems"/> when slots mix types).</summary>
    public int[] UncommonShopJokers { get; set; } = [];

    /// <summary>0..n rolls on the rare shop joker PRNG only (fast path).</summary>
    public int[] RareShopJokers { get; set; } = [];

    /// <summary>0..n rolls on the all-rarity shop joker stream (fast path).</summary>
    public int[] AllShopJokers { get; set; } = [];
}
