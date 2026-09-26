using Xunit;

namespace Motely.Tests;

/// <summary>
/// Pins AND-clause scoring to min-of-children ("complete conjunctions"), not the
/// sum of child counts. The sum double-paid the clause score: tag(1) + voucher(1)
/// summed to tally 2, paying 2×score for one conjunction.
/// Ground truth (analyzer, MOTELY77 Red/White, ante 1): small blind tag is
/// Polychrome Tag, voucher is Tarot Merchant.
/// </summary>
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

    // Accessibility/correctness gate: a should clause you bothered to write, but gave no explicit
    // score, is worth 1 — not 0. Defaulting to 0 silently made unscored should clauses contribute
    // nothing, the bug that zeroed whole filters for ~10 months. This test is that fix, pinned.
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
        Assert.Equal(1, tally); // one complete conjunction — NOT 2 (tag + voucher summed)
        Assert.Equal(7, score); // exactly the clause score — NOT 14
    }

    [Fact]
    public void AndClause_OneChildMissing_ContributesNothing()
    {
        // Telescope is not the ante-1 voucher on MOTELY77 — the AND must gate to 0.
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
