using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// The ante-scoped families — tags, vouchers, bosses, packs, jokers, consumables, the erratic deck
/// and the starting hand — answer "how rare is this?" from pool sizes and the engine's own rates,
/// before any seed is visited. Every expectation here is written out by hand from those rates,
/// never sampled from a run and pasted back; if the maths and the runtime ever disagree, this file
/// says what the runtime was supposed to be doing, and <see cref="JamlRarityValidationTests"/> says
/// whether it is.
/// </summary>
public sealed class JamlPoolRarityTests
{
    private const double Tol = 1e-9;

    private static readonly JamlRarityContext RedWhite = JamlRarityContext.Default;
    private static readonly JamlRarityContext Ghost = new(MotelyDeck.Ghost, MotelyStake.White);
    private static readonly JamlRarityContext Zodiac = new(MotelyDeck.Zodiac, MotelyStake.White);
    private static readonly JamlRarityContext RedBlack = new(MotelyDeck.Red, MotelyStake.Black);
    private static readonly JamlRarityContext RedGold = new(MotelyDeck.Red, MotelyStake.Gold);

    /// <summary>Joker weight 20 over the default shop total 20 + 4 + 4.</summary>
    private const double ShopJoker = 20.0 / 28.0;

    private static double PackWeightSum => MotelyWeightedPools.BoosterPacks.WeightSum;

    // ── the toolkit ────────────────────────────────────────────────────────────────────────────

    /// <summary>The gate reads exactly as MeetsOccurrenceBounds: min is a floor, a set max is a ceiling at every value.</summary>
    [Fact]
    public void Window_MirrorsMeetsOccurrenceBounds()
    {
        double[] pmf = [0.1, 0.2, 0.3, 0.4];

        Assert.Equal(0.9, JamlCountDistribution.Window(pmf, 1, null), Tol);
        Assert.Equal(0.0, JamlCountDistribution.Window(pmf, 1, 0), Tol); // ceiling below the floor: empty window
        Assert.Equal(0.1, JamlCountDistribution.Window(pmf, 0, 0), Tol); // max 0 is "exactly none"
        Assert.Equal(0.3, JamlCountDistribution.Window(pmf, 2, 2), Tol);
        Assert.Equal(1.0, JamlCountDistribution.Window(pmf, 0, null), Tol);
        Assert.Equal(0.0, JamlCountDistribution.Window(pmf, 5, null), Tol);
        Assert.Equal(0.0, JamlCountDistribution.Window(pmf, 3, 2), Tol);
    }

    [Fact]
    public void Convolve_TwoCoins_IsTheTwoCoinDistribution()
    {
        double[] two = JamlCountDistribution.Convolve(
            JamlCountDistribution.Bernoulli(0.5),
            JamlCountDistribution.Bernoulli(0.5)
        );
        Assert.Equal([0.25, 0.5, 0.25], two.Select(x => Math.Round(x, 12)));
    }

    /// <summary>Whatever weight the parts do not cover is the chance the source yielded nothing.</summary>
    [Fact]
    public void Mixture_LeavesUncoveredMassOnZero()
    {
        double[] mixed = JamlCountDistribution.Mixture([(0.25, JamlCountDistribution.Binomial(2, 1.0))]);
        Assert.Equal([0.75, 0.0, 0.25], mixed.Select(x => Math.Round(x, 12)));
    }

    [Fact]
    public void Binomial_AgreesWithTheRollWindow()
    {
        double[] pmf = JamlCountDistribution.Binomial(6, 1.0 / 15.0);
        Assert.Equal(1.0, pmf.Sum(), Tol);
        Assert.Equal(
            JamlRollRarity.Window(6, 2, 4, 1.0 / 15.0),
            JamlCountDistribution.Window(pmf, 2, 4),
            Tol
        );
    }

    /// <summary>Eight of fifty-two with no two: C(48,8)/C(52,8), which is four ratios once the rest cancels.</summary>
    [Fact]
    public void Hypergeometric_MatchesTheClosedForm()
    {
        double[] pmf = JamlCountDistribution.Hypergeometric(52, 4, 8);
        double noTwo = 44.0 * 43 * 42 * 41 / (52.0 * 51 * 50 * 49);

        Assert.Equal(noTwo, pmf[0], Tol);
        Assert.Equal(1.0, pmf.Sum(), Tol);
        Assert.Equal(5, pmf.Length); // at most four twos can be dealt
    }

