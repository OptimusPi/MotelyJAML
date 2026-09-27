using Xunit;

namespace Motely.Tests;

/// <summary>
/// A boss clause nested inside <c>and:</c>/<c>or:</c> scores the same as a standalone one.
/// <c>PrepareRunState</c> sizes <c>CachedBosses</c> from the boss antes it finds, so it has to
/// recurse into nested clauses the way <c>GetMaxAnte</c> does; missing one leaves the array null or
/// short and <c>CountBossOccurrences</c> indexes it directly.
/// Ground truth (analyzer, MOTELY77 Red/White, ante 1): boss is The Window, voucher is
/// Tarot Merchant — so the conjunction below is one complete match worth its score of 7.
/// </summary>
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

    // The standalone form has always worked — it's the control, proving the seed and the boss
    // spelling are right so a failure below can only be the nesting.
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

    // A boss clause nested deeper than the top-level ante scan reaches: the standalone boss at
    // ante 1 sizes CachedBosses to [0..1], then the nested clause indexes ante 4 past its end.
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
