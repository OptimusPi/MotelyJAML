using Motely.Filters;
using Xunit;

namespace Motely.Tests;

public class JamlOrModeScoringTests
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
    public void OrMode_Omitted_DefaultsToSum()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(
                """
                name: or-mode-default
                deck: Red
                stake: White
                should:
                  - or:
                      - smallBlindTag: PolychromeTag
                        antes: [1]
                      - voucher: TarotMerchant
                        antes: [1]
                """,
                out var config,
                out var error
            ),
            error
        );
        var or = Assert.IsType<OrClause>(Assert.Single(config!.Should));
        Assert.Equal(JamlLogicScoreMode.Sum, or.Mode);
    }

    [Fact]
    public void OrMode_Max_Parses()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(
                """
                name: or-mode-max-parse
                deck: Red
                stake: White
                should:
                  - or:
                      mode: max
                      clauses:
                        - smallBlindTag: PolychromeTag
                          antes: [1]
                        - voucher: TarotMerchant
                          antes: [1]
                """,
                out var config,
                out var error
            ),
            error
        );
        var or = Assert.IsType<OrClause>(Assert.Single(config!.Should));
        Assert.Equal(JamlLogicScoreMode.Max, or.Mode);
    }

    [Fact]
    public void OrMode_Unknown_HardError()
    {
        Assert.False(
            JamlConfigLoader.TryLoad(
                """
                name: or-mode-bad
                deck: Red
                stake: White
                should:
                  - or:
                      mode: average
                      clauses:
                        - joker: Blueprint
                """,
                out _,
                out var error
            )
        );
        Assert.Contains("mode", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrMode_Sum_TotalsArmWeights()
    {
        var (matching, score, tally) = RunSingleSeed(
            """
            name: or-mode-sum
            deck: Red
            stake: White
            should:
              - or:
                  mode: sum
                  score: 1
                  clauses:
                    - smallBlindTag: PolychromeTag
                      antes: [1]
                      score: 3
                    - voucher: TarotMerchant
                      antes: [1]
                      score: 5
            """
        );

        Assert.Equal(1, matching);
        Assert.Equal(8, score);
        Assert.Equal(2, tally);
    }

    [Fact]
    public void OrMode_Max_BestArmOnly()
    {
        var (matching, score, tally) = RunSingleSeed(
            """
            name: or-mode-max
            deck: Red
            stake: White
            should:
              - or:
                  mode: max
                  score: 1
                  clauses:
                    - smallBlindTag: PolychromeTag
                      antes: [1]
                      score: 3
                    - voucher: TarotMerchant
                      antes: [1]
                      score: 5
            """
        );

        Assert.Equal(1, matching);
        Assert.Equal(5, score);
        Assert.Equal(1, tally);
    }

    [Fact]
    public void OrMode_Max_Loads()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(
                """
                name: or-mode-roundtrip
                deck: Red
                stake: White
                should:
                  - or:
                      mode: max
                      clauses:
                        - joker: Blueprint
                          antes: [1]
                """,
                out var config,
                out var error
            ),
            error
        );

        var or = Assert.IsType<OrClause>(Assert.Single(config!.Should));
        Assert.Equal(JamlLogicScoreMode.Max, or.Mode);
    }

    [Fact]
    public void Or_ArmsWriteTheirOwnAntes_LoadAndScore_MOTELY77()
    {
        const string jaml = """
            name: or-arm-antes
            deck: Red
            stake: White
            should:
              - or:
                  mode: max
                  score: 1
                  clauses:
                    - smallBlindTag: PolychromeTag
                      antes: [1]
                      score: 3
                    - voucher: TarotMerchant
                      antes: [1]
                      score: 5
            """;

        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            error
        );

        var or = Assert.IsType<OrClause>(Assert.Single(config!.Should));
        Assert.Equal(JamlLogicScoreMode.Max, or.Mode);
        Assert.Equal(2, or.Clauses.Length);

        var tag = Assert.IsAssignableFrom<IAnteScopedClause>(or.Clauses[0]);
        var voucher = Assert.IsAssignableFrom<IAnteScopedClause>(or.Clauses[1]);
        Assert.Equal([1], tag.Antes);
        Assert.Equal([1], voucher.Antes);

        var (matching, score, tally) = RunSingleSeed(jaml);
        Assert.Equal(1, matching);
        Assert.Equal(5, score);
        Assert.Equal(1, tally);
    }

    [Fact]
    public void Or_ArmAntes_WrongAnte_NoScore_MOTELY77()
    {
        var (_, score, tally) = RunSingleSeed(
            """
            name: or-antes-wrong
            deck: Red
            stake: White
            should:
              - or:
                  mode: sum
                  score: 1
                  clauses:
                    - smallBlindTag: PolychromeTag
                      antes: [2]
                      score: 3
                    - voucher: TarotMerchant
                      antes: [2]
                      score: 5
            """
        );

        Assert.True((score ?? 0) == 0, $"expected no score at the wrong ante, got {score}");
        Assert.True((tally ?? 0) == 0, $"expected no tally at the wrong ante, got {tally}");
    }
}


