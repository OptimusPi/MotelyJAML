using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("spectralCard", "spectralCards",
    ValueEnum = typeof(MotelySpectralCard), SourceConfigType = typeof(SpectralCardSourceConfig))]
public sealed class SpectralCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelySpectralCard[] Spectrals { get; set; } = [];

    // null = no sources: in JAML → filter DefaultSources at CreateFilter/score (not parse).
    public SpectralCardSourceConfig? Sources { get; set; }
}

public struct SpectralCardFilterDesc(SpectralCardClause clause)
    : IMotelySeedFilterDesc<SpectralCardFilterDesc.SpectralCardFilter>,
      IJamlClauseDesc<SpectralCardClause>
{
    private readonly SpectralCardClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["spectralCard", "spectralCards"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources"];

    /// <inheritdoc/>
    public static bool Set(SpectralCardClause clause, string key, IJamlValueReader value)
    {
        return false;
    }

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(SpectralCardClause clause, IJamlValueReader value)
    {
        // Empty disc (null / "" / []) = category match. No "Any" token.
        if (string.IsNullOrWhiteSpace(value.Text))
            return true;
        if (!value.TryEnumArray<MotelySpectralCard>(out var spectrals)) return false;
        clause.Spectrals = spectrals;
        return true;
    }

    /// <summary>
    /// Filter-layer default when Sources is null for ordinary spectrals. Shop only;
    /// packs/specialty need explicit <c>sources:</c>.
    /// </summary>
    internal static readonly SpectralCardSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

    /// <summary>
    /// Null-sources default for TheSoul / BlackHole: those cards never appear in shop —
    /// only Arcana/Celestial/Spectral packs. Shop-only <see cref="DefaultSources"/> would
    /// make <c>spectralCard: TheSoul</c> match nothing.
    /// </summary>
    internal static readonly SpectralCardSourceConfig DefaultSpecialSources = new()
    {
        BoosterPacks = [0, 1, 2, 3, 4, 5],
    };

    /// <summary>
    /// Ordinary spectrals are a uniform draw of sixteen — the plain draw re-rolls past The Soul and
    /// Black Hole — off the shop (only Ghost offers them), Sixth Sense and Séance; in a spectral
    /// pack each card first rolls The Soul at 0.003 and then Black Hole at 0.003 before that draw.
    /// The two specials are counted per pack, at most once each: in spectral packs, and — as the
    /// scorer does — The Soul in arcana packs and Black Hole in celestial ones. A category-any
    /// clause counts every card of a spectral pack, specials included. The Ethereal-tag bonus
    /// pack and Omen Globe substitution depend on state this model does not follow, so a clause
    /// asking for either is reported as unmodelled rather than undercounted.
    /// </summary>
    public static double EstimateRarity(SpectralCardClause clause, in JamlRarityContext ctx)
    {
        var sources =
            clause.Sources
            ?? (JamlScoring.TargetsSpecialSpectral(clause) ? DefaultSpecialSources : DefaultSources);
        if (sources.EtherealTag || sources.OmenGlobe)
            return double.NaN;

        var spectrals = JamlDisc.OrEmpty(clause.Spectrals);
        bool any = JamlDisc.IsCategoryAny(spectrals);
        bool soul = false, blackHole = false;
        HashSet<MotelySpectralCard> ordinaryNames = [];
        foreach (var spectral in spectrals)
        {
            if (spectral == MotelySpectralCard.TheSoul) soul = true;
            else if (spectral == MotelySpectralCard.BlackHole) blackHole = true;
            else ordinaryNames.Add(spectral);
        }

        int ordinaryPool = MotelyEnum<MotelySpectralCard>.ValueCount - 2;
        double ordinary = JamlPoolRarity.PoolShare(ordinaryNames.Count, ordinaryPool, any);

        const double SpecialPerCard = 0.003; // soul / black-hole polls: > 0.997
        double shopShare = ctx.ShopSpectralRate / ctx.ShopTotalRate * ordinary;
        double packCardShare = any
            ? 1.0
            : (1.0 - SpecialPerCard) * (1.0 - SpecialPerCard) * ordinary;

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
                    continue; // ante 1's first offer is a Buffoon, never a spectral/arcana/celestial pack

                double[] slotPmf = JamlPoolRarity.PackSlotCards(
                    MotelyBoosterPackType.Spectral,
                    packCardShare,
                    sources.RequireMegaPack
                );
                if (soul)
                {
                    slotPmf = JamlCountDistribution.Convolve(
                        slotPmf,
                        JamlCountDistribution.Bernoulli(
                            JamlPoolRarity.PackSlotHasAny(MotelyBoosterPackType.Spectral, SpecialPerCard, sources.RequireMegaPack)
                            + JamlPoolRarity.PackSlotHasAny(MotelyBoosterPackType.Arcana, SpecialPerCard, sources.RequireMegaPack)
                        )
                    );
                }
                if (blackHole)
                {
                    slotPmf = JamlCountDistribution.Convolve(
                        slotPmf,
                        JamlCountDistribution.Bernoulli(
                            JamlPoolRarity.PackSlotHasAny(MotelyBoosterPackType.Spectral, SpecialPerCard, sources.RequireMegaPack)
                            + JamlPoolRarity.PackSlotHasAny(MotelyBoosterPackType.Celestial, SpecialPerCard, sources.RequireMegaPack)
                        )
                    );
                }
                pmf = JamlCountDistribution.Convolve(pmf, slotPmf);
            }

            pmf = JamlCountDistribution.Convolve(
                pmf,
                JamlCountDistribution.Binomial(JamlPoolRarity.Distinct(sources.SixthSense), ordinary)
            );
            pmf = JamlCountDistribution.Convolve(
                pmf,
                JamlCountDistribution.Binomial(JamlPoolRarity.Distinct(sources.Seance), ordinary)
            );
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    public SpectralCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
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
            // empty Spectrals = category any
            var clause = _clause;
            int maxShopItem = _maxShopItem;
            int maxBoosterPack = _maxBoosterPack;
            int maxSixthSense = _maxSixthSense;
            int maxSeance = _maxSeance;
            int needed = clause.Min;
            Debug.Assert(needed > 0, "SpectralCardClause.Min must be > 0 — loader bug.");

            Vector256<int> matchCounts = Vector256<int>.Zero;
            var sources = clause.Sources ?? DefaultSources;
            // Mega / Ethereal / OmenGlobe: Arcana or pack-size rules live in scoring.
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

                // ── Spectral packs SIMD ──
                // Per-lane size (Normal=2, Jumbo/Mega=4) + ante-1 slot reachability.
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

                // ── Sixth Sense SIMD ──
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

                // ── Seance SIMD ──
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

/// <summary>
/// <c>sources:</c> block for <c>spectralCard:</c>. Colocated with <see cref="SpectralCardFilterDesc"/> (T5).
/// </summary>
public sealed record SpectralCardSourceConfig
{
    /// <summary>requireMega/requireMegaPack: both real aliases for RequireMegaPack below.</summary>
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

    /// <summary>
    /// When true, booster Spectral scoring may consume the Ethereal-tag bonus pack (second weighted slot, no natural Spectral).
    /// </summary>
    public bool EtherealTag { get; set; }

    /// <summary>
    /// When true, count Spectral substitutes from Arcana packs under Omen Globe (20% per card).
    /// Uses <c>boosterPacks</c> slots when set; otherwise walks the full late-ante pack range.
    /// Assumes Omen Globe is owned for the clause (source means search that path).
    /// </summary>
    public bool OmenGlobe { get; set; }
}
