using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// S8.P2 — scalar specialty joker sources (judgement / wraith / riffRaff / rareTag /
/// uncommonTag / rarity shop streams) through real should-clause scoring: every surviving
/// seed's tally is the PRNG-derived count from those streams, named and wildcard.
/// </summary>
public sealed class S8P2SpecialtyJokerSourceTests
{
    private static readonly string[] WideSeeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF",
    ];

    private const string SpecialtyJaml = """
        name: s8p2-specialty
        deck: Red
        stake: White
        must:
          - joker: []
            antes: [1]
        should:
          - joker: []
            antes: [1, 2]
            sources:
              judgement: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              wraith: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              riffRaff: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              rareTag: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              uncommonTag: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              commonShopJokers: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              uncommonShopJokers: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              rareShopJokers: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              allShopJokers: [0, 1]
          - joker: Joker
            antes: [1, 2]
            sources:
              judgement: [0, 1]
              wraith: [0, 1]
              riffRaff: [0, 1]
          - joker: Joker
            antes: [1, 2]
            sources:
              rareTag: [0, 1]
              uncommonTag: [0, 1]
          - joker: Joker
            antes: [1, 2]
            sources:
              commonShopJokers: [0, 1]
              uncommonShopJokers: [0, 1]
              rareShopJokers: [0, 1]
              allShopJokers: [0, 1]
        """;

    private static SortedDictionary<string, int> RunScores()
    {
        var config = ProofSearch.LoadOrThrow(SpecialtyJaml);
        var scores = new SortedDictionary<string, int>(StringComparer.Ordinal);
        using var search = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(WideSeeds, WideSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(r => scores[r.Seed] = r.Score)
            .Start();
        search.AwaitCompletion();
        return scores;
    }

    /// <summary>
    /// Pinned from the engine's own deterministic run. The spread (36–72) is the proof the
    /// specialty streams discriminate: every seed walks all nine stream families, and the
    /// per-seed tally is the count of jokers those PRNG streams actually produce. A wildcard
    /// <c>joker: []</c> also counts legendaries off the default pack slots, so each ante-1/2 Soul
    /// adds nine; CUC and FMF each hold one Soul in ante 1's fourth rolled pack, which no run
    /// offers (slot 0 there is the fixed Buffoon), so it does not count.
    /// </summary>
    [Fact]
    public void SpecialtySources_ScoreEverySurvivingSeed_PinnedTallies()
    {
        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["111"] = 45, ["2A2"] = 45, ["3X3"] = 46, ["474"] = 45, ["4C4"] = 45,
            ["5X5"] = 36, ["616"] = 37, ["696"] = 36, ["6J6"] = 36, ["7H7"] = 38,
            ["88"] = 36, ["99"] = 37, ["AAAAAAAA"] = 37, ["ALEEB"] = 72, ["CC"] = 36,
            ["CUC"] = 36, ["F"] = 37, ["FMF"] = 45, ["GHG"] = 45, ["H"] = 36,
            ["I"] = 36, ["MOTELY"] = 36, ["MOTELY77"] = 36, ["Q"] = 36, ["R"] = 36,
            ["UNITTEST"] = 36, ["VV"] = 37, ["Z"] = 36,
        };
        Assert.Equal(expected, RunScores());
    }
}
