using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// A top-level <c>defaults:</c> block fills its keys into every clause that leaves them out,
/// wherever the block sits in the document: the filter loads to exactly the clauses it would with
/// each key written out. Proven field by field and by scoring a fixed seed list.
/// </summary>
public sealed class JamlDefaultsTests
{
    private static string[] Clauses(string yaml) => TwinFilters.AllFields(ProofSearch.LoadOrThrow(yaml));

    private const string WithDefaults = """
        name: oops perkeo
        deck: Anaglyph
        defaults:
          antes: [1, 2]
        must:
          - legendaryJoker: Perkeo
        should:
          - smallBlindTag: NegativeTag
            antes: [1]
          - joker: OopsAll6s
            score: 1
          - Showman
          - Perkeo score 5
          - The Eye in ante 3
          - luckyMoney: [0, 1]
          - or:
              - joker: Blueprint
              - joker: Brainstorm
                antes: [3]
        """;

    private const string WrittenOut = """
        name: oops perkeo
        deck: Anaglyph
        must:
          - legendaryJoker: Perkeo
            antes: [1, 2]
        should:
          - smallBlindTag: NegativeTag
            antes: [1]
          - joker: OopsAll6s
            antes: [1, 2]
            score: 1
          - joker: Showman
            antes: [1, 2]
          - legendaryJoker: Perkeo
            antes: [1, 2]
            score: 5
          - boss: TheEye
            antes: [3]
          - luckyMoney: [0, 1]
          - or:
              - joker: Blueprint
                antes: [1, 2]
              - joker: Brainstorm
                antes: [3]
        """;

    [Fact]
    public void Defaults_LoadTheClausesTheyWouldWrittenOut()
    {
        Assert.Equal(Clauses(WrittenOut), Clauses(WithDefaults));
    }

    [Fact]
    public void Defaults_FindExactlyWhatTheWrittenOutFilterFinds()
    {
        TwinFilters.AssertSame(WrittenOut, WithDefaults);
    }

    [Fact]
    public void Defaults_MayComeAfterTheClauses()
    {
        var after = WithDefaults.Replace("defaults:\n  antes: [1, 2]\n", "") + "\ndefaults:\n  antes: [1, 2]\n";
        Assert.True(after.IndexOf("defaults:", StringComparison.Ordinal) > after.LastIndexOf("antes: [3]", StringComparison.Ordinal));
        Assert.Equal(Clauses(WrittenOut), Clauses(after));
    }

    [Fact]
    public void ScoreDefault_FillsClausesThatNameNone_IncludingGroups()
    {
        Assert.Equal(
            Clauses("""
                should:
                  - joker: Blueprint
                    score: 3
                  - joker: Brainstorm
                    score: 1
                  - Perkeo score 3
                  - Canio in ante 1 score 9
                  - or:
                      - boss: TheEye
                        score: 3
                      - boss: TheWall
                        score: 3
                    score: 3
                  - and:
                      clauses:
                        - joker: Blueprint
                          score: 3
                      score: 7
                """),
            Clauses("""
                defaults:
                  score: 3
                should:
                  - joker: Blueprint
                  - joker: Brainstorm
                    score: 1
                  - Perkeo
                  - Canio in ante 1 score 9
                  - or:
                      - boss: TheEye
                      - boss: TheWall
                  - and:
                      clauses:
                        - joker: Blueprint
                      score: 7
                """));
    }

    [Fact]
    public void AnteSpelling_WorksInDefaultsToo()
    {
        Assert.Equal(
            Clauses("defaults:\n  antes: [2]\nmust:\n  - joker: Blueprint\n"),
            Clauses("defaults:\n  ante: 2\nmust:\n  - joker: Blueprint\n"));
    }

    [Fact]
    public void Defaults_AreKeptOnTheConfig()
    {
        var config = ProofSearch.LoadOrThrow("defaults:\n  antes: [1, 2]\n  score: 4\nmust:\n  - joker: Blueprint\n");
        Assert.Equal([1, 2], config.Defaults!.Antes!);
        Assert.Equal(4, config.Defaults.Score);
    }

    [Fact]
    public void EachClauseGetsItsOwnCopyOfTheAntes()
    {
        var config = ProofSearch.LoadOrThrow("defaults:\n  antes: [1, 2]\nmust:\n  - joker: Blueprint\n  - joker: Brainstorm\n");
        var a = Assert.IsType<JokerClause>(config.Must[0]).Antes;
        var b = Assert.IsType<JokerClause>(config.Must[1]).Antes;
        Assert.NotSame(a, b);
        Assert.NotSame(config.Defaults!.Antes, a);
    }

    [Fact]
    public void NoDefaults_LeavesTheClauseDefaults()
    {
        var joker = Assert.IsType<JokerClause>(Assert.Single(ProofSearch.LoadOrThrow("must:\n  - Blueprint\n").Must));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], joker.Antes);
        Assert.Equal(1, joker.Score);
    }

    [Fact]
    public void AnUnknownDefaultsKey_NamesTheKeys()
    {
        Assert.False(JamlConfigLoader.TryLoad("defaults:\n  edition: Negative\nmust:\n  - joker: Blueprint\n", out _, out var error));
        Assert.Equal("JAML line 2: 'edition' is not a key of `defaults:`, which takes antes, score.", error);
    }

    [Fact]
    public void Defaults_RoundTripThroughJsonAndBack()
    {
        var json = JamlConfigLoader.ToJson(WithDefaults);
        var yamlAgain = JamlConfigLoader.ToYaml(json);
        Assert.Equal(Clauses(WrittenOut), Clauses(json));
        Assert.Equal(Clauses(WrittenOut), Clauses(yamlAgain));
        Assert.Equal([1, 2], ProofSearch.LoadOrThrow(yamlAgain).Defaults!.Antes!);
    }
}
