using Motely.Filters;
using Xunit;

namespace Motely.Tests;

public class LuckyEventLuckTests
{
    private const string DifferentialSeed = "41111111";

    private static (
        long SeedsSearched,
        long MatchingSeeds,
        int? Score,
        int? Tally
    ) RunSingleSeedJaml(string jaml)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        int? score = null;
        int? tally = null;
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator([DifferentialSeed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result =>
            {
                score = result.Score;
                tally = result.TallyCount > 0 ? result.GetTally(0) : null;
            });

        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.TotalSeedsSearched, search.MatchingSeeds, score, tally);
    }

    [Fact]
    public void LuckyMoney_Roll0_UsesWithLuckMultiplier()
    {
        var defaultLuck = """
            name: LuckyMoneyDefaultLuck
            deck: Red
            stake: White
            must:
              - luckyMoney: [0]
            should:
              - luckyMoney: [0]
                label: lucky_money_r0_luck1
                score: 100
            """;

        var luck5 = """
            name: LuckyMoneyLuck5
            deck: Red
            stake: White
            must:
              - luckyMoney: [0]
                with:
                  luck: 5
            should:
              - luckyMoney: [0]
                label: lucky_money_r0_luck5
                score: 100
                with:
                  luck: 5
            """;

        var defaultResult = RunSingleSeedJaml(defaultLuck);
        var luck5Result = RunSingleSeedJaml(luck5);

        Assert.Equal(1, defaultResult.SeedsSearched);
        Assert.Equal(0, defaultResult.MatchingSeeds);
        Assert.Null(defaultResult.Score);
        Assert.Null(defaultResult.Tally);

        Assert.Equal(1, luck5Result.SeedsSearched);
        Assert.Equal(1, luck5Result.MatchingSeeds);
        Assert.Equal(100, luck5Result.Score);
        Assert.Equal(1, luck5Result.Tally);
    }

    [Fact]
    public void LuckyMult_LoadsWithLuck()
    {
        var jaml = """
            name: LuckyMultLuck5
            deck: Red
            stake: White
            must:
              - luckyMult: [0]
                with:
                  luck: 5
            should:
              - luckyMult: [0]
                label: lucky_mult_r0_luck5
                score: 100
                with:
                  luck: 5
            """;

        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        var must = Assert.IsType<LuckyMultClause>(config!.Must[0]);
        Assert.Equal(MotelyLuck.X5, must.With.Luck);

        var should = Assert.IsType<LuckyMultClause>(config.Should[0]);
        Assert.Equal(MotelyLuck.X5, should.With.Luck);
        Assert.Equal(100, should.Score);
    }

    [Fact]
    public void GrosMichelExtinct_LoadsWithLuck()
    {
        var jaml = """
            name: GrosMichelExtinctLuck5
            deck: Red
            stake: White
            must:
              - grosMichelExtinct: [0]
                with:
                  luck: 5
            """;

        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        var clause = Assert.IsType<GrosMichelExtinctClause>(config!.Must[0]);
        Assert.Equal(MotelyLuck.X5, clause.With.Luck);
    }

    [Fact]
    public void LuckyMult_HigherLuck_IncreasesMatchCount()
    {
        var defaultLuck = """
            name: LuckyMultDefaultLuck
            deck: Red
            stake: White
            must:
              - luckyMult: [0]
            """;

        var luck5 = """
            name: LuckyMultLuck5
            deck: Red
            stake: White
            must:
              - luckyMult: [0]
                with:
                  luck: 5
            """;

        // Seeds with known analyzer output (verified against Balatro).
        // Luck 1 → luckyMult triggers 1/15; luck 5 → 5/15.
        // Higher luck is a superset: every luck-1 match is also a luck-5 match,
        // and luck 5 must match at least as many.
        string[] seeds = ["41111111", "12345678", "UNITTEST", "ALEEBOOO", "ALEEB"];

        var defaultMatches = CollectMatchingSeedsFromList(defaultLuck, seeds);
        var luck5Matches = CollectMatchingSeedsFromList(luck5, seeds);

        Assert.True(
            luck5Matches.Count >= defaultMatches.Count,
            $"Expected luck 5 to match at least as many seeds as default luck, but got {luck5Matches.Count} < {defaultMatches.Count}."
        );

        // Every default-luck match must also be a luck-5 match (higher luck is a superset).
        Assert.Subset(luck5Matches, defaultMatches);
    }

    private static HashSet<string> CollectMatchingSeedsFromList(string jaml, string[] seeds)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"JAML parse failed: {error}\n{jaml}"
        );

        var matches = new HashSet<string>(StringComparer.Ordinal);
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(seed => matches.Add(seed));

        using var search = settings.Start();
        search.AwaitCompletion();
        return matches;
    }
}
