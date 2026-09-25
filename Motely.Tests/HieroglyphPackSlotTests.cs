using Motely.Filters;
using Xunit;

namespace Motely.Tests;

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

        var result = RunSingleSeedJaml(jaml, HieroglyphPerkeoSeed);
        Assert.Equal(1, result.SeedsSearched);
    }
}
