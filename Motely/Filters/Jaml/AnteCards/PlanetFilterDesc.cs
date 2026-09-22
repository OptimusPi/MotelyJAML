using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("planetCard", "planetCards",
    ValueEnum = typeof(MotelyPlanetCard), SourceConfigType = typeof(PlanetSourceConfig))]
public sealed class PlanetCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyPlanetCard[] Planets { get; set; } = [];

    // null = no sources: in JAML → filter DefaultSources at CreateFilter/score (not parse).
    // applies. Any explicit block (even partial) is used verbatim — defaults never merge in.
    public PlanetSourceConfig? Sources { get; set; }
}

public struct PlanetCardFilterDesc(PlanetCardClause clause)
    : IMotelySeedFilterDesc<PlanetCardFilterDesc.PlanetCardFilter>,
      IJamlClauseDesc<PlanetCardClause>
{
    private readonly PlanetCardClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["planetCard", "planetCards"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources"];

    /// <inheritdoc/>
    public static bool Set(PlanetCardClause clause, string key, IJamlValueReader value)
    {
        return false;
    }

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(PlanetCardClause clause, IJamlValueReader value)
    {
        // Empty disc (null / "" / []) = category match. No "Any" token.
        if (string.IsNullOrWhiteSpace(value.Text))
            return true;
        if (!value.TryEnumArray<MotelyPlanetCard>(out var planets)) return false;
        clause.Planets = planets;
        return true;
    }

    /// <summary>
    /// Filter-layer default when Sources is null. Shop only; packs need explicit sources:.
    /// </summary>
    internal static readonly PlanetSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

    /// <summary>
    /// A shop slot is a planet with the deck's planet weight of the shop total, then 1 of 12; a
    /// celestial pack slot is a weighted pack roll then that many draws, each <c>0.997 × 1/12</c>
    /// because Black Hole takes the card first 0.3% of the time.
    /// </summary>
    public static double EstimateRarity(PlanetCardClause clause, in JamlRarityContext ctx)
    {
        var sources = clause.Sources ?? DefaultSources;

        var planets = JamlDisc.OrEmpty(clause.Planets);
        double share = JamlPoolRarity.PoolShare(
            JamlPoolRarity.Distinct(planets),
            MotelyEnum<MotelyPlanetCard>.ValueCount,
            JamlDisc.IsCategoryAny(planets)
        );

        const double BlackHolePerCard = 0.003; // GetNextPlanet on a black-holeable stream: poll > 0.997
        double shopShare = ctx.ShopPlanetRate / ctx.ShopTotalRate * share;
        double packCardShare = (1.0 - BlackHolePerCard) * share;

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
                    continue; // ante 1's first offer is a Buffoon, never a celestial pack
                pmf = JamlCountDistribution.Convolve(
                    pmf,
                    JamlPoolRarity.PackSlotCards(
                        MotelyBoosterPackType.Celestial,
                        packCardShare,
                        sources.RequireMegaPack
                    )
                );
            }
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    public PlanetCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
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

        return new PlanetCardFilter(_clause, maxShopItem, maxBoosterPack);
    }

    public struct PlanetCardFilter(PlanetCardClause clause, int maxShopItem, int maxBoosterPack)
        : IMotelySeedFilter
    {
        private readonly PlanetCardClause _clause = clause;
        private readonly int _maxShopItem = maxShopItem;
        private readonly int _maxBoosterPack = maxBoosterPack;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // empty Planets = category any
            var clause = _clause;
            int maxShopItem = _maxShopItem;
            int maxBoosterPack = _maxBoosterPack;
            int needed = clause.Min;
            Debug.Assert(needed > 0, "PlanetCardClause.Min must be > 0 — loader bug.");

            Vector256<int> matchCounts = Vector256<int>.Zero;
            var sources = clause.Sources ?? DefaultSources;
            var shopIndices = sources.ShopItems;
            var boosterPacks = sources.BoosterPacks;

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

                        VectorMask isPlanet = VectorEnum256.Equals(
                            item.TypeCategory,
                            MotelyItemTypeCategory.PlanetCard
                        );
                        VectorMask match = MatchPlanets(item, clause) & isPlanet;

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

                // ── Celestial packs SIMD ──
                // Per-lane size (Normal=3, Jumbo/Mega=5) + ante-1 slot reachability.
                if (boosterPacks.Length > 0)
                {
                    var packStream = ctx.CreateBoosterPackStream(ante);
                    var planetStream = ctx.CreateCelestialPackPlanetStream(ante);

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
                        VectorMask isCelestial = VectorEnum256.Equals(
                            packType,
                            MotelyBoosterPackType.Celestial
                        );
                        if (isCelestial.IsAllFalse())
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
                        VectorMask baseLanes = isCelestial;
                        VectorMask extraLanes = isCelestial & ~isNormal;
                        var baseMask = JamlSimdPackSupport.ToPrngMask(baseLanes);
                        var extraMask = JamlSimdPackSupport.ToPrngMask(extraLanes);

                        for (int c = 0; c < 3; c++)
                        {
                            var card = ctx.GetNextPlanet(ref planetStream, baseMask);
                            if (countLanes.IsPartiallyTrue())
                                JamlSimdPackSupport.AddMatchCounts(
                                    MatchPlanets(card, clause) & countLanes & baseLanes,
                                    ref matchCounts
                                );
                        }

                        if (extraLanes.IsPartiallyTrue())
                        {
                            for (int c = 0; c < 2; c++)
                            {
                                var card = ctx.GetNextPlanet(ref planetStream, extraMask);
                                if (countLanes.IsPartiallyTrue())
                                    JamlSimdPackSupport.AddMatchCounts(
                                        MatchPlanets(card, clause) & countLanes & extraLanes,
                                        ref matchCounts
                                    );
                            }
                        }
                    }
                }
            }

            return JamlSimdPackSupport.MeetsMinMaxMask(matchCounts, needed, clause.Max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static VectorMask MatchPlanets(MotelyItemVector items, PlanetCardClause clause)
        {
            var planets = JamlDisc.OrEmpty(clause.Planets);
            if (JamlDisc.IsCategoryAny(planets))
                return VectorEnum256.Equals(items.TypeCategory, MotelyItemTypeCategory.PlanetCard);

            VectorMask mask = VectorMask.NoBitsSet;
            var itemTypes = items.Type;

            for (int i = 0; i < planets.Length; i++)
            {
                var targetType = (int)MotelyItemTypeCategory.PlanetCard | (int)planets[i];
                mask |= VectorEnum256.Equals(itemTypes, (MotelyItemType)targetType);
            }

            return mask;
        }
    }
}

/// <summary>
/// <c>sources:</c> block for <c>planetCard:</c>. Colocated with <see cref="PlanetCardFilterDesc"/> (T5).
/// </summary>
public sealed record PlanetSourceConfig
{
    /// <summary>requireMega/requireMegaPack: both real aliases for RequireMegaPack below.</summary>
    public static readonly string[] SourceKeys =
        ["shopItems", "boosterPacks", "requireMega", "requireMegaPack"];

    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];

    /// <summary>When true, only Mega-sized Celestial packs count (Normal/Jumbo still advance the stream).</summary>
    public bool RequireMegaPack { get; set; }
}
