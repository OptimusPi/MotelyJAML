using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("standardCard", "standardCards",
    SourceConfigType = typeof(StandardCardSourceConfig))]
public sealed class StandardCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
    public MotelyItemEnhancement? Enhancement { get; set; }
    public MotelyItemSeal? Seal { get; set; }
    public MotelyItemEdition? Edition { get; set; }

    // null = no sources: in JAML → filter DefaultSources at CreateFilter/score (not parse).
    public StandardCardSourceConfig? Sources { get; set; }
}

public struct StandardCardFilterDesc(StandardCardClause clause)
    : IMotelySeedFilterDesc<StandardCardFilterDesc.StandardCardFilter>,
      IJamlClauseDesc<StandardCardClause>
{
    private readonly StandardCardClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["standardCard", "standardCards"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources", "rank", "suit", "enhancement", "seal", "edition"];

    /// <inheritdoc/>
    public static bool Set(StandardCardClause clause, string key, IJamlValueReader value)
    {
        switch (key.ToLowerInvariant())
        {
            case "rank":
                if (!value.TryEnum<MotelyStandardcardRank>(out var rank)) return false;
                clause.Rank = rank;
                return true;
            case "suit":
                if (!value.TryEnum<MotelyStandardcardSuit>(out var suit)) return false;
                clause.Suit = suit;
                return true;
            case "enhancement":
                if (!value.TryEnum<MotelyItemEnhancement>(out var enh)) return false;
                clause.Enhancement = enh;
                return true;
            case "seal":
                if (!value.TryEnum<MotelyItemSeal>(out var seal)) return false;
                clause.Seal = seal;
                return true;
            case "edition":
                if (!value.TryEnum<MotelyItemEdition>(out var edition)) return false;
                clause.Edition = edition;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Filter-layer default when Sources is null. Shop only; packs/specialty need explicit sources:.
    /// </summary>
    internal static readonly StandardCardSourceConfig DefaultSources = new()
    {
        ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
    };

    /// <summary>
    /// A standard-pack card is a uniform 1 of 52 for its face, then 40% to carry one of the eight
    /// enhancements, an edition off the 0.92 / 0.04 / 0.028 / 0.012 bands (never Negative), and
    /// 20% to carry one of the four seals — <c>GetNextStandardCard</c>. A shop card is bare: face
    /// only, no enhancement, edition or seal, and it appears with the Magic Trick weight, which no
    /// deck starts with — so on the engine's scoring path a shop slot never yields a playing card
    /// at all. The Certificate, Incantation, Familiar, Grim and deck-draw sources are not modelled;
    /// a clause naming any of them is reported as unmodelled rather than undercounted.
    /// </summary>
    public static double EstimateRarity(StandardCardClause clause, in JamlRarityContext ctx)
    {
        var sources = clause.Sources ?? DefaultSources;
        if (
            sources.Certificate.Length > 0
            || sources.Incantation.Length > 0
            || sources.Familiar.Length > 0
            || sources.Grim.Length > 0
            || sources.DeckDraw.Length > 0
        )
            return double.NaN;

        // Face: the share of the 52-card pool whose rank and suit the clause accepts, judged the
        // way MatchStandardCard judges a drawn card.
        int faces = 0;
        foreach (var card in MotelyEnum<MotelyStandardCard>.Values)
        {
            var item = new MotelyItem(card);
            if (clause.Rank.HasValue && item.StandardcardRank != clause.Rank.Value)
                continue;
            if (clause.Suit.HasValue && item.StandardcardSuit != clause.Suit.Value)
                continue;
            faces++;
        }
        double face = faces / (double)MotelyEnum<MotelyStandardCard>.ValueCount;

        double enhancement = clause.Enhancement switch
        {
            null => 1.0,
            MotelyItemEnhancement.None => 0.6,
            _ => 0.4 / (MotelyEnum<MotelyItemEnhancement>.ValueCount - 1),
        };
        double seal = clause.Seal switch
        {
            null => 1.0,
            MotelyItemSeal.None => 0.8,
            _ => 0.2 / (MotelyEnum<MotelyItemSeal>.ValueCount - 1),
        };
        double edition = clause.Edition switch
        {
            null => 1.0,
            MotelyItemEdition.None => 0.92,
            MotelyItemEdition.Foil => 0.04,
            MotelyItemEdition.Holographic => 0.028,
            MotelyItemEdition.Polychrome => 0.012,
            _ => 0.0,
        };
        double packCardShare = face * enhancement * seal * edition;

        bool bareCardFits =
            clause.Enhancement is null or MotelyItemEnhancement.None
            && clause.Seal is null or MotelyItemSeal.None
            && clause.Edition is null or MotelyItemEdition.None;
        double shopShare = ctx.ShopStandardCardRate / ctx.ShopTotalRate * (bareCardFits ? face : 0.0);

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
                    continue; // ante 1's first offer is a Buffoon, never a standard pack
                pmf = JamlCountDistribution.Convolve(
                    pmf,
                    JamlPoolRarity.PackSlotCards(
                        MotelyBoosterPackType.Standard,
                        packCardShare,
                        sources.RequireMegaPack
                    )
                );
            }
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    public StandardCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
        {
            ctx.CacheShopStream(ante);
            ctx.CacheBoosterPackStream(ante);
        }

        return new StandardCardFilter(_clause);
    }

    public struct StandardCardFilter(StandardCardClause clause) : IMotelySeedFilter
    {
        private readonly StandardCardClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // Single match core: same PrepareRunState + count as should-scoring.
            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}

/// <summary>
/// <c>sources:</c> block for <c>standardCard:</c>. Colocated with <see cref="StandardCardFilterDesc"/> (T5).
/// </summary>
public sealed record StandardCardSourceConfig
{
    /// <summary>requireMega/requireMegaPack: both real aliases for RequireMegaPack below.</summary>
    public static readonly string[] SourceKeys =
    [
        "shopItems",
        "boosterPacks",
        "certificate",
        "incantation",
        "familiar",
        "grim",
        "deckDraw",
        "requireMega",
        "requireMegaPack",
    ];

    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];

    /// <summary>When true, only Mega-sized Standard packs count (Normal/Jumbo still advance the stream).</summary>
    public bool RequireMegaPack { get; set; }

    public int[] Certificate { get; set; } = [];
    public int[] Incantation { get; set; } = [];
    public int[] Familiar { get; set; } = [];
    public int[] Grim { get; set; } = [];
    public int[] DeckDraw { get; set; } = [];
}
