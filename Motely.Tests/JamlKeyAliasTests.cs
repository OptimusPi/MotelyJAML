using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// Second spellings of keys the owner's filters use — <c>ante:</c>, a single number for a list,
/// <c>requireMega:</c>, <c>soulJoker:</c> — load to exactly the clause the canonical spelling does,
/// and <c>dateCreated:</c> is kept as metadata.
/// </summary>
public sealed class JamlKeyAliasTests
{
    private static string[] Clauses(string yaml) => TwinFilters.AllFields(ProofSearch.LoadOrThrow(yaml));

    [Theory]
    [InlineData("legendaryJoker: Perkeo\n    ante: 2", "legendaryJoker: Perkeo\n    antes: [2]")]
    [InlineData("legendaryJoker: Perkeo\n    ante: [1, 2]", "legendaryJoker: Perkeo\n    antes: [1, 2]")]
    [InlineData("smallBlindTag: EtherealTag\n    ante: 2", "smallBlindTag: EtherealTag\n    antes: [2]")]
    [InlineData("joker: Blueprint\n    antes: 3", "joker: Blueprint\n    antes: [3]")]
    [InlineData("joker: Blueprint\n    Ante: 3", "joker: Blueprint\n    antes: [3]")]
    [InlineData("boss: TheEye\n    ante: 3", "boss: TheEye\n    antes: [3]")]
    [InlineData("joker: Blueprint\n    sources:\n      shopItems: 2", "joker: Blueprint\n    sources:\n      shopItems: [2]")]
    [InlineData("legendaryJoker: Perkeo\n    sources:\n      requireMega: true\n      boosterPacks: [1]", "legendaryJoker: Perkeo\n    sources:\n      requireMegaPack: true\n      boosterPacks: [1]")]
    [InlineData("spectralCard: Ankh\n    sources:\n      requireMega: true\n      boosterPacks: [1]", "spectralCard: Ankh\n    sources:\n      requireMegaPack: true\n      boosterPacks: [1]")]
    [InlineData("soulJoker: Perkeo\n    antes: [1]\n    edition: Negative", "legendaryJoker: Perkeo\n    antes: [1]\n    edition: Negative")]
    [InlineData("SoulJoker: Any", "legendaryJoker: Any")]
    public void Alias_LoadsTheSameClauseAsTheCanonicalKey(string alias, string canonical)
    {
        Assert.Equal(Clauses($"must:\n  - {canonical}\n"), Clauses($"must:\n  - {alias}\n"));
    }

    [Fact]
    public void AliasFilter_FindsExactlyWhatItsCanonicalTwinFinds()
    {
        TwinFilters.AssertSame(
            """
            must:
              - legendaryJoker: Any
                antes: [1, 2]
            should:
              - legendaryJoker: Perkeo
                antes: [2]
              - smallBlindTag: EtherealTag
                antes: [2]
              - spectralCard: Ankh
                antes: [1, 2]
                sources:
                  requireMegaPack: true
                  boosterPacks: [1]
              - joker: Blueprint
                antes: [3]
                sources:
                  shopItems: [2]
            """,
            """
            dateCreated: 2025-12-01T18:03:17.1203281Z
            must:
              - soulJoker: Any
                ante: [1, 2]
            should:
              - soulJoker: Perkeo
                ante: 2
              - smallBlindTag: EtherealTag
                ante: 2
              - spectralCard: Ankh
                antes: [1, 2]
                sources:
                  requireMega: true
                  boosterPacks: [1]
              - joker: Blueprint
                antes: 3
                sources:
                  shopItems: 2
            """);
    }

    [Fact]
    public void BothSpellingsOfAnte_IsAnError()
    {
        Assert.False(JamlConfigLoader.TryLoad("must:\n  - joker: Blueprint\n    antes: [1]\n    ante: 2\n", out _, out var error));
        Assert.Equal("JAML line 4: this clause writes both `ante` and `antes`: keep one.", error);
    }

    [Theory]
    [InlineData("must:\n  - joker: Blueprint\n    antes: any\n", "JAML line 3: 'any' is not a number: write a whole number, or a list of them, as in [1, 2].")]
    [InlineData("must:\n  - joker: Blueprint\n    ante: [1, two]\n", "JAML line 3: 'two' is not a number: write a whole number, or a list of them, as in [1, 2].")]
    public void ANonNumber_InANumberList_SaysSo(string yaml, string message)
    {
        Assert.False(JamlConfigLoader.TryLoad(yaml, out _, out var error));
        Assert.Equal(message, error);
    }

    [Fact]
    public void ANullAnteList_KeepsTheDefault()
    {
        // As before the single-number form: an empty value leaves the clause's own default.
        var joker = Assert.IsType<JokerClause>(Assert.Single(ProofSearch.LoadOrThrow("must:\n  - joker: Blueprint\n    ante: ~\n").Must));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], joker.Antes);
    }

    [Fact]
    public void DateCreated_IsKeptAsMetadata()
    {
        var config = ProofSearch.LoadOrThrow("name: x\ndateCreated: \"2025-12-29T22:48:00Z\"\nmust:\n  - joker: Blueprint\n");
        Assert.Equal("2025-12-29T22:48:00Z", config.DateCreated);
        Assert.Equal(Clauses("name: x\nmust:\n  - joker: Blueprint\n"), TwinFilters.AllFields(config));
    }

    [Fact]
    public void Aliases_RoundTripThroughJsonAndBack()
    {
        const string yaml = """
            name: aliases
            dateCreated: 2025-12-01T18:03:17Z
            must:
              - soulJoker: Perkeo
                ante: 2
                sources:
                  requireMega: true
                  boosterPacks: 1
            """;
        var json = JamlConfigLoader.ToJson(yaml);
        var yamlAgain = JamlConfigLoader.ToYaml(json);
        var original = ProofSearch.LoadOrThrow(yaml);
        foreach (var text in new[] { json, yamlAgain })
        {
            var config = ProofSearch.LoadOrThrow(text);
            Assert.Equal(TwinFilters.AllFields(original), TwinFilters.AllFields(config));
            Assert.Equal(original.DateCreated, config.DateCreated);
        }
    }
}
