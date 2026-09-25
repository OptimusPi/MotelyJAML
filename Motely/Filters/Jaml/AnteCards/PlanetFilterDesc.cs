using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("planetCard", "planetCards",
    ValueEnum = typeof(MotelyPlanetCard), SourceConfigType = typeof(PlanetSourceConfig))]
[YamlObject]
public sealed partial class PlanetCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyPlanetCard[] Planets { get; set; } = [];

    public PlanetSourceConfig? Sources { get; set; }
}

public struct PlanetCardFilterDesc(PlanetCardClause clause)
    : IMotelySeedFilterDesc<PlanetCardFilterDesc.PlanetCardFilter>
{
    private readonly PlanetCardClause _clause = clause;

    public static string[] Discriminators => ["planetCard", "planetCards"];

    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources"];

    internal static readonly PlanetSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

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

[YamlObject]
public sealed partial record PlanetSourceConfig
{
    public static readonly string[] SourceKeys =
        ["shopItems", "boosterPacks", "requireMega", "requireMegaPack"];

    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];

    public bool RequireMegaPack { get; set; }
}