    // ── tags ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tag_LaterAnte_IsOneOfTheWholePool()
    {
        var clause = new TagClause { Tags = [MotelyTag.NegativeTag], Antes = [2], Rolls = [0] };
        Assert.Equal(1.0 / 24.0, TagFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    /// <summary>Ante 1 re-rolls nine tags away, so the pool is fifteen and a disallowed tag is impossible.</summary>
    [Fact]
    public void Tag_AnteOne_DrawsFromFifteen()
    {
        var charm = new TagClause { Tags = [MotelyTag.CharmTag], Antes = [1], Rolls = [0, 1] };
        Assert.Equal(1.0 - Math.Pow(14.0 / 15.0, 2), TagFilterDesc.EstimateRarity(charm, RedWhite), Tol);

        var negative = new TagClause { Tags = [MotelyTag.NegativeTag], Antes = [1], Rolls = [0] };
        Assert.Equal(0.0, TagFilterDesc.EstimateRarity(negative, RedWhite));
    }

    // ── vouchers ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Voucher_BaseAtAnteOne_IsOneOfSixteen()
    {
        var clause = new VoucherClause { Vouchers = [MotelyVoucher.Overstock], Antes = [1], Rolls = [0] };
        Assert.Equal(1.0 / 16.0, VoucherFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    [Fact]
    public void Voucher_BaseAtAnteThree_SurvivedTwoAwards()
    {
        var clause = new VoucherClause { Vouchers = [MotelyVoucher.Overstock], Antes = [3], Rolls = [0] };
        Assert.Equal(Math.Pow(15.0 / 16.0, 2) / 16.0, VoucherFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    /// <summary>An upgrade needs its base awarded first: impossible at ante 1, 1/256 at ante 2.</summary>
    [Fact]
    public void Voucher_Upgrade_NeedsItsPrerequisite()
    {
        var anteOne = new VoucherClause { Vouchers = [MotelyVoucher.OverstockPlus], Antes = [1], Rolls = [0] };
        Assert.Equal(0.0, VoucherFilterDesc.EstimateRarity(anteOne, RedWhite));

        var anteTwo = new VoucherClause { Vouchers = [MotelyVoucher.OverstockPlus], Antes = [2], Rolls = [0] };
        Assert.Equal(1.0 / 256.0, VoucherFilterDesc.EstimateRarity(anteTwo, RedWhite), Tol);
    }

    // ── bosses ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Boss_Finisher_OnlyEveryEighthAnte()
    {
        var anteOne = new BossClause { Bosses = [MotelyBossBlind.CeruleanBell], Antes = [1] };
        Assert.Equal(0.0, BossFilterDesc.EstimateRarity(anteOne, RedWhite));

        var anteEight = new BossClause { Bosses = [MotelyBossBlind.CeruleanBell], Antes = [8] };
        Assert.Equal(1.0 / 5.0, BossFilterDesc.EstimateRarity(anteEight, RedWhite), Tol);
    }

    /// <summary>Eight normal bosses are eligible at ante 1; The Ox waits for ante 6.</summary>
    [Fact]
    public void Boss_Normal_DrawsFromTheEligiblePool()
    {
        var club = new BossClause { Bosses = [MotelyBossBlind.TheClub], Antes = [1] };
        Assert.Equal(1.0 / 8.0, BossFilterDesc.EstimateRarity(club, RedWhite), Tol);

        var ox = new BossClause { Bosses = [MotelyBossBlind.TheOx], Antes = [2] };
        Assert.Equal(0.0, BossFilterDesc.EstimateRarity(ox, RedWhite));
    }

    /// <summary>
    /// The Wall at ante 3: eligible from ante 2 (pool 18 less the one seen at ante 1 = 17), so it
    /// must have been missed there, then drawn at ante 3 from 20 less the two seen.
    /// </summary>
    [Fact]
    public void Boss_Normal_MustHaveBeenMissedEarlier()
    {
        var wall = new BossClause { Bosses = [MotelyBossBlind.TheWall], Antes = [3] };
        Assert.Equal((16.0 / 17.0) * (1.0 / 18.0), BossFilterDesc.EstimateRarity(wall, RedWhite), Tol);
    }

    // ── booster packs ──────────────────────────────────────────────────────────────────────────

    /// <summary>Ante 1's first offer is a Buffoon before the PRNG is touched; the rest are weighted rolls.</summary>
    [Fact]
    public void Pack_AnteOneSlotZero_IsACertainBuffoon()
    {
        var buffoon = new BoosterPackClause { Packs = [MotelyBoosterPack.Buffoon], Antes = [1], Rolls = [0] };
        Assert.Equal(1.0, BoosterPackFilterDesc.EstimateRarity(buffoon, RedWhite), Tol);

        var arcanaAtZero = new BoosterPackClause { Packs = [MotelyBoosterPack.Arcana], Antes = [1], Rolls = [0] };
        Assert.Equal(0.0, BoosterPackFilterDesc.EstimateRarity(arcanaAtZero, RedWhite));

        var arcanaAtOne = new BoosterPackClause { Packs = [MotelyBoosterPack.Arcana], Antes = [1], Rolls = [1] };
        Assert.Equal(4.0 / PackWeightSum, BoosterPackFilterDesc.EstimateRarity(arcanaAtOne, RedWhite), Tol);
    }

    [Fact]
    public void Pack_AnteOne_HasNoSixthSlot()
    {
        var clause = new BoosterPackClause { Antes = [1], Rolls = [5] };
        Assert.Equal(0.0, BoosterPackFilterDesc.EstimateRarity(clause, RedWhite));

        var later = new BoosterPackClause { Antes = [2], Rolls = [5] };
        Assert.Equal(1.0, BoosterPackFilterDesc.EstimateRarity(later, RedWhite), Tol);
    }

    // ── jokers ─────────────────────────────────────────────────────────────────────────────────

    private static JokerSourceConfig OneShopSlot => new() { ShopItems = [0] };

    /// <summary>
    /// A wildcard <c>joker:</c> clause also counts legendaries off the soul path, with the default
    /// six pack slots — exactly what <c>CountJokerClauseOccurrences</c> does. The tests below that
    /// are about the shop alone switch that path off so the number they pin is the one they name;
    /// <see cref="Joker_Wildcard_CountsLegendariesToo"/> pins the default on its own.
    /// </summary>
    private static LegendaryJokerSourceConfig NoSoul => new();

    /// <summary>A wildcard joker clause counts legendaries as well, because the scorer does.</summary>
    [Fact]
    public void Joker_Wildcard_CountsLegendariesToo()
    {
        var withSoul = new JokerClause { Antes = [1], Sources = OneShopSlot };
        var shopOnly = new JokerClause { Antes = [1], Sources = OneShopSlot, LegendarySources = NoSoul };
        var legendary = new LegendaryJokerClause { Antes = [1] };

        double pShop = JokerFilterDesc.EstimateRarity(shopOnly, RedWhite);
        double pSoul = LegendaryJokerFilterDesc.EstimateRarity(legendary, RedWhite);

        Assert.Equal(ShopJoker, pShop, Tol);
        Assert.Equal(1.0 - (1.0 - pShop) * (1.0 - pSoul), JokerFilterDesc.EstimateRarity(withSoul, RedWhite), Tol);
    }

    /// <summary>A named common joker in one shop slot: joker weight, then 0.7, then 1 of the common pool.</summary>
    [Fact]
    public void Joker_NamedCommon_InOneShopSlot()
    {
        var clause = new JokerClause { Jokers = [MotelyJoker.Joker], Antes = [1], Sources = OneShopSlot };
        double expected = ShopJoker * 0.7 / MotelyEnum<MotelyJokerCommon>.ValueCount;
        Assert.Equal(expected, JokerFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    [Fact]
    public void Joker_RareWildcard_IsTheRarityPollAlone()
    {
        var viaJoker = new RareJokerClause { Antes = [1], Sources = OneShopSlot };
        Assert.Equal(ShopJoker * 0.05, RareJokerFilterDesc.EstimateRarity(viaJoker, RedWhite), Tol);
    }

    [Fact]
    public void Joker_Edition_IsTheBand()
    {
        var negative = new JokerClause { Antes = [1], Sources = OneShopSlot, LegendarySources = NoSoul, Edition = MotelyItemEdition.Negative };
        Assert.Equal(ShopJoker * 0.003, JokerFilterDesc.EstimateRarity(negative, RedWhite), Tol);

        var polychrome = new JokerClause { Antes = [1], Sources = OneShopSlot, LegendarySources = NoSoul, Edition = MotelyItemEdition.Polychrome };
        Assert.Equal(ShopJoker * 0.003, JokerFilterDesc.EstimateRarity(polychrome, RedWhite), Tol);
    }

    /// <summary>Stickers exist from Black; below that a sticker clause is impossible, not unknown.</summary>
    [Fact]
    public void Joker_Stickers_AreGatedByStake()
    {
        var eternal = new JokerClause { Antes = [1], Sources = OneShopSlot, LegendarySources = NoSoul, Stickers = [MotelyJokerSticker.Eternal] };
        Assert.Equal(0.0, JokerFilterDesc.EstimateRarity(eternal, RedWhite));
        Assert.Equal(ShopJoker * 0.3, JokerFilterDesc.EstimateRarity(eternal, RedBlack), Tol);

        var both = new JokerClause
        {
            Antes = [1],
            Sources = OneShopSlot,
            LegendarySources = NoSoul,
            Stickers = [MotelyJokerSticker.Eternal, MotelyJokerSticker.Perishable],
        };
        Assert.Equal(0.0, JokerFilterDesc.EstimateRarity(both, RedGold)); // one poll decides both
    }

    /// <summary>Riff-Raff, Judgement and Wraith never sticker, so a sticker clause gets zero from them.</summary>
    [Fact]
    public void Joker_StickerlessStreams_CannotSatisfyAStickerClause()
    {
        var clause = new JokerClause
        {
            Antes = [1],
            Sources = new() { RiffRaff = [0] },
            LegendarySources = NoSoul,
            Stickers = [MotelyJokerSticker.Eternal],
        };
        Assert.Equal(0.0, JokerFilterDesc.EstimateRarity(clause, RedGold));
    }

    [Fact]
    public void Joker_GhostDeck_ShrinksTheJokerShare()
    {
        var clause = new JokerClause { Antes = [1], Sources = OneShopSlot, LegendarySources = NoSoul };
        Assert.Equal(20.0 / 30.0, JokerFilterDesc.EstimateRarity(clause, Ghost), Tol);
    }

    /// <summary>Ante 1's first pack is a certain two-card Buffoon: two jokers for sure, never three.</summary>
    [Fact]
    public void Joker_AnteOneFirstPack_IsTwoCertainJokers()
    {
        var sources = new JokerSourceConfig { BoosterPacks = [0] };
        var two = new JokerClause { Antes = [1], Sources = sources, LegendarySources = NoSoul, Min = 2 };
        Assert.Equal(1.0, JokerFilterDesc.EstimateRarity(two, RedWhite), Tol);

        var three = new JokerClause { Antes = [1], Sources = sources, LegendarySources = NoSoul, Min = 3 };
        Assert.Equal(0.0, JokerFilterDesc.EstimateRarity(three, RedWhite));
    }

    // ── legendaries ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One weighted slot's chance of holding The Soul: every arcana and spectral size, weight times
    /// 1 − 0.997^cards. Written out from the pack table and card counts.
    /// </summary>
    private static double SoulPerSlot =>
        (
            4.0 * (1 - Math.Pow(0.997, 3))
            + 2.0 * (1 - Math.Pow(0.997, 5))
            + 0.5 * (1 - Math.Pow(0.997, 5))
            + 0.6 * (1 - Math.Pow(0.997, 2))
            + 0.3 * (1 - Math.Pow(0.997, 4))
            + 0.07 * (1 - Math.Pow(0.997, 4))
        ) / PackWeightSum;

    /// <summary>
    /// Ante 1 on the soul path is three weighted slots: slot 0 is the same fixed Buffoon every
    /// other family sees there, and a Buffoon never holds The Soul.
    /// </summary>
    [Fact]
    public void Legendary_AnteOne_IsThreeWeightedSlots_BehindTheFixedBuffoon()
    {
        var any = new LegendaryJokerClause { Antes = [1] };
        Assert.Equal(1.0 - Math.Pow(1.0 - SoulPerSlot, 3), LegendaryJokerFilterDesc.EstimateRarity(any, RedWhite), Tol);

        var perkeo = new LegendaryJokerClause { Jokers = [MotelyJoker.Perkeo], Antes = [1] };
        Assert.Equal(1.0 - Math.Pow(1.0 - SoulPerSlot / 5.0, 3), LegendaryJokerFilterDesc.EstimateRarity(perkeo, RedWhite), Tol);

        var soulOnly = new LegendaryJokerClause { Antes = [1], SoulCardOnly = true };
        Assert.Equal(LegendaryJokerFilterDesc.EstimateRarity(any, RedWhite), LegendaryJokerFilterDesc.EstimateRarity(soulOnly, RedWhite), Tol);

        var slotZero = new LegendaryJokerClause { Antes = [1], Sources = new() { BoosterPacks = [0] } };
        Assert.Equal(0.0, LegendaryJokerFilterDesc.EstimateRarity(slotZero, RedWhite));

        var anteZeroSlotZero = new LegendaryJokerClause { Antes = [0], Sources = new() { BoosterPacks = [0] } };
        Assert.Equal(SoulPerSlot, LegendaryJokerFilterDesc.EstimateRarity(anteZeroSlotZero, RedWhite), Tol);
    }

    [Fact]
    public void Legendary_Negative_IsTheSoulTimesTheBand()
    {
        var clause = new LegendaryJokerClause { Antes = [2], Edition = MotelyItemEdition.Negative, Sources = new() { BoosterPacks = [0] } };
        Assert.Equal(SoulPerSlot * 0.003, LegendaryJokerFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    /// <summary>A <c>joker:</c> clause naming a legendary takes the soul path, not the shop.</summary>
    [Fact]
    public void Joker_NamingALegendary_TakesTheSoulPath()
    {
        var viaJoker = new JokerClause { Jokers = [MotelyJoker.Perkeo], Antes = [1] };
        var viaLegendary = new LegendaryJokerClause { Jokers = [MotelyJoker.Perkeo], Antes = [1] };
        Assert.Equal(
            LegendaryJokerFilterDesc.EstimateRarity(viaLegendary, RedWhite),
            JokerFilterDesc.EstimateRarity(viaJoker, RedWhite),
            Tol
        );
    }

    // ── consumables ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tarot_OneShopSlot_IsTheTarotWeightThenOneOfTwentyTwo()
    {
        var fool = new TarotCardClause { Tarots = [MotelyTarotCard.TheFool], Antes = [1], Sources = new() { ShopItems = [0] } };
        Assert.Equal(4.0 / 28.0 / 22.0, TarotCardFilterDesc.EstimateRarity(fool, RedWhite), Tol);
        Assert.Equal(9.6 / 39.2 / 22.0, TarotCardFilterDesc.EstimateRarity(fool, Zodiac), Tol); // Tarot Merchant from the start

        var any = new TarotCardClause { Antes = [1], Sources = new() { ShopItems = [0] } };
        Assert.Equal(4.0 / 28.0, TarotCardFilterDesc.EstimateRarity(any, RedWhite), Tol);
    }

    [Fact]
    public void Tarot_CharmTag_IsUnmodelled()
    {
        var clause = new TarotCardClause { Antes = [1], Sources = new() { BoosterPacks = [1], CharmTag = true } };
        Assert.True(double.IsNaN(TarotCardFilterDesc.EstimateRarity(clause, RedWhite)));
    }

    [Fact]
    public void Planet_OneShopSlot_IsThePlanetWeightThenOneOfTwelve()
    {
        var clause = new PlanetCardClause { Planets = [MotelyPlanetCard.Mercury], Antes = [1], Sources = new() { ShopItems = [0] } };
        Assert.Equal(4.0 / 28.0 / 12.0, PlanetCardFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    /// <summary>Only Ghost stocks spectrals; anywhere else a shop-only spectral clause is impossible.</summary>
    [Fact]
    public void Spectral_ShopOnly_NeedsTheGhostDeck()
    {
        var clause = new SpectralCardClause { Spectrals = [MotelySpectralCard.Familiar], Antes = [1], Sources = new() { ShopItems = [0] } };
        Assert.Equal(0.0, SpectralCardFilterDesc.EstimateRarity(clause, RedWhite));
        Assert.Equal(2.0 / 30.0 / 16.0, SpectralCardFilterDesc.EstimateRarity(clause, Ghost), Tol);
    }

    [Fact]
    public void Spectral_EtherealAndOmen_AreUnmodelled()
    {
        var ethereal = new SpectralCardClause { Antes = [1], Sources = new() { BoosterPacks = [1], EtherealTag = true } };
        Assert.True(double.IsNaN(SpectralCardFilterDesc.EstimateRarity(ethereal, RedWhite)));

        var omen = new SpectralCardClause { Antes = [1], Sources = new() { BoosterPacks = [1], OmenGlobe = true } };
        Assert.True(double.IsNaN(SpectralCardFilterDesc.EstimateRarity(omen, RedWhite)));
    }

    /// <summary>
    /// Shop slots never yield a playing card on the scoring path (Magic Trick is never in the
    /// deck's starting state), so the shop-only default is a modelled impossibility — the engine's
    /// behaviour, reported rather than hidden.
    /// </summary>
    [Fact]
    public void StandardCard_ShopOnly_IsImpossible()
    {
        var clause = new StandardCardClause { Rank = MotelyStandardcardRank.Two, Antes = [1] };
        Assert.Equal(0.0, StandardCardFilterDesc.EstimateRarity(clause, RedWhite));
    }

    /// <summary>One weighted standard-pack slot at ante 2: normal is three cards, jumbo and mega five, each 1 of 13 for a rank.</summary>
    [Fact]
    public void StandardCard_OnePackSlot_IsTheWeightedCardDraws()
    {
        var clause = new StandardCardClause { Rank = MotelyStandardcardRank.Two, Antes = [2], Sources = new() { BoosterPacks = [1] } };
        double expected =
            4.0 / PackWeightSum * (1 - Math.Pow(12.0 / 13.0, 3))
            + 2.5 / PackWeightSum * (1 - Math.Pow(12.0 / 13.0, 5));
        Assert.Equal(expected, StandardCardFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    // ── the erratic deck and the starting hand ─────────────────────────────────────────────────

    [Fact]
    public void Erratic_IsFiftyTwoDrawsWithReplacement()
    {
        var rank = new ErraticRankClause { Rank = MotelyStandardcardRank.Two };
        Assert.Equal(1.0 - Math.Pow(12.0 / 13.0, 52), ErraticRankFilterDesc.EstimateRarity(rank, RedWhite), Tol);

        var suit = new ErraticSuitClause { Suit = MotelyStandardcardSuit.Spades, Min = 15 };
        Assert.Equal(JamlRollRarity.Window(52, 15, null, 0.25), ErraticSuitFilterDesc.EstimateRarity(suit, RedWhite), Tol);
    }

    [Fact]
    public void StartingDraw_IsEightOfFiftyTwoWithoutReplacement()
    {
        var clause = new StartingDrawClause { Rank = MotelyStandardcardRank.Two, Antes = [1] };
        double noTwo = 44.0 * 43 * 42 * 41 / (52.0 * 51 * 50 * 49);
        Assert.Equal(1.0 - noTwo, StartingDrawFilterDesc.EstimateRarity(clause, RedWhite), Tol);
    }

    // ── coverage ───────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> AllDiscriminators()
    {
        var data = new TheoryData<string>();
        foreach (var disc in JamlSchema.Discriminators)
            data.Add(disc);
        return data;
    }

    /// <summary>
    /// Every family but <c>pokerHand</c> now declares its odds — poker hands are best-five-of-eight,
    /// which wants an enumerated table, not a formula, and an honest NaN beats a wrong number. A new
    /// family arriving without a model fails here instead of quietly widening the report's footnote.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllDiscriminators))]
    public void EveryFamilyButPokerHand_DeclaresItsOdds(string discriminator)
    {
        var clause = JamlSchema.CreateClause(discriminator);
        double p = JamlClauseDescDispatch.EstimateRarity(clause, RedWhite);

        if (clause is AndClause or OrClause)
            Assert.True(double.IsNaN(p), "and/or are composed by the estimator, not looked up");
        else if (clause is PokerHandClause)
            Assert.True(double.IsNaN(p), "pokerHand is deliberately unmodelled");
        else
            Assert.False(double.IsNaN(p), $"{discriminator} has no rarity model");
    }
}
