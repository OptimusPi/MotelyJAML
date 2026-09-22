namespace Motely.Filters.Jaml;

/// <summary>
/// Rarity for the five joker families, read off the same streams the scorer walks in
/// <c>JamlScoring.CountJokerOccurrencesGeneric</c> / <c>CountSpecialtyJokerSources</c> /
/// <c>LegendarySoulMatcher</c>: a shop slot is a joker with weight 20 of the shop total, then a
/// rarity poll (0.7 / 0.25 / 0.05) and a uniform pick from that rarity's pool; a buffoon pack is
/// two to five such picks behind a weighted pack roll; the specialty streams are fixed-rarity
/// picks with no rarity poll; and a legendary is The Soul (0.003 per arcana or spectral card)
/// followed by a uniform pick of five.
/// <para>
/// Every source yields a count pmf and the sources convolve, so a clause that mixes shop slots
/// with buffoon packs, or legendary names with common ones, has one distribution its
/// <c>min</c>/<c>max</c> window is read from — exactly the total the scorer compares against.
/// </para>
/// </summary>
internal static class JamlJokerRarity
{
    /// <summary>Share of all-rarity joker rolls that come up <paramref name="rarity"/>, from the 0.95 / 0.7 poll bands.</summary>
    public static double RarityShare(MotelyJokerRarity rarity) =>
        rarity switch
        {
            MotelyJokerRarity.Common => 0.7,
            MotelyJokerRarity.Uncommon => 0.25,
            MotelyJokerRarity.Rare => 0.05,
            _ => 0.0, // legendaries never come off a rarity poll
        };

    /// <summary>How many jokers a uniform pick of <paramref name="rarity"/> chooses among.</summary>
    public static int PoolSize(MotelyJokerRarity rarity) =>
        rarity switch
        {
            MotelyJokerRarity.Common => MotelyEnum<MotelyJokerCommon>.ValueCount,
            MotelyJokerRarity.Uncommon => MotelyEnum<MotelyJokerUncommon>.ValueCount,
            MotelyJokerRarity.Rare => MotelyEnum<MotelyJokerRare>.ValueCount,
            MotelyJokerRarity.Legendary => MotelyEnum<MotelyJokerLegendary>.ValueCount,
            _ => 0,
        };

    /// <summary>The rarity bits a <see cref="MotelyJoker"/> carries.</summary>
    public static MotelyJokerRarity RarityOf(MotelyJoker joker) =>
        (MotelyJokerRarity)((int)joker & MotelyGlobals.JokerRarityMask);

    /// <summary>
    /// The chance one joker drawn off a stream satisfies the clause. <paramref name="streamRarity"/>
    /// is null for an all-rarity stream (shop, buffoon pack, Judgement, Wraith, <c>allShopJokers</c>),
    /// where a named joker pays its rarity's poll share; a fixed-rarity stream pays nothing for
    /// rarity but can only ever produce its own. <paramref name="stickered"/> says whether the
    /// stream applies stickers at all — Judgement, Wraith and Riff-Raff do not, so a clause that
    /// asks for one gets a flat zero from them rather than an unmodelled shrug.
    /// </summary>
    public static double MatchShare(
        MotelyJoker[] named,
        MotelyJokerRarity? wildcardRarity,
        MotelyJokerRarity? streamRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        bool stickered,
        MotelyStake stake
    )
    {
        double editionShare = JamlPoolRarity.JokerEditionShare(edition);

        if (named.Length == 0)
        {
            double rarityShare =
                streamRarity is { } fixedRarity
                    ? (wildcardRarity is null || wildcardRarity == fixedRarity ? 1.0 : 0.0)
                    : (wildcardRarity is { } wanted ? RarityShare(wanted) : 1.0);

            // A wildcard cannot know which jokers it will see, so it cannot apply CanBeEternal;
            // the handful of excluded names inflate an Eternal wildcard by under a tenth.
            double stickerShare = stickered
                ? JamlPoolRarity.StickerShare(stickers, stake, canBeEternal: true)
                : (JamlPoolRarity.WantsAnySticker(stickers) ? 0.0 : 1.0);

            return rarityShare * editionShare * stickerShare;
        }

        double share = 0.0;
        HashSet<MotelyJoker> seen = [];
        foreach (var joker in named)
        {
            if (!seen.Add(joker))
                continue; // the scorer matches each slot item once per distinct type

            var rarity = RarityOf(joker);
            if (rarity == MotelyJokerRarity.Legendary)
                continue; // legendaries ride the soul path, never a rarity poll

            double rarityShare =
                streamRarity is { } fixedRarity
                    ? (rarity == fixedRarity ? 1.0 : 0.0)
                    : RarityShare(rarity);
            if (rarityShare <= 0.0)
                continue;

            double stickerShare = stickered
                ? JamlPoolRarity.StickerShare(
                    stickers,
                    stake,
                    MotelySingleSearchContext.CanBeEternal(new MotelyItem(joker))
                )
                : (JamlPoolRarity.WantsAnySticker(stickers) ? 0.0 : 1.0);

            share += rarityShare / PoolSize(rarity) * editionShare * stickerShare;
        }
        return share;
    }

