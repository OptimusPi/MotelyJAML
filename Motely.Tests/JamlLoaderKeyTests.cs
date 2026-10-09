using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// The two traps in VYaml's generated deserializer, which the loader does not use: it assigns
/// every property from <c>default(T)</c>, so an omitted key wipes the class initializer, and it
/// skips unknown keys, so a typo loads as nothing.
/// </summary>
public class JamlLoaderKeyTests
{
    [Fact]
    public void OmittedClauseKeys_KeepTheirInitializers()
    {
        var config = YamlConfigLoader.FromYaml("""
            must:
              - joker: Blueprint
              - smallBlindTag: NegativeTag
              - luckyMoney: 1
            """);

        var joker = Assert.IsType<JokerClause>(config.Must[0]);
        Assert.Equal(1, joker.Score);
        Assert.Equal(1, joker.Min);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], joker.Antes);
        Assert.Empty(joker.Stickers);
        Assert.Null(joker.Sources);

        Assert.Equal([0], Assert.IsType<SmallBlindTagClause>(config.Must[1]).Rolls);
        Assert.Equal(MotelyLuck.X1, Assert.IsType<LuckyMoneyClause>(config.Must[2]).With.Luck);
    }

    [Fact]
    public void OmittedDocumentKeys_KeepTheirInitializers()
    {
        var config = YamlConfigLoader.FromYaml("name: only a name");

        Assert.NotNull(config.Must);
        Assert.Empty(config.Must);
        Assert.NotNull(config.Should);
        Assert.Empty(config.Should);
        Assert.NotNull(config.MustNot);
        Assert.Empty(config.MustNot);
        Assert.Empty(config.Seeds);
        Assert.Equal("", config.Id);
        Assert.Equal(MotelyDeck.Red, config.Deck);
        Assert.Equal(MotelyStake.White, config.Stake);
    }

    [Fact]
    public void UnknownClauseKey_IsRejectedOnItsLine()
    {
        var ok = YamlConfigLoader.TryLoad("""
            name: typo
            must:
              - joker: Blueprint
              - joker: Brainstorm
                score: 2
                antse: [1]
            """, out _, out var error);

        Assert.False(ok);
        Assert.Contains("YAML line 6", error);
        Assert.Contains("'antse'", error);
    }

    [Fact]
    public void UnknownSourcesKey_IsRejectedOnItsLine()
    {
        var ok = YamlConfigLoader.TryLoad("""
            must:
              - joker: Blueprint
                sources:
                  shopItem: [0, 1]
            """, out _, out var error);

        Assert.False(ok);
        Assert.Contains("YAML line 4", error);
        Assert.Contains("'shopItem'", error);
    }
}
