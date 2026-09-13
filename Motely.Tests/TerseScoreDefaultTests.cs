using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// One default score for both spellings. A block clause and its one-line (JUMMY) twin must load
/// to the same <see cref="IJamlClause.Score"/>, so an unscored "- Blueprint in ante 1" counts
/// exactly like "- joker: Blueprint / ante: 1" and the writer freezes neither into a score: 0.
/// </summary>
public sealed class TerseScoreDefaultTests
{
    private const string Block = """
        id: block
        should:
          - joker: Blueprint
            ante: 1
        """;

    private const string Terse = """
        id: block
        should:
          - Blueprint in ante 1
        """;

    private static JamlConfig Load(string jaml)
    {
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        return config!;
    }

    [Fact]
    public void BothSpellings_LoadTheSameScore()
    {
        var block = Assert.IsType<JokerClause>(Assert.Single(Load(Block).Should));
        var terse = Assert.IsType<JokerClause>(Assert.Single(Load(Terse).Should));

        Assert.Equal(block.Score, terse.Score);
        Assert.Equal(JamlConfigLoader.DefaultScore, terse.Score);
    }

    [Theory]
    [InlineData("Blueprint in ante 1 score 7", 7)]
    [InlineData("Blueprint in ante 1 score 0", 0)]
    [InlineData("Blueprint in ante 1 score -3", -3)]
    public void TerseLine_WithScoreTail_KeepsIt(string line, int expected)
    {
        var config = Load($"should:\n  - {line}\n");
        Assert.Equal(expected, Assert.Single(config.Should).Score);
    }

    [Fact]
    public void TerseLine_ContinuationScoreKey_StillWins()
    {
        var config = Load("""
            should:
              - Blueprint in ante 1
                score: 7
            """);
        Assert.Equal(7, Assert.Single(config.Should).Score);
    }

    [Fact]
    public void TerseLine_InsideLogic_GetsTheDefault()
    {
        var config = Load("""
            should:
              - or:
                  - Blueprint in ante 1
                  - joker: Brainstorm
                    ante: 1
            """);
        var or = Assert.IsType<Motely.Filters.OrClause>(Assert.Single(config.Should));
        Assert.Equal(or.Clauses[1].Score, or.Clauses[0].Score);
    }

    [Fact]
    public void WriteLoadWrite_IsStable_AndIdenticalForBothSpellings()
    {
        string blockOnce = JamlConfigLoader.ToJaml(Load(Block));
        string terseOnce = JamlConfigLoader.ToJaml(Load(Terse));

        Assert.Equal(blockOnce, terseOnce);
        Assert.DoesNotContain("score", terseOnce);
        Assert.Equal(blockOnce, JamlConfigLoader.ToJaml(Load(blockOnce)));
        Assert.Equal(terseOnce, JamlConfigLoader.ToJaml(Load(terseOnce)));
    }

    [Fact]
    public void ExplicitScore_SurvivesWriteLoadWrite()
    {
        string once = JamlConfigLoader.ToJaml(Load("should:\n  - Blueprint in ante 1 score 7\n"));
        Assert.Contains("score: 7", once);
        Assert.Equal(once, JamlConfigLoader.ToJaml(Load(once)));
    }

    [Fact]
    public void JamlLine_NoTail_DefaultsLikeTheLoader_AndCanonicalizeIsFixedPoint()
    {
        Assert.True(JamlLine.TryToClause("Blueprint in ante 1", out var clause, out var error), error);
        Assert.Equal(JamlConfigLoader.DefaultScore, clause!.Score);

        string canonical = JamlLine.Canonicalize("Blueprint in ante 1");
        Assert.Equal("Blueprint in ante 1", canonical);
        Assert.Equal(canonical, JamlLine.Canonicalize(canonical));
    }

    /// <summary>
    /// The default is not cosmetic: an unscored terse should clause has to move the seed's score.
    /// 2PICKLEB carries Blueprint in ante 1 (JamlFilters/Pickle.jaml lists it as that filter's seed).
    /// </summary>
    [Fact]
    public void UnscoredTerseShould_ContributesToTheSeedScore()
    {
        var config = Load("""
            deck: Red
            stake: White
            should:
              - Blueprint in ante 1
            """);

        string[] seeds = ["2PICKLEB"];
        int score = -1;
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result => score = result.Score);
        using var search = settings.Start();
        search.AwaitCompletion();

        Assert.Equal(JamlConfigLoader.DefaultScore, score);
    }
}
