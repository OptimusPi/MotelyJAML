using Motely.Filters;
using Motely.Filters.Jaml;

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
        var config = JamlConfigLoader.FromJaml("""
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
        var ok = JamlConfigLoader.TryLoad("""{ "must": [{ "joker": "Blueprint" }], "boses": [] }""",
            out _,
            out var error
        );

        Assert.False(ok);
        Assert.Contains("boses", error);
    }

    [Fact]
    public void FromJson_NullJoker_IsCategoryAny()
    {
        var config = JamlConfigLoader.FromJaml("""{ "must": [{ "joker": null }] }""");
        AssertSingleMustIsAnyJoker(config);
    }

    [Fact]
    public void FromYaml_BareJoker_IsCategoryAny()
    {
        var config = JamlConfigLoader.FromJaml("""
            must:
              - joker:
            """);
        AssertSingleMustIsAnyJoker(config);
    }

    [Fact]
    public void FromYaml_FoldedParagraph_LandsOnDescription()
    {
        var config = JamlConfigLoader.FromJaml("""
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
        var config = JamlConfigLoader.FromJaml("""
            must:
              - joker: Any
            """);
        AssertSingleMustIsAnyJoker(config);
    }

    [Fact]
    public void FromYaml_HappyPath_MatchesJson()
    {
        var fromYaml = JamlConfigLoader.FromJaml("""
            name: yaml happy
            deck: red
            stake: white
            must:
              - joker: Blueprint
            """);
        var fromJson = JamlConfigLoader.FromJaml("""{ "name": "yaml happy", "deck": "red", "stake": "white", "must": [{ "joker": "Blueprint" }] }""");

        Assert.Equal(fromJson.Deck, fromYaml.Deck);
        Assert.Equal(fromJson.Stake, fromYaml.Stake);   
        Assert.IsType<JokerClause>(Assert.Single(fromYaml.Must));
        Assert.IsType<JokerClause>(Assert.Single(fromJson.Must));
    }

    [Fact]
    public void FromJson_InvalidJson_ThrowsWithMessage()
    {
        var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
            JamlConfigLoader.FromJaml("{ not json")
        );
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void FromYaml_InvalidYaml_ThrowsWithMessage()
    {
        var ex = Assert.ThrowsAny<InvalidOperationException>(() =>
            JamlConfigLoader.FromJaml("must: [")
        );
        Assert.NotEmpty(ex.Message);
    }
}
