using Motely.Filters;
using Xunit;

namespace Motely.Tests;

/// <summary>
/// Regression tests for the Hieroglyph / Petroglyph scenario: these vouchers reset progression
/// one ante backward and re-open the shop, effectively unlocking pack slots 4..7 in ante 1
/// (which normally caps at 4 packs / slots 0..3, slot 0 being the fixed first-shop Buffoon).
///
/// Seed <c>KHTW99TC</c> is the canonical example: a Negative-edition Perkeo appears in the
/// ante-1 Arcana at pack slot 6 (the sixth rolled pack, behind the Buffoon), only accessible
/// after buying Hieroglyph in ante 2 to rewind to ante 1.
///
/// These tests pin the filter's ability to match that seed so future per-ante clamping work
/// does not silently remove Hieroglyph-accessible matches.
/// </summary>
public class HieroglyphPackSlotTests
{
    private const string HieroglyphPerkeoSeed = "KHTW99TC";

    private static (long SeedsSearched, long MatchingSeeds) RunSingleSeedJaml(
        string jaml,
        string seed
    )
    {
        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator([seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true);

        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.TotalSeedsSearched, search.MatchingSeeds);
    }

    /// <summary>
    /// KHTW99TC: ante-1 pack slot 6 contains a Negative Perkeo (Hieroglyph-reachable only).
    /// Scoring extends ante-1 reachability only because the seed's actual voucher path rewinds
    /// back to ante 1.
    /// </summary>
    [Fact]
    public void KHTW99TC_HasNegativePerkeo_InAnte1_Slot6_WhenRunStateRewindsAnte()
    {
        var jaml = """
            name: HieroglyphPerkeo
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
                edition: Negative
                antes: [1]
                sources:
                  boosterPacks: [6]
            """;

        var result = RunSingleSeedJaml(jaml, HieroglyphPerkeoSeed);
        Assert.Equal(1, result.SeedsSearched);
        Assert.Equal(1, result.MatchingSeeds);
    }

    /// <summary>
    /// Full extended slot range [0..7] on ante 1 with actual voucher rewind — must match.
    /// </summary>
    [Fact]
    public void KHTW99TC_HasNegativePerkeo_InAnte1_FullSlotRange_WithRunStateRewind()
    {
        var jaml = """
            name: HieroglyphPerkeoFullRange
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
                edition: Negative
                antes: [1]
                sources:
                  boosterPacks: [0, 1, 2, 3, 4, 5, 6, 7]
            """;

        var result = RunSingleSeedJaml(jaml, HieroglyphPerkeoSeed);
        Assert.Equal(1, result.SeedsSearched);
        Assert.Equal(1, result.MatchingSeeds);
    }

    /// <summary>
    /// Clamping sanity: restricting to slots [0..3] on ante 1 must NOT match this seed, because
    /// the Perkeo is specifically at slot 6 (only reachable with Hieroglyph). This pins the
    /// behaviour that an explicit restricted list is honoured exactly.
    /// </summary>
    [Fact]
    public void KHTW99TC_DoesNotMatch_WhenRestrictedTo_NormalAnte1_Slots()
    {
        var jaml = """
            name: HieroglyphPerkeoRestricted
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
                edition: Negative
                antes: [1]
                sources:
                  boosterPacks: [0, 1, 2, 3]
            """;

        var result = RunSingleSeedJaml(jaml, HieroglyphPerkeoSeed);
        Assert.Equal(1, result.SeedsSearched);
        Assert.Equal(0, result.MatchingSeeds);
    }

    /// <summary>
    /// Unknown early-ante pack-cap keys are rejected at load (valid hieroglyph scans use sources).
    /// </summary>
    [Fact]
    public void EarlyAntesMaxPackSourceProperty_IsRejected()
    {
        var jaml = """
            name: RemovedEarlyAntesMaxPack
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
                edition: Negative
                antes: [1]
                sources:
                  boosterPacks: [6]
                  earlyAntesMaxPack: 6
            """;

        Assert.False(JamlConfigLoader.TryLoad(jaml, out _, out var error));
        Assert.Contains("earlyAntesMaxPack", error);
    }

    /// <summary>
    /// Bare clause with no antes/sources gets the loader defaults stamped: antes [1..8] and
    /// boosterPacks [0..5]. Ante 1 slot 5 is decided by run-state reachability, but the seed
    /// might still match via another ante — this test only confirms the clause compiles cleanly
    /// and does not produce a parse error with the new field.
    /// </summary>
    [Fact]
    public void BareLegendaryJokerClause_CompilesAndRuns()
    {
        var jaml = """
            name: HieroglyphPerkeoBare
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
            """;

        // We don't assert match/no-match here because the seed may or may not have Perkeo in
        // other antes; this test pins that the bare form still parses and runs.
        var result = RunSingleSeedJaml(jaml, HieroglyphPerkeoSeed);
        Assert.Equal(1, result.SeedsSearched);
    }
}
