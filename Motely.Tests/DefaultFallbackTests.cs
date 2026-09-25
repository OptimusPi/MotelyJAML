using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

public class DefaultFallbackTests
{
    private const string Seed = "MOTELY77";

    private static (long Matching, int Score) Score(JokerClause clause)
    {
        var config = new JamlConfig
        {
            Id = "default-fallback",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Should.Add(clause);

        int score = 0;
        long matching = 0;
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator([Seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result => score = result.Score);

        using var search = settings.Start();
        search.AwaitCompletion();
        matching = search.MatchingSeeds;
        return (matching, score);
    }

    [Fact]
    public void SourcelessWildcardJoker_DefaultsToAllAntesAndShopOnly()
    {
        var (implicitMatching, implicitScore) = Score(
            new JokerClause { Score = 1 }
        );

        var (_, explicitScore) = Score(
            new JokerClause
            {
                Score = 1,
                Antes = [1, 2, 3, 4, 5, 6, 7, 8],
                Sources = new JokerSourceConfig
                {
                    ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
                },
            }
        );

        Assert.True(
            implicitScore > 0,
            "a sourceless wildcard joker must match jokers, not nothing"
        );
        Assert.Equal(explicitScore, implicitScore);
        Assert.Equal(1, implicitMatching);
    }

    [Fact]
    public void ExplicitSources_AreNotOverwrittenByDefaults()
    {
        var (_, narrowScore) = Score(
            new JokerClause
            {
                Score = 1,
                Antes = [1],
                Sources = new JokerSourceConfig { ShopItems = [0] },
            }
        );

        var (_, wideScore) = Score(new JokerClause { Score = 1 });

        Assert.True(
            wideScore >= narrowScore,
            "the all-antes default must cover at least the single-slot case"
        );
    }

    private static JamlConfig LabelConfig(params IJamlClause[] should)
    {
        var config = new JamlConfig
        {
            Id = "tally-labels",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        foreach (var clause in should)
            config.Should.Add(clause);
        return config;
    }

    [Fact]
    public void TallyLabels_ExplicitLabelWins()
    {
        var clause = new JokerClause { Jokers = [MotelyJoker.Blueprint], Label = "bp" };
        var plan = JamlSearchBuilder.CreatePlan(LabelConfig(clause));
        Assert.Equal(["bp"], plan.TallyLabels);
    }

    [Fact]
    public void TallyLabels_UnlabeledClause_FallsBackToScoreIndex()
    {
        var labeled = new JokerClause { Jokers = [MotelyJoker.Blueprint], Label = "bp" };
        var unlabeled = new JokerClause { Jokers = [MotelyJoker.Blueprint], Antes = [1, 2] };

        var plan = JamlSearchBuilder.CreatePlan(LabelConfig(labeled, unlabeled));

        Assert.Equal(["bp", "score1"], plan.TallyLabels);
    }
}
