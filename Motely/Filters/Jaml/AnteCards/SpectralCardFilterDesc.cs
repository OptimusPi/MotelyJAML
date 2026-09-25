using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("spectralCard", "spectralCards",
    ValueEnum = typeof(MotelySpectralCard), SourceConfigType = typeof(SpectralCardSourceConfig))]
[YamlObject]
public sealed partial class SpectralCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelySpectralCard[] Spectrals { get; set; } = [];

    public SpectralCardSourceConfig? Sources { get; set; }
}

public struct SpectralCardFilterDesc(SpectralCardClause clause)
    : IMotelySeedFilterDesc<SpectralCardFilterDesc.SpectralCardFilter>
{
    private readonly SpectralCardClause _clause = clause;

    public static string[] Discriminators => ["spectralCard", "spectralCards"];

    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources"];

    internal static readonly SpectralCardSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
        BoosterPacks = Enumerable.Range(0, MotelyGlobals.LateAntesMaxPackSlot + 1).ToArray(),
    };

    internal static readonly SpectralCardSourceConfig DefaultSpecialSources = new()
    {
        BoosterPacks = Enumerable.Range(0, MotelyGlobals.LateAntesMaxPackSlot + 1).ToArray(),
    };

    internal static SpectralCardSourceConfig ResolveSources(SpectralCardClause clause) =>
        clause.Sources
        ?? (JamlScoring.TargetsSpecialSpectral(clause) ? DefaultSpecialSources : DefaultSources);

    public SpectralCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var sources = ResolveSources(_clause);

        foreach (var ante in _clause.Antes)
        {
            if (sources.ShopItems.Length > 0)
                ctx.CacheShopStream(ante);
            if (sources.BoosterPacks.Length > 0)
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

        int maxSixthSense = 0;
        for (int i = 0; i < sources.SixthSense.Length; i++)
        {
            if (sources.SixthSense[i] > maxSixthSense)
                maxSixthSense = sources.SixthSense[i];
        }

        int maxSeance = 0;
        for (int i = 0; i < sources.Seance.Length; i++)
        {
            if (sources.Seance[i] > maxSeance)
                maxSeance = sources.Seance[i];
        }

        return new SpectralCardFilter(
            _clause,
            maxShopItem,
            maxBoosterPack,
            maxSixthSense,
            maxSeance
        );
    }

    public struct SpectralCardFilter(
        SpectralCardClause clause,
        int maxShopItem,
        int maxBoosterPack,
        int maxSixthSense,
        int maxSeance
    ) : IMotelySeedFilter
    {
        private readonly SpectralCardClause _clause = clause;
        private readonly int _maxShopItem = maxShopItem;
        private readonly int _maxBoosterPack = maxBoosterPack;
        private readonly int _maxSixthSense = maxSixthSense;
        private readonly int _maxSeance = maxSeance;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;
            int maxShopItem = _maxShopItem;
            int maxBoosterPack = _maxBoosterPack;
            int maxSixthSense = _maxSixthSense;
            int maxSeance = _maxSeance;
            int needed = clause.Min;
            Debug.Assert(needed > 0, "SpectralCardClause.Min must be > 0 — loader bug.");

            Vector256<int> matchCounts = Vector256<int>.Zero;
            var sources = ResolveSources(clause);
            if (sources.RequireMegaPack || sources.EtherealTag || sources.OmenGlobe)
                return ctx.SearchIndividualSeeds(
                    (MotelySingleSearchContext single) =>
                        JamlScoring.ClauseMeetsMinForFilter(ref single, clause) ? 1 : 0
                );

            var shopIndices = sources.ShopItems;
            var boosterPacks = sources.BoosterPacks;
            var sixthSenseRolls = sources.SixthSense;
            var seanceRolls = sources.Seance;

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
                if (shopIndices.Length > 0 && ctx.Deck == MotelyDeck.Ghost)
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

                        VectorMask isSpectral = VectorEnum256.Equals(
                            item.TypeCategory,
                            MotelyItemTypeCategory.SpectralCard
                        );
                        VectorMask match = MatchSpectrals(item, clause) & isSpectral;

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
                    var spectralStream = ctx.CreateSpectralPackSpectralStream(ante);

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
                        VectorMask isSpectral = VectorEnum256.Equals(
                            packType,
                            MotelyBoosterPackType.Spectral
                        );
                        if (isSpectral.IsAllFalse())
                            continue;

                        VectorMask isNormal = VectorEnum256.Equals(
                            pack.GetPackSize(),
                            MotelyBoosterPackSize.Normal
                        );
                        VectorMask baseLanes = isSpectral;
                        VectorMask extraLanes = isSpectral & ~isNormal;
                        var baseMask = JamlSimdPackSupport.ToPrngMask(baseLanes);
                        var extraMask = JamlSimdPackSupport.ToPrngMask(extraLanes);

                        for (int c = 0; c < 2; c++)
                        {
                            var card = ctx.GetNextSpectral(ref spectralStream, baseMask);
                            if (countLanes.IsPartiallyTrue())
                                JamlSimdPackSupport.AddMatchCounts(
                                    MatchSpectrals(card, clause) & countLanes & baseLanes,
                                    ref matchCounts
                                );
                        }

                        if (extraLanes.IsPartiallyTrue())
                        {
                            for (int c = 0; c < 2; c++)
                            {
                                var card = ctx.GetNextSpectral(ref spectralStream, extraMask);
                                if (countLanes.IsPartiallyTrue())
                                    JamlSimdPackSupport.AddMatchCounts(
                                        MatchSpectrals(card, clause) & countLanes & extraLanes,
                                        ref matchCounts
                                    );
                            }
                        }
                    }
                }

                if (sixthSenseRolls.Length > 0)
                {
                    var sixthSenseStream = ctx.CreateSixthSenseSpectralStream(ante);

                    for (int roll = 0; roll <= maxSixthSense; roll++)
                    {
                        var item = ctx.GetNextSpectral(ref sixthSenseStream);
                        bool isTarget = false;
                        for (int i = 0; i < sixthSenseRolls.Length; i++)
                        {
                            if (sixthSenseRolls[i] == roll)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (isTarget)
                        {
                            VectorMask match = MatchSpectrals(item, clause);
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

                if (seanceRolls.Length > 0)
                {
                    var seanceStream = ctx.CreateSeanceSpectralStream(ante);

                    for (int roll = 0; roll <= maxSeance; roll++)
                    {
                        var item = ctx.GetNextSpectral(ref seanceStream);
                        bool isTarget = false;
                        for (int i = 0; i < seanceRolls.Length; i++)
                        {
                            if (seanceRolls[i] == roll)
                            {
                                isTarget = true;
                                break;
                            }
                        }

                        if (isTarget)
                        {
                            VectorMask match = MatchSpectrals(item, clause);
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
        internal static VectorMask MatchSpectrals(MotelyItemVector items, SpectralCardClause clause)
        {
            var spectrals = JamlDisc.OrEmpty(clause.Spectrals);
            if (JamlDisc.IsCategoryAny(spectrals))
                return VectorEnum256.Equals(items.TypeCategory, MotelyItemTypeCategory.SpectralCard);

            VectorMask mask = VectorMask.NoBitsSet;
            var itemTypes = items.Type;

            for (int i = 0; i < spectrals.Length; i++)
            {
                var targetType =
                    (int)MotelyItemTypeCategory.SpectralCard | (int)spectrals[i];
                mask |= VectorEnum256.Equals(itemTypes, (MotelyItemType)targetType);
            }

            return mask;
        }
    }
}

[YamlObject]
public sealed partial record SpectralCardSourceConfig
{
    public static readonly string[] SourceKeys =
    [
        "shopItems",
        "boosterPacks",
        "sixthSense",
        "seance",
        "etherealTag",
        "requireMega",
        "requireMegaPack",
        "omenGlobe",
    ];

    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];
    public int[] SixthSense { get; set; } = [];
    public int[] Seance { get; set; } = [];
    public bool RequireMegaPack { get; set; }

    public bool EtherealTag { get; set; }

    public bool OmenGlobe { get; set; }
}
