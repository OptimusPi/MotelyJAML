using Xunit;

namespace Motely.Tests;

public class JamlNestedBossScoringTests
{
    private const string Seed = "MOTELY77";

    private static (long Matching, int? Score) RunSingleSeed(string jaml)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        int? score = null;
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator([Seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result => score = result.Score);

        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, score);
    }

    [Fact]
    public void StandaloneBossClause_Scores()
    {
        var (_, score) = RunSingleSeed(
            """
            name: standalone-boss
            deck: Red
            stake: White
            should:
              - boss: TheWindow
                antes: [1]
                score: 7
            """
        );

        Assert.Equal(7, score);
    }

    [Fact]
    public void BossClauseNestedInAnd_Scores()
    {
        var (_, score) = RunSingleSeed(
            """
            name: nested-boss
            deck: Red
            stake: White
            should:
              - and:
                  - boss: TheWindow
                    antes: [1]
                  - voucher: TarotMerchant
                    antes: [1]
                score: 7
            """
        );

        Assert.Equal(7, score);
    }

    [Fact]
    public void BossClauseNestedInAnd_AtHigherAnteThanStandalone_Scores()
    {
        var (_, score) = RunSingleSeed(
            """
            name: nested-boss-higher-ante
            deck: Red
            stake: White
            should:
              - boss: TheWindow
                antes: [1]
                score: 1
              - and:
                  - boss: TheTooth
                    antes: [6]
                  - voucher: TarotMerchant
                    antes: [1]
                score: 7
            """
        );

        Assert.Equal(8, score);
    }
}
