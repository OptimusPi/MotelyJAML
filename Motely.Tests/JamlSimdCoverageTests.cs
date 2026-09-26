using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// Smoke: every FilterDesc family through Must (SIMD) and Should (scalar) with C# objects.
/// Asserts the list batch ran and Should delivers a scored callback. Find-proof is golden tests.
/// </summary>
public class JamlSimdCoverageTests
{
    private static readonly string[] Seeds = ["MOTELY77"];

    /// <summary>SIMD Must path: batch must run (no MatchingSeeds pin).</summary>
    private static void RunMust(IJamlClause clause)
    {
        var config = new JamlConfig
        {
            Id = "cov-must",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);

        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true);

        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.True(search.TotalSeedsSearched >= 1, "Must path must run the SIMD filter over the list batch");
    }

    /// <summary>Scalar Should path: scoring callback must fire for this seed list.</summary>
    private static void RunShould(IJamlClause clause)
    {
        clause.Score = 1;
        var config = new JamlConfig
        {
            Id = "cov-should",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Should.Add(clause);

        int score = -1;
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result => score = result.Score);

        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.True(search.TotalSeedsSearched >= 1, "Should path must run the list batch");
        Assert.True(score >= 0, "scoring callback must fire (seed pin lives in golden tests)");
    }

    private static void ExerciseBoth(IJamlClause must, IJamlClause should)
    {
        RunMust(must);
        RunShould(should);
    }

    [Fact]
    public void Jokers_AllRarities_FilterAndScore()
    {
        ExerciseBoth(
            new JokerClause { Antes = [1, 2] },
            new JokerClause { Jokers = [MotelyJoker.Blueprint], Antes = [1, 2] }
        );
        ExerciseBoth(
            new CommonJokerClause { Antes = [1] },
            new CommonJokerClause { Antes = [1] }
        );
        ExerciseBoth(
            new UncommonJokerClause
            {
                Antes = [1],
                // exercise the fast-path rarity stream branch
                Sources = new JokerSourceConfig
                {
                    ShopItems = [0, 1],
                    BoosterPacks = [0],
                    UncommonShopJokers = [0],
                },
            },
            new UncommonJokerClause { Antes = [1] }
        );
        ExerciseBoth(
            new RareJokerClause { Antes = [1] },
            new RareJokerClause { Antes = [1] }
        );
        ExerciseBoth(
            new LegendaryJokerClause
            {
                Antes = [1],
                Sources = new LegendaryJokerSourceConfig { ArcanaPacks = [0], SpectralPacks = [0] },
            },
            new LegendaryJokerClause
            {
                Antes = [1],
                Sources = new LegendaryJokerSourceConfig { ArcanaPacks = [0], SpectralPacks = [0] },
            }
        );
        // T4: edition prefilter path inside LegendaryJokerFilterDesc (LegendarySoulEditionPrefilter).
        ExerciseBoth(
            new LegendaryJokerClause
            {
                Jokers = [MotelyJoker.Perkeo],
                Edition = MotelyItemEdition.Negative,
                Antes = [1],
                Min = 1,
            },
            new LegendaryJokerClause
            {
                Jokers = [MotelyJoker.Perkeo],
                Edition = MotelyItemEdition.Negative,
                Antes = [1],
                Min = 1,
            }
        );
    }

    [Fact]
    public void Cards_AllTypes_FilterAndScore()
    {
        // Tarot: shop + arcana pack + Emperor + Purple Seal — hits every source branch.
        ExerciseBoth(
            new TarotCardClause
            {
                Tarots = [MotelyTarotCard.TheFool],
                Antes = [1],
                Sources = new TarotCardSourceConfig
                {
                    ShopItems = [0, 1],
                    BoosterPacks = [0],
                    Emperor = [0],
                    PurpleSealOrEightBall = [0],
                },
            },
            new TarotCardClause { Tarots = [MotelyTarotCard.TheFool], Antes = [1] }
        );

        ExerciseBoth(
            new SpectralCardClause
            {
                Spectrals = [MotelySpectralCard.Familiar],
                Antes = [1],
                Sources = new SpectralCardSourceConfig
                {
                    ShopItems = [0],
                    BoosterPacks = [0],
                    SixthSense = [0],
                    Seance = [0],
                },
            },
            new SpectralCardClause { Spectrals = [MotelySpectralCard.Familiar], Antes = [1] }
        );

        // T4: Soul/BlackHole take SpecialSpectralCardFilterDesc (pack-type narrow + scalar confirm).
        ExerciseBoth(
            new SpectralCardClause { Spectrals = [MotelySpectralCard.TheSoul], Antes = [1] },
            new SpectralCardClause { Spectrals = [MotelySpectralCard.TheSoul], Antes = [1] }
        );
        ExerciseBoth(
            new SpectralCardClause { Spectrals = [MotelySpectralCard.BlackHole], Antes = [1] },
            new SpectralCardClause { Spectrals = [MotelySpectralCard.BlackHole], Antes = [1] }
        );

        ExerciseBoth(
            new PlanetCardClause { Planets = [MotelyPlanetCard.Mercury], Antes = [1] },
            new PlanetCardClause { Planets = [MotelyPlanetCard.Mercury], Antes = [1] }
        );

        ExerciseBoth(
            new StandardCardClause
            {
                Rank = MotelyStandardcardRank.Two,
                Suit = MotelyStandardcardSuit.Spades,
                Antes = [1],
            },
            new StandardCardClause { Rank = MotelyStandardcardRank.Two, Antes = [1] }
        );

        ExerciseBoth(
            new ErraticRankClause { Rank = MotelyStandardcardRank.Two, Antes = [1] },
            new ErraticRankClause { Rank = MotelyStandardcardRank.Two, Antes = [1] }
        );

        ExerciseBoth(
            new ErraticSuitClause { Suit = MotelyStandardcardSuit.Spades, Antes = [1] },
            new ErraticSuitClause { Suit = MotelyStandardcardSuit.Spades, Antes = [1] }
        );
    }

    [Fact]
    public void Features_FilterAndScore()
    {
        ExerciseBoth(
            new VoucherClause
            {
                Vouchers = [MotelyVoucher.Overstock],
                Rolls = [0],
                Antes = [1],
            },
            new VoucherClause
            {
                Vouchers = [MotelyVoucher.Overstock],
                Rolls = [0],
                Antes = [1],
            }
        );
        ExerciseBoth(
            new TagClause
            {
                Tags = [MotelyTag.RareTag],
                Rolls = [0],
                Antes = [1],
            },
            new TagClause
            {
                Tags = [MotelyTag.RareTag],
                Rolls = [0],
                Antes = [1],
            }
        );
        ExerciseBoth(
            new BossClause { Bosses = [MotelyBossBlind.CeruleanBell], Antes = [1] },
            new BossClause { Bosses = [MotelyBossBlind.CeruleanBell], Antes = [1] }
        );
        ExerciseBoth(
            new StartingDrawClause { Rank = MotelyStandardcardRank.Two, Antes = [1] },
            new StartingDrawClause { Rank = MotelyStandardcardRank.Two, Antes = [1] }
        );
    }

    [Fact]
    public void Events_FilterAndScore()
    {
        IJamlClause[] Make() =>
            [
                new LuckyMoneyClause { Rolls = [0, 1] },
                new LuckyMultClause { Rolls = [0, 1] },
                new MisprintMultClause { Rolls = [0, 1], Mult = 1 },
                new WheelOfFortuneClause { Rolls = [0] },
                new CavendishExtinctClause { Rolls = [0] },
                new GrosMichelExtinctClause { Rolls = [0] },
                new SpaceLevelupClause { Rolls = [0] },
                new BusinessPayoutClause { Rolls = [0] },
                new BloodstoneTriggerClause { Rolls = [0] },
                new ParkingPayoutClause { Rolls = [0] },
                new GlassDestroyClause { Rolls = [0] },
                new WheelStaysFlippedClause { Rolls = [0] },
            ];

        var must = Make();
        var should = Make();
        for (int i = 0; i < must.Length; i++)
            ExerciseBoth(must[i], should[i]);
    }
}
