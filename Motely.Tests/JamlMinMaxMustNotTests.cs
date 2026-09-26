using Motely.Filters;
using Xunit;

namespace Motely.Tests;

/// <summary>
/// min/max are part of the scoring contract, not only the SIMD gate:
/// a should leaf below its <c>min</c> tallies what was found but pays no score; an
/// <c>and:</c>/<c>or:</c> arm counts as matched only inside its own bounds; <c>or: mode: max</c>
/// reports the raw count of the same arm it scores. And <c>mustNot</c> over a coarse SIMD
/// prefilter (bare legendaryJoker, pack-sourced items) is enforced by the scalar scoring pass
/// instead of negating a prefilter that passes every lane.
/// Ground truth MOTELY77 Red/White: ante 1 = Polychrome Tag + Tarot Merchant, ante 2 = Blank.
/// </summary>
public class JamlMinMaxMustNotTests
{
    private const string Seed = "MOTELY77";

    /// <summary>First twelve seeds of JamlFilters/Zerkeo.jaml: Anaglyph/White, Hieroglyph in
    /// ante 1, Negative Perkeo in an ante-0 pack.</summary>
    private static readonly string[] ZerkeoSeeds =
    [
        "F2U88X11", "JX8C8X11", "L8FJ8X11", "A68EBX11", "M2TCJX11", "BC36RX11",
        "4E1MRX11", "KWR8OX11", "BB3BOX11", "K4GBQX11", "B5BCQX11", "6E5RVX11",
    ];

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

    private static HashSet<string> RunSeedList(string jaml, string[] seeds)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        var hits = new HashSet<string>();
        using var search = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedList(seeds)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => hits.Add(s))
            .Start();
        search.AwaitCompletion();
        Assert.Equal(hits.Count, (int)search.MatchingSeeds);
        return hits;
    }

    [Fact]
    public void ShouldLeaf_BelowMin_TalliesRawCountButScoresZero()
    {
        var (_, score, tally) = RunSingleSeed(
            """
            name: should-leaf-min
            deck: Red
            stake: White
            should:
              - voucher: TarotMerchant
                antes: [1]
                min: 2
                score: 5
            """
        );

        Assert.Equal(1, tally);
        Assert.Equal(0, score);
    }

    [Fact]
    public void ShouldLeaf_AtMin_ScoresAsBefore()
    {
        var (_, score, tally) = RunSingleSeed(
            """
            name: should-leaf-min-met
            deck: Red
            stake: White
            should:
              - voucher: TarotMerchant
                antes: [1]
                min: 1
                score: 5
            """
        );

        Assert.Equal(1, tally);
        Assert.Equal(5, score);
    }

    [Fact]
    public void OrChild_BelowItsMin_IsNotAMatchedArm()
    {
        var (_, score, tally) = RunSingleSeed(
            """
            name: or-child-min
            deck: Red
            stake: White
            should:
              - or:
                  score: 1
                  clauses:
                    - smallBlindTag: PolychromeTag
                      antes: [1]
                      min: 2
                      score: 3
                    - voucher: TarotMerchant
                      antes: [1]
                      score: 5
            """
        );

        Assert.Equal(1, tally); // only the voucher arm is inside its bounds
        Assert.Equal(5, score); // not 3 + 5
    }

    [Fact]
    public void AndChild_BelowItsMin_GatesConjunctionToZero()
    {
        var (_, score, tally) = RunSingleSeed(
            """
            name: and-child-min
            deck: Red
            stake: White
            should:
              - and:
                  score: 7
                  clauses:
                    - smallBlindTag: PolychromeTag
                      antes: [1]
                      min: 2
                    - voucher: TarotMerchant
                      antes: [1]
            """
        );

        Assert.Equal(0, tally ?? 0);
        Assert.Equal(0, score ?? 0);
    }

    [Fact]
    public void OrModeMax_TallyReportsTheArmThatScored()
    {
        // Arm 1 counts 2 (Tarot Merchant ante 1 + Blank ante 2) at weight 1 → 2; arm 2 counts 1 at
        // weight 5 → 5. mode: max scores arm 2, so the tally must be arm 2's raw count (1), not 2.
        var (_, score, tally) = RunSingleSeed(
            """
            name: or-mode-max-same-arm
            deck: Red
            stake: White
            should:
              - or:
                  mode: max
                  score: 1
                  clauses:
                    - voucher: [TarotMerchant, Blank]
                      antes: [1, 2]
                      score: 1
                    - voucher: TarotMerchant
                      antes: [1]
                      score: 5
            """
        );

        Assert.Equal(5, score);
        Assert.Equal(1, tally);
    }

    private const string ZerkeoHeader = """
        name: zerkeo-mustnot
        deck: Anaglyph
        stake: White
        """;

    [Fact]
    public void MustNot_CoarseLegendary_RejectsSeedsThatHaveIt()
    {
        var hits = RunSeedList(
            ZerkeoHeader
                + """

                must:
                  - voucher: Hieroglyph
                    antes: [1]
                mustNot:
                  - legendaryJoker: Perkeo
                    antes: [0]
                    sources:
                      boosterPacks: [0, 1]
                """,
            ZerkeoSeeds
        );

        Assert.Empty(hits);
    }

    [Fact]
    public void Must_CoarseLegendary_KeepsEveryZerkeoSeed()
    {
        var hits = RunSeedList(
            ZerkeoHeader
                + """

                must:
                  - voucher: Hieroglyph
                    antes: [1]
                  - legendaryJoker: Perkeo
                    antes: [0]
                    sources:
                      boosterPacks: [0, 1]
                """,
            ZerkeoSeeds
        );

        Assert.Equal(ZerkeoSeeds.OrderBy(s => s), hits.OrderBy(s => s));
    }

    /// <summary>
    /// A bare legendaryJoker prefilter passes every lane; negated in SIMD it rejected every seed.
    /// Enforced in scoring, mustNot keeps exactly the seeds the inverse must does not.
    /// </summary>
    [Fact]
    public void MustNot_CoarseLegendary_IsTheComplementOfMust()
    {
        const string triboulet = """

            must:
              - voucher: Hieroglyph
                antes: [1]
            {0}:
              - legendaryJoker: Triboulet
                antes: [0]
                sources:
                  boosterPacks: [0, 1]
            """;

        var without = RunSeedList(ZerkeoHeader + string.Format(triboulet, "mustNot"), ZerkeoSeeds);
        var with = RunSeedList(ZerkeoHeader + string.Format(triboulet, "must"), ZerkeoSeeds);

        Assert.NotEmpty(without);
        Assert.Empty(without.Intersect(with));
        Assert.Equal(ZerkeoSeeds.Length, without.Count + with.Count);
    }

    [Fact]
    public void MustNot_ExactClause_StillRejectsInSimd()
    {
        var hits = RunSeedList(
            ZerkeoHeader
                + """

                must:
                  - legendaryJoker: Perkeo
                    antes: [0]
                    sources:
                      boosterPacks: [0, 1]
                mustNot:
                  - voucher: Hieroglyph
                    antes: [1]
                """,
            ZerkeoSeeds
        );

        Assert.Empty(hits);
    }

    [Fact]
    public void Builder_SplitsMustNotByExactConfirm()
    {
        Assert.True(
            JamlScoring.IsExactFilterConfirm(
                new VoucherClause { Vouchers = [MotelyVoucher.Hieroglyph], Antes = [1] }
            )
        );
        Assert.False(
            JamlScoring.IsExactFilterConfirm(
                new LegendaryJokerClause { Jokers = [MotelyJoker.Perkeo], Antes = [0] }
            )
        );
    }
}
