using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("tarotCard", "tarotCards",
    ValueEnum = typeof(MotelyTarotCard), SourceConfigType = typeof(TarotCardSourceConfig))]
public sealed class TarotCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyTarotCard[] Tarots { get; set; } = [];

    // null = no sources: in JAML → filter DefaultSources at CreateFilter/score (not parse).
    public TarotCardSourceConfig? Sources { get; set; }
}

public struct TarotCardFilterDesc(TarotCardClause clause)
    : IMotelySeedFilterDesc<TarotCardFilterDesc.TarotCardFilter>,
      IJamlClauseDesc<TarotCardClause>
{
    private readonly TarotCardClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["tarotCard", "tarotCards"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources"];

    /// <inheritdoc/>
    public static bool Set(TarotCardClause clause, string key, IJamlValueReader value)
    {
        return false;
    }

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(TarotCardClause clause, IJamlValueReader value)
    {
        // Empty disc (null / "" / []) = category match. No "Any" token.
        if (string.IsNullOrWhiteSpace(value.Text))
            return true;
        if (!value.TryEnumArray<MotelyTarotCard>(out var tarots)) return false;
        clause.Tarots = tarots;
        return true;
    }

    /// <summary>
    /// Filter-layer default when Sources is null. Shop only; packs/specialty need explicit sources:.
    /// </summary>
    internal static readonly TarotCardSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

    /// <summary>
    /// A shop slot is a tarot with the deck's tarot weight of the shop total, then 1 of 22; an
    /// arcana pack slot is a weighted pack roll then that many draws, each <c>0.997 × 1/22</c>
    /// because The Soul takes the card first 0.3% of the time; an Emperor roll is two draws and a
    /// purple-seal roll one, both off plain 1-of-22 streams. The Charm-tag bonus pack depends on
    /// what the first two weighted slots rolled, which this model does not follow — a clause that
    /// asks for it is reported as unmodelled rather than silently undercounted.
    /// </summary>
    public static double EstimateRarity(TarotCardClause clause, in JamlRarityContext ctx)
    {
        var sources = clause.Sources ?? DefaultSources;
        if (sources.CharmTag)
            return double.NaN;

        var tarots = JamlDisc.OrEmpty(clause.Tarots);
        double share = JamlPoolRarity.PoolShare(
            JamlPoolRarity.Distinct(tarots),
            MotelyEnum<MotelyTarotCard>.ValueCount,
            JamlDisc.IsCategoryAny(tarots)
        );

        const double SoulPerCard = 0.003; // GetNextTarot on a soulable stream: poll > 0.997
        double shopShare = ctx.ShopTarotRate / ctx.ShopTotalRate * share;
        double packCardShare = (1.0 - SoulPerCard) * share;

        double[] pmf = JamlCountDistribution.Zero;
        foreach (int ante in clause.Antes)
        {
            pmf = JamlCountDistribution.Convolve(
                pmf,
                JamlCountDistribution.Binomial(JamlPoolRarity.Distinct(sources.ShopItems), shopShare)
            );

            HashSet<int> slots = [];
            foreach (int slot in sources.BoosterPacks)
            {
                if (!slots.Add(slot) || !JamlPoolRarity.SlotIsReachable(ante, slot))
                    continue;
                if (JamlPoolRarity.SlotIsFixedBuffoon(ante, slot))
                    continue; // ante 1's first offer is a Buffoon, never an arcana pack
                pmf = JamlCountDistribution.Convolve(
                    pmf,
                    JamlPoolRarity.PackSlotCards(
                        MotelyBoosterPackType.Arcana,
                        packCardShare,
                        sources.RequireMegaPack
                    )
                );
            }

            pmf = JamlCountDistribution.Convolve(
                pmf,
                JamlCountDistribution.Binomial(2 * JamlPoolRarity.Distinct(sources.Emperor), share)
            );
            pmf = JamlCountDistribution.Convolve(
                pmf,
                JamlCountDistribution.Binomial(
                    JamlPoolRarity.Distinct(sources.PurpleSealOrEightBall),
                    share
                )
            );
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

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
            // empty Tarots = category any
            var clause = _clause;
            int maxShopItem = _maxShopItem;
            int maxBoosterPack = _maxBoosterPack;
            int maxEmperor = _maxEmperor;
            int maxPurpleSeal = _maxPurpleSeal;
            int needed = clause.Min;
            Debug.Assert(needed > 0, "TarotCardClause.Min must be > 0 — loader bug.");

            Vector256<int> matchCounts = Vector256<int>.Zero;
            var sources = clause.Sources ?? DefaultSources;

            // Charm-tag bonus pack is shop-order weighted; share one match core with scoring.
            // charmTag counts only alongside a boosterPacks list — the bonus pack's contents
            // are gated on its pack index being in boosterPacks, so charmTag alone matches
            // nothing by construction.
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
                // ── Shop items SIMD ──
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

                // ── Arcana packs SIMD ──
                // Per-lane pack size (Normal=3, Jumbo/Mega=5) + ante-1 slot reachability.
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
                        // Cards 0–2: every Arcana lane. Cards 3–4: Jumbo/Mega only.
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

                // ── Emperor SIMD ──
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

                // ── Purple Seal SIMD ──
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

/// <summary>
/// <c>sources:</c> block for <c>tarotCard:</c>. Colocated with <see cref="TarotCardFilterDesc"/> (T5).
/// </summary>
public sealed record TarotCardSourceConfig
{
    /// <summary>requireMega/requireMegaPack: both real aliases for RequireMegaPack below.</summary>
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

    /// <summary>
    /// When true, booster arcana scoring may consume the Charm-tag bonus pack (second weighted slot, no natural Arcana).
    /// </summary>
    public bool CharmTag { get; set; }

    /// <summary>When true, only Mega-sized Arcana packs count (Normal/Jumbo still advance the stream).</summary>
    public bool RequireMegaPack { get; set; }
}