    /// <summary>
    /// The count of matching non-legendary jokers across <paramref name="antes"/> and every source
    /// in <paramref name="sources"/>, as a pmf. Mirrors <c>CountJokerOccurrencesGeneric</c> and
    /// <c>CountSpecialtyJokerSources</c> source by source: shop slots are one all-rarity stickered
    /// draw each; buffoon pack slots are a weighted pack roll then that many draws (the certain
    /// Buffoon at ante 1 slot 0 is two draws, no roll); each specialty index is one draw on its
    /// own stream. Duplicate re-rolls inside a pack are ignored (binomial, not hypergeometric).
    /// </summary>
    public static double[] Distribution(
        int[] antes,
        JokerSourceConfig sources,
        MotelyJoker[] named,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        in JamlRarityContext ctx
    )
    {
        MotelyStake stake = ctx.Stake; // an `in` parameter cannot be captured by the local function
        double Share(MotelyJokerRarity? streamRarity, bool stickered) =>
            MatchShare(named, wildcardRarity, streamRarity, edition, stickers, stickered, stake);

        double jokerSlotShare = ctx.ShopJokerRate / ctx.ShopTotalRate;
        double shopShare = jokerSlotShare * Share(null, stickered: true);
        double packCardShare = Share(null, stickered: true);

        double[] pmf = JamlCountDistribution.Zero;

        foreach (int ante in antes)
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

                double[] slotPmf = JamlPoolRarity.SlotIsFixedBuffoon(ante, slot)
                    ? (
                        sources.RequireMegaPack
                            ? JamlCountDistribution.Zero
                            : JamlCountDistribution.Binomial(
                                MotelyBoosterPackType.Buffoon.GetCardCount(MotelyBoosterPackSize.Normal),
                                packCardShare
                            )
                    )
                    : JamlPoolRarity.PackSlotCards(
                        MotelyBoosterPackType.Buffoon,
                        packCardShare,
                        sources.RequireMegaPack
                    );
                pmf = JamlCountDistribution.Convolve(pmf, slotPmf);
            }

