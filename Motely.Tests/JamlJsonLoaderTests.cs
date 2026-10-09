using Motely.Filters;

namespace Motely.Tests;

public class JamlJsonLoaderTests
{
    /// <summary>The one must clause is a joker clause naming no joker, which the engine reads as the whole category.</summary>
    private static void AssertSingleMustIsAnyJoker(JamlConfig config)
    {
        var clause = Assert.Single(config.Must);
        var joker = Assert.IsType<JokerClause>(clause);
        Assert.Empty(joker.Jokers);
    }

    [Fact]
    public void FromJson_HappyPath_ParsesDeckStakeAndClauses()
    {
        var config = YamlConfigLoader.FromYaml("""
            {
              "name": "json happy",
              "deck": "Erratic",
              "stake": "Gold",
              "must": [{ "joker": "Blueprint" }],
              "should": [{ "voucher": "Telescope", "score": 5 }],
              "mustNot": [{ "joker": "Vagabond" }]
            }
            """);

        Assert.Equal(MotelyDeck.Erratic, config.Deck);
        Assert.Equal(MotelyStake.Gold, config.Stake);
        Assert.Single(config.Must);
        Assert.Single(config.Should);
        Assert.Single(config.MustNot);
    }

    [Fact]
    public void TryLoadFromJson_UnknownRootKey_IsRejected()
    {
        var ok = YamlConfigLoader.TryLoad("""{ "must": [{ "joker": "Blueprint" }], "boses": [] }""",

            out _,
            out var error
        );

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("boses", error);
    }

    [Fact]
    public void TryLoadFromYaml_UnknownRootKey_IsRejected()
    {
        var ok = YamlConfigLoader.TryLoad("""
            name: test
            deck: Red
            stake: White
            unknownKey: value
            must:
              - joker: Blueprint
            """,
            out _,
            out var error
        );

        Assert.False(ok);
        Assert.Contains("unknownKey", error);
        Assert.Contains("YAML line", error);
    }

    [Fact]
    public void TryLoadFromYaml_UnknownRootKey_IsRejectedWithLine()
    {
        var ok = YamlConfigLoader.TryLoad("""
            name: typo
            deck: Red
            must:
              - joker: Blueprint
            shuold:
              - voucher: Telescope
            """, out _, out var error);
        Assert.False(ok);
        Assert.Contains("YAML line 5", error);
        Assert.Contains("'shuold'", error);

    }

    [Fact]
    public void TryLoadFromYaml_SyntaxError_NamesLine()
    {
        var ok = YamlConfigLoader.TryLoad("""
            name: colon
            deck: Red
            description: keep one: Diet Cola
            must:
              - joker: Blueprint
            """, out _, out var error);
        Assert.False(ok);
        Assert.Contains("YAML line 3:", error);
    }

    [Fact]
    public void FromJson_NullJoker_IsCategoryAny()
    {
        var config = YamlConfigLoader.FromYaml("""{ "must": [{ "joker": null }] }""");
        AssertSingleMustIsAnyJoker(config);
    }

    [Fact]
    public void FromYaml_BareJoker_IsCategoryAny()
    {
        var config = YamlConfigLoader.FromYaml("""
            must:
              - joker:
            """);
        AssertSingleMustIsAnyJoker(config);
    }

    [Fact]
    public void FromYaml_FoldedParagraph_LandsOnDescription()
    {
        var config = YamlConfigLoader.FromYaml("""
            name: folded
            description: >
              hello
              world
            must:
              - joker: Any
            """);
        Assert.Equal("hello world\n", config.Description);
        AssertSingleMustIsAnyJoker(config);
    }

    [Fact]
    public void FromYaml_AnyKeyword_IsCategoryAny()
    {
        var config = YamlConfigLoader.FromYaml("""
            must:
              - joker: Any
            """);
        AssertSingleMustIsAnyJoker(config);
    }

    [Fact]
    public void FromYaml_HappyPath_MatchesJson()
    {
        var fromYaml = YamlConfigLoader.FromYaml("""
            name: yaml happy
            deck: red
            stake: white
            must:
              - joker: Blueprint
            """);
        var fromJson = YamlConfigLoader.FromYaml("""{ "name": "yaml happy", "deck": "red", "stake": "white", "must": [{ "joker": "Blueprint" }] }""");

        Assert.Equal(fromJson.Deck, fromYaml.Deck);
        Assert.Equal(fromJson.Stake, fromYaml.Stake);   
        Assert.IsType<JokerClause>(Assert.Single(fromYaml.Must));
        Assert.IsType<JokerClause>(Assert.Single(fromJson.Must));
    }

    [Fact]
    public void FromJson_InvalidJson_ThrowsWithMessage()
    {
        var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
            YamlConfigLoader.FromYaml("{ not json")
        );
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void FromYaml_InvalidYaml_ThrowsWithMessage()
    {
        var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
            YamlConfigLoader.FromYaml("must: [")
        );
        Assert.NotEmpty(ex.Message);
    }
}
