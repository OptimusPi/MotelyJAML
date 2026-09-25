using Xunit;

namespace Motely.Tests;

public class JamlAndScoringTests
{
    private const string Seed = "MOTELY77";

    private static (long Matching, int? Score, int? Tally) RunSingleSeed(string jaml)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        int? score = null;
        int? tally = null;
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator([Seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result =>
            {
                score = result.Score;
                tally = result.TallyCount > 0 ? result.GetTally(0) : null;
            });

        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, score, tally);
    }

    [Fact]
    public void ShouldClause_WithNoExplicitScore_DefaultsToOne()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(
                """
                name: default-score-is-one
                deck: Red
                stake: White
                should:
                  - joker: Blueprint
                """,
                out var config,
                out var error
            ),
            error
        );
        Assert.Equal(1, config!.Should[0].Score);
    }

    [Fact]
    public void AndClause_BothChildrenMatchOnce_TalliesOneConjunctionNotSum()
    {
        var (matching, score, tally) = RunSingleSeed(
            """
            name: and-min
            deck: Red
            stake: White
            should:
              - and:
                  - smallBlindTag: PolychromeTag
                    antes: [1]
                  - voucher: TarotMerchant
                    antes: [1]
                score: 7
            """
        );

        Assert.Equal(1, matching);
        Assert.Equal(1, tally);
        Assert.Equal(7, score);
    }

    [Fact]
    public void AndClause_OneChildMissing_ContributesNothing()
    {
        var (_, score, tally) = RunSingleSeed(
            """
            name: and-gate
            deck: Red
            stake: White
            should:
              - and:
                  - smallBlindTag: PolychromeTag
                    antes: [1]
                  - voucher: Telescope
                    antes: [1]
                score: 7
            """
        );

        Assert.True((score ?? 0) == 0, $"expected no score, got {score}");
        Assert.True((tally ?? 0) == 0, $"expected no tally, got {tally}");
    }
}