            pmf = Specialty(pmf, sources.Judgement, Share(null, stickered: false));
            pmf = Specialty(pmf, sources.Wraith, Share(null, stickered: false));
            pmf = Specialty(pmf, sources.RiffRaff, Share(MotelyJokerRarity.Common, stickered: false));
            pmf = Specialty(pmf, sources.RareTag, Share(MotelyJokerRarity.Rare, stickered: true));
            pmf = Specialty(pmf, sources.UncommonTag, Share(MotelyJokerRarity.Uncommon, stickered: true));
            pmf = Specialty(pmf, sources.CommonShopJokers, Share(MotelyJokerRarity.Common, stickered: true));
            pmf = Specialty(pmf, sources.UncommonShopJokers, Share(MotelyJokerRarity.Uncommon, stickered: true));
            pmf = Specialty(pmf, sources.RareShopJokers, Share(MotelyJokerRarity.Rare, stickered: true));
            pmf = Specialty(pmf, sources.AllShopJokers, Share(null, stickered: true));
        }

        return pmf;
    }

    private static double[] Specialty(double[] pmf, int[] rolls, double share) =>
        rolls.Length == 0
            ? pmf
            : JamlCountDistribution.Convolve(
                pmf,
                JamlCountDistribution.Binomial(JamlPoolRarity.Distinct(rolls), share)
            );

    /// <summary>
    /// The count of legendary matches across the clause's antes, as a pmf. Mirrors
    /// <see cref="LegendarySoulMatcher.CountAnte"/>: every reachable pack slot is a weighted roll
    /// (that path opens the stream with the first pack already generated, so ante 1 slot 0 is a
    /// roll, not the certain Buffoon); a targeted arcana or spectral pack holds The Soul with
    /// 0.003 per card; The Soul then names one of the five legendaries uniformly, with an
    /// edition off the soul stream. Each slot yields at most one match.
    /// </summary>
    public static double[] LegendaryDistribution(LegendaryJokerClause clause, in JamlRarityContext ctx)
    {
        var src = clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources;
        bool split = src.ArcanaPacks.Length > 0 || src.SpectralPacks.Length > 0;

        double soulShare;
        if (clause.SoulCardOnly)
        {
            soulShare = 1.0;
        }
        else
        {
            int legendaryNames = 0;
            HashSet<MotelyJoker> seen = [];
            foreach (var joker in JamlDisc.OrEmpty(clause.Jokers))
                if (RarityOf(joker) == MotelyJokerRarity.Legendary && seen.Add(joker))
                    legendaryNames++;

            soulShare =
                JamlPoolRarity.PoolShare(
                    legendaryNames,
                    PoolSize(MotelyJokerRarity.Legendary),
                    any: JamlDisc.IsCategoryAny(clause.Jokers)
                ) * JamlPoolRarity.JokerEditionShare(clause.Edition);
        }

        const double SoulPerCard = 0.003; // GetNext*PackHasTheSoul: poll > 0.997

        double[] pmf = JamlCountDistribution.Zero;
        foreach (int ante in clause.Antes)
        {
            for (int slot = 0; slot <= MotelyGlobals.LateAntesMaxPackSlot; slot++)
            {
                if (!JamlPoolRarity.SlotIsReachable(ante, slot))
                    continue;

                bool arcana = JamlPoolRarity.Contains(split ? src.ArcanaPacks : src.BoosterPacks, slot);
                bool spectral = JamlPoolRarity.Contains(split ? src.SpectralPacks : src.BoosterPacks, slot);

                double slotShare = 0.0;
                if (arcana)
                    slotShare += JamlPoolRarity.PackSlotHasAny(MotelyBoosterPackType.Arcana, SoulPerCard, src.RequireMegaPack);
                if (spectral)
                    slotShare += JamlPoolRarity.PackSlotHasAny(MotelyBoosterPackType.Spectral, SoulPerCard, src.RequireMegaPack);

                pmf = JamlCountDistribution.Convolve(pmf, JamlCountDistribution.Bernoulli(slotShare * soulShare));
            }
        }

        return pmf;
    }

    /// <summary>
    /// The whole story for a <c>joker:</c> clause, which may name legendaries beside ordinary
    /// jokers or, as a wildcard, count both kinds — exactly the split
    /// <c>CountJokerClauseOccurrences</c> makes. The two paths convolve, because the clause's
    /// window applies to their sum.
    /// </summary>
    public static double EstimateJoker(JokerClause clause, in JamlRarityContext ctx)
    {
        var sources = clause.Sources ?? JokerFilterDesc.DefaultSources;
        var jokers = JamlDisc.OrEmpty(clause.Jokers);
        bool any = JamlDisc.IsCategoryAny(jokers);

        MotelyJoker[] ordinary = any ? [] : Array.FindAll(jokers, j => RarityOf(j) != MotelyJokerRarity.Legendary);
        MotelyJoker[] legendary = any ? [] : Array.FindAll(jokers, j => RarityOf(j) == MotelyJokerRarity.Legendary);

        double[] pmf = JamlCountDistribution.Zero;

        if (any || ordinary.Length > 0)
            pmf = Distribution(clause.Antes, sources, ordinary, wildcardRarity: null, clause.Edition, clause.Stickers, in ctx);

        if (any || legendary.Length > 0)
        {
            var soul = new LegendaryJokerClause
            {
                Jokers = legendary,
                Edition = clause.Edition,
                Sources = clause.LegendarySources ?? LegendaryJokerFilterDesc.DefaultSources,
                Antes = clause.Antes,
                Min = clause.Min,
                Max = clause.Max,
            };
            pmf = JamlCountDistribution.Convolve(pmf, LegendaryDistribution(soul, in ctx));
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    /// <summary>A single-rarity family: its names are already that rarity, its wildcard is rarity-gated.</summary>
    public static double EstimateFixedRarity<TJoker>(
        int[] antes,
        JokerSourceConfig? sources,
        TJoker[] named,
        MotelyJokerRarity rarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        int min,
        int? max,
        in JamlRarityContext ctx
    )
        where TJoker : struct, Enum
    {
        var jokers = new MotelyJoker[named.Length];
        for (int i = 0; i < named.Length; i++)
            jokers[i] = (MotelyJoker)((int)rarity | Convert.ToInt32(named[i]));

        double[] pmf = Distribution(
            antes,
            sources ?? JokerFilterDesc.DefaultSources,
            jokers,
            wildcardRarity: rarity,
            edition,
            stickers,
            in ctx
        );
        return JamlCountDistribution.Window(pmf, min, max);
    }
}
