namespace Motely.Tests;

/// <summary>
/// Smoke: scalar JamlScoring paths golden tests skip. Asserts list batch ran and scored callback
/// fired. Exact scores/seeds live in golden/behavior tests.
/// </summary>
public sealed class JamlScoringCoverageTests
{
    private static readonly string[] Seeds = ["ALEEB", "MOTELY77"];

    private static int RunShould(IJamlClause clause, MotelyDeck deck = MotelyDeck.Red)
    {
        if (clause.Score == 0)
            clause.Score = 1;
        var config = new JamlConfig
        {
            Id = "scoring-cov",
            Deck = deck,
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
        Assert.True(search.TotalSeedsSearched >= 1, "scoring smoke must run the list batch");
        return score;
    }

    [Fact]
    public void Tarot_AllSources_Scores()
    {
        var score = RunShould(
            new TarotCardClause
            {
                Tarots = [MotelyTarotCard.TheFool, MotelyTarotCard.TheMagician],
                Antes = [1, 2],
                Sources = new TarotCardSourceConfig
                {
                    ShopItems = [0, 1, 2, 3],
                    BoosterPacks = [0, 1],
                    Emperor = [0, 1],
                    PurpleSealOrEightBall = [0, 1],
                    CharmTag = true,
                },
            }
        );
        Assert.True(score >= 0, "scoring callback must fire");
    }

    [Theory]
    [InlineData(MotelyDeck.Red)]
    [InlineData(MotelyDeck.Ghost)]
    public void Spectral_AllSources_Scores(MotelyDeck deck)
    {
        var score = RunShould(
            new SpectralCardClause
            {
                Spectrals = [MotelySpectralCard.Familiar, MotelySpectralCard.Grim],
                Antes = [1, 2],
                Sources = new SpectralCardSourceConfig
                {
                    ShopItems = [0, 1, 2, 3],
                    BoosterPacks = [0, 1],
                    SixthSense = [0, 1],
                    Seance = [0, 1],
                    EtherealTag = true,
                },
            },
            deck
        );
        Assert.True(score >= 0, "scoring callback must fire");
    }

    [Fact]
    public void Spectral_MegaPackOnly_Scores()
    {
        var score = RunShould(
            new SpectralCardClause
            {
                Spectrals = [MotelySpectralCard.Immolate],
                Antes = [1],
                Sources = new SpectralCardSourceConfig
                {
                    BoosterPacks = [0, 1],
                    RequireMegaPack = true,
                },
            }
        );
        Assert.True(score >= 0, "scoring callback must fire");
    }

    [Fact]
    public void SpecialSpectrals_SoulAndBlackHole_Score()
    {
        Assert.True(RunShould(new SpectralCardClause { Spectrals = [MotelySpectralCard.TheSoul], Antes = [1, 2] }) >= 0, "scoring callback must fire");
        Assert.True(RunShould(new SpectralCardClause { Spectrals = [MotelySpectralCard.BlackHole], Antes = [1, 2] }) >= 0, "scoring callback must fire");
    }

    [Fact]
    public void OrClause_ScoredAndUnscored_Score()
    {
        var scored = RunShould(
            new OrClause
            {
                Score = 5,
                Min = 1,
                Clauses =
                [
                    new JokerClause { Antes = [1] },
                    new TagClause { Tags = [MotelyTag.RareTag], Rolls = [0], Antes = [1] },
                ],
            }
        );
        Assert.True(scored >= 0, "scoring callback must fire");

        // Score stays 0 inside the child so CountOrOccurrences takes the matched-count branch.
        var unscoredChildren = RunShould(
            new OrClause
            {
                Min = 1,
                Clauses =
                [
                    new VoucherClause { Vouchers = [MotelyVoucher.Overstock], Rolls = [0], Antes = [1] },
                    new JokerClause { Antes = [1] },
                ],
            }
        );
        Assert.True(unscoredChildren >= 0, "scoring callback must fire");
    }

    [Fact]
    public void OrClause_MinTwo_RequiresBothBranches()
    {
        var score = RunShould(
            new OrClause
            {
                Min = 2,
                Score = 3,
                Clauses =
                [
                    new JokerClause { Antes = [1] },
                    new TagClause { Tags = [MotelyTag.NegativeTag], Rolls = [0, 1], Antes = [1, 2] },
                ],
            }
        );
        Assert.True(score >= 0, "scoring callback must fire");
    }

    [Fact]
    public void AndClause_MinOfChildren_Scores()
    {
        var score = RunShould(
            new AndClause
            {
                Score = 2,
                Clauses =
                [
                    new JokerClause { Antes = [1] },
                    new OrClause
                    {
                        Min = 1,
                        Clauses =
                        [
                            new TagClause { Tags = [MotelyTag.SpeedTag], Rolls = [0], Antes = [1] },
                            new TagClause { Tags = [MotelyTag.CharmTag], Rolls = [0], Antes = [1] },
                        ],
                    },
                ],
            }
        );
        Assert.True(score >= 0, "scoring callback must fire");
    }

    [Fact]
    public void Events_SpreadRollsWithMax_ExerciseFullLoops()
    {
        // Rolls beyond index 0 force the skip loops; Max forces the counter to keep scanning
        // instead of returning at min. Every event counter in JamlScoring takes both branches.
        IJamlClause[] clauses =
        [
            new LuckyMoneyClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new LuckyMultClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new MisprintMultClause { Rolls = [0, 2, 5], Mult = 5, Min = 1, Max = 3 },
            new WheelOfFortuneClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new CavendishExtinctClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new GrosMichelExtinctClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new SpaceLevelupClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new BusinessPayoutClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new BloodstoneTriggerClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new ParkingPayoutClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new GlassDestroyClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
            new WheelStaysFlippedClause { Rolls = [0, 2, 5], Min = 1, Max = 3 },
        ];

        foreach (var clause in clauses)
            Assert.True(RunShould(clause) >= 0, "scoring callback must fire");
    }

    [Fact]
    public void Voucher_MultiRollMultiAnte_Scores()
    {
        var score = RunShould(
            new VoucherClause
            {
                Vouchers = [MotelyVoucher.Overstock, MotelyVoucher.Grabber],
                Rolls = [0, 1, 2],
                Antes = [1, 2],
            }
        );
        Assert.True(score >= 0, "scoring callback must fire");
    }

    [Fact]
    public void Tarot_DefaultSources_Score()
    {
        // No Sources at all — the DefaultSources path in the scoring counter.
        var score = RunShould(
            new TarotCardClause { Tarots = [MotelyTarotCard.Death], Antes = [1, 2, 3] }
        );
        Assert.True(score >= 0, "scoring callback must fire");
    }
}
