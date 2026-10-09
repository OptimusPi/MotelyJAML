using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters;

[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class RareJokerClause : IMotelyClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyJokerRare[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    public MotelyJokerSticker[] Stickers { get; set; } = [];
    public JokerSourceConfig? Sources { get; set; }

    public IMotelySeedFilterDesc CreateFilterDesc() => new RareJokerFilterDesc(this);
}

public struct RareJokerFilterDesc(RareJokerClause clause)
    : IMotelySeedFilterDesc<RareJokerFilterDesc.RareJokerFilter>
{
    private readonly RareJokerClause _clause = clause;

    /// <summary>Defaults when a clause specifies no <c>sources:</c> block — shop slots only.
    /// Packs and specialty streams need an explicit <c>sources:</c> block. Applied only when <c>Sources</c> is null.</summary>
    /// <inheritdoc cref="JokerFilterDesc.DefaultSources"/>
    internal static readonly JokerSourceConfig DefaultSources = JokerFilterDesc.DefaultSources;

    public RareJokerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
        }

        // Pre-calculate target item types to avoid bitwise logic in the hot loop
        var jokers = _clause.Jokers;
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
                    $"Rare joker {jokers[i]} not found in MotelyItemType"
                );
            }
        }

        // null sources → filter default (shop only). Loader never fills Sources.
        var sources = _clause.Sources ?? DefaultSources;
        var shopIndices = sources.ShopItems;
        var boosterIndices = sources.BoosterPacks;

        // Only shop slots and buffoon packs are walked in SIMD here; everything else the
        // clause can name is counted per seed by the scalar law.
        bool confirmPerSeed = sources.HasSpawnSources || sources.HasRawShopJokerSources;

        int maxShopItem = 0;
        foreach (var idx in shopIndices)
            if (idx > maxShopItem)
                maxShopItem = idx;

        int maxBoosterPack = 0;
        foreach (var idx in boosterIndices)
            if (idx > maxBoosterPack)
                maxBoosterPack = idx;

        return new RareJokerFilter(
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

    public struct RareJokerFilter(
        RareJokerClause clause,
        MotelyItemType[] targetTypes,
        int[] shopIndices,
        int[] boosterIndices,
        int maxShopItem,
        int maxBoosterPack,
        bool requireMegaPack,
        bool confirmPerSeed
    ) : IMotelySeedFilter
    {
        private readonly RareJokerClause _clause = clause;
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
            // empty Jokers = category any
            int needed = _clause.Min;
            Debug.Assert(needed > 0, "RareJokerClause.Min must be > 0 — loader bug.");

            if (_confirmPerSeed)
            {
                var clause = _clause;
                return ctx.SearchIndividualSeeds(
                    (MotelySingleSearchContext singleCtx) =>
                        ClauseScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
                );
            }

            Vector256<int> matchCounts = Vector256<int>.Zero;

            var shopIndices = _shopIndices;
            var boosterIndices = _boosterIndices;

            VectorMask ante1Extended = VectorMask.NoBitsSet;
            if (boosterIndices.Length > 0 && SimdPackSupport.NeedsAnte1Extension(_maxBoosterPack))
            {
                bool hasAnte1 = false;
                for (int i = 0; i < _clause.Antes.Length; i++)
                    if (_clause.Antes[i] == 1)
                    {
                        hasAnte1 = true;
                        break;
                    }
                if (hasAnte1)
                    ante1Extended = SimdPackSupport.Ante1PackExtensionMask(ref ctx);
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

                        VectorMask reachable = SimdPackSupport.SlotReachableMask(
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
                        var baseMask = SimdPackSupport.ToPrngMask(baseLanes);
                        var extraMask = SimdPackSupport.ToPrngMask(extraLanes);

                        for (int c = 0; c < 2; c++)
                        {
                            var joker = ctx.GetNextJoker(ref jokerStream, baseMask);
                            if (countLanes.IsPartiallyTrue())
                                SimdPackSupport.AddMatchCounts(
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
                                    SimdPackSupport.AddMatchCounts(
                                        MatchJokers(joker) & countLanes & extraLanes,
                                        ref matchCounts
                                    );
                            }
                        }
                    }
                }
            }

            return SimdPackSupport.MeetsMinMaxMask(matchCounts, _clause.Min, _clause.Max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly VectorMask MatchJokers(in MotelyItemVector item)
        {
            VectorMask jokerMatch;
            if ((_clause.Jokers.Length == 0))
            {
                jokerMatch = VectorEnum256.Equals(item.TypeCategory, MotelyItemTypeCategory.Joker);
                var rarityVec = new VectorEnum256<MotelyJokerRarity>(
                    Vector256.BitwiseAnd(
                        item.Value,
                        Vector256.Create(MotelyGlobals.JokerRarityMask)
                    )
                );
                jokerMatch &= VectorEnum256.Equals(rarityVec, MotelyJokerRarity.Rare);
            }
            else
            {
                jokerMatch = VectorMask.NoBitsSet;
                for (int t = 0; t < _targetTypes.Length; t++)
                    jokerMatch |= VectorEnum256.Equals(item.Type, _targetTypes[t]);
            }

            if (_clause.Edition.HasValue)
                jokerMatch &= VectorEnum256.Equals(item.Edition, _clause.Edition.Value);

            // Every listed sticker must be present, same as scalar MatchJoker; None is no gate.
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
