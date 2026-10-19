using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// An enum value in JAML is a name. <c>Enum.Parse</c> also reads a number as the enum's value, so
/// <c>erraticRank: 2</c> loaded as Four (Two is 0) and <c>rank: "10"</c> as Queen, with no error.
/// </summary>
public class JamlEnumNameTests
{
    [Theory]
    [InlineData("must:\n  - erraticRank: 2", 2, "'2'", "Two, Three")]
    [InlineData("must:\n  - standardCard:\n      rank: \"10\"", 3, "'10'", "Ten")]
    [InlineData("must:\n  - standardCard: 12", 2, "'12'", "")]
    [InlineData("must:\n  - joker: 5", 2, "'5'", "")]
    [InlineData("must:\n  - joker: [Blueprint, 5]", 2, "'5'", "")]
    [InlineData("must:\n  - joker: Blueprint\n    edition: 1", 3, "'1'", "Polychrome")]
    [InlineData("must:\n  - joker: Blueprint\n    stickers: [0]", 3, "'0'", "Eternal")]
    [InlineData("deck: 3\nmust:\n  - joker: Blueprint", 1, "'3'", "Ghost")]
    [InlineData("{\"must\": [{\"erraticRank\": 2}]}", 1, "'2'", "Two")]
    public void ANumberIsRefusedOnItsLine(string jaml, int line, string quoted, string names)
    {
        var ok = JamlConfigLoader.TryLoad(jaml, out _, out var error);

        Assert.False(ok);
        Assert.StartsWith($"JAML line {line}: {quoted} is not a name", error);
        Assert.Contains(names, error);
    }

    [Fact]
    public void LuckIsItsNumber()
    {
        // MotelyLuck's value is the multiplier (X4 = 4), so the number says what it means.
        var config = JamlConfigLoader.FromJaml("must:\n  - luckyMoney: [0]\n    with:\n      luck: 4");

        Assert.Equal(MotelyLuck.X4, Assert.IsType<LuckyMoneyClause>(config.Must[0]).With.Luck);
    }

    [Fact]
    public void LuckThatIsNoValueIsRefused()
    {
        // Enum.Parse took "3" as (MotelyLuck)3, a multiplier no Oops count gives.
        var ok = JamlConfigLoader.TryLoad("must:\n  - luckyMoney: [0]\n    with:\n      luck: 3", out _, out var error);

        Assert.False(ok);
        Assert.StartsWith("JAML line 4: '3' is not a value: write one of X1, X2, X4", error);
    }

    [Fact]
    public void ACommaListInOneScalarIsRefused()
    {
        // Enum.Parse reads "A, B" as A | B: two vouchers OR'd into whichever one has that value.
        var ok = JamlConfigLoader.TryLoad("must:\n  - voucher: \"Hone, Overstock\"", out _, out var error);

        Assert.False(ok);
        Assert.StartsWith("JAML line 2: 'Hone, Overstock' holds more than one name", error);
    }

    [Fact]
    public void NamesBindInAnyCase()
    {
        var config = JamlConfigLoader.FromJaml("""
            deck: ghost
            stake: GOLD
            must:
              - erraticRank: two
              - joker: [blueprint, BRAINSTORM]
                edition: negative
                stickers: [eternal]
              - standardCard:
                  rank: ten
                  suit: hearts
            """);

        Assert.Equal(MotelyDeck.Ghost, config.Deck);
        Assert.Equal(MotelyStake.Gold, config.Stake);
        Assert.Equal(MotelyStandardcardRank.Two, Assert.IsType<ErraticRankClause>(config.Must[0]).Rank);
        var joker = Assert.IsType<JokerClause>(config.Must[1]);
        Assert.Equal([MotelyJoker.Blueprint, MotelyJoker.Brainstorm], joker.Jokers);
        Assert.Equal(MotelyItemEdition.Negative, joker.Edition);
        Assert.Equal([MotelyJokerSticker.Eternal], joker.Stickers);
        var card = Assert.IsType<StandardCardClause>(config.Must[2]);
        Assert.Equal(MotelyStandardcardRank.Ten, card.Rank);
        Assert.Equal(MotelyStandardcardSuit.Hearts, card.Suit);
    }

    [Fact]
    public void ANullEnumStaysUnset()
    {
        var config = JamlConfigLoader.FromJaml("""
            must:
              - standardCard:
                  rank: ~
                  suit: Hearts
            """);

        var card = Assert.IsType<StandardCardClause>(config.Must[0]);
        Assert.Null(card.Rank);
        Assert.Equal(MotelyStandardcardSuit.Hearts, card.Suit);
    }

    [Fact]
    public void AnUnknownNameStillNamesItself()
    {
        var ok = JamlConfigLoader.TryLoad("must:\n  - joker: Blueprnt", out _, out var error);

        Assert.False(ok);
        Assert.Contains("Blueprnt", error);
    }
}
