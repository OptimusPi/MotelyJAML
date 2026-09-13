using Motely.Filters.Jaml;
using Xunit;

namespace Motely.Tests;

/// <summary>
/// One range grammar for the whole JAML file. <c>JamlLine</c> already reads <c>1-8</c>, <c>1..8</c>
/// and <c>3–6</c> (en dash), ascending or descending. The clause loader's int arrays read the same
/// tokens, so <c>luckyMoney: [1..99]</c> loads exactly like <c>luckyMoney: [1-99]</c>, and antes
/// written as <c>0-39</c> cover the whole ante space.
/// </summary>
public sealed class JamlIntRangeTokenTests
{
    private static string LuckyMoneyRolls(string token) => $"""
        name: range-test
        deck: Red
        stake: White
        should:
          - luckyMoney: [{token}]
        """;

    private static int[] LoadRolls(string jaml)
    {
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        Assert.Single(config!.Should);
        var clause = config.Should[0] as LuckyMoneyClause;
        Assert.NotNull(clause);
        return clause.Rolls;
    }

    [Theory]
    [InlineData("1-99")]
    [InlineData("1..99")]
    [InlineData("1–99")]
    [InlineData("1 - 99")]
    public void EveryRangeSpelling_ExpandsToTheSameRolls(string token)
    {
        int[] rolls = LoadRolls(LuckyMoneyRolls(token));

        Assert.Equal(Enumerable.Range(1, 99), rolls);
    }

    [Fact]
    public void DescendingRange_KeepsTheWrittenOrder()
    {
        int[] rolls = LoadRolls(LuckyMoneyRolls("8..1"));

        Assert.Equal([8, 7, 6, 5, 4, 3, 2, 1], rolls);
    }

    [Fact]
    public void MixedListAndRange_ExpandsInPlace()
    {
        int[] rolls = LoadRolls(LuckyMoneyRolls("0, 2..4, 9"));

        Assert.Equal([0, 2, 3, 4, 9], rolls);
    }

    // On a shop family: ante 0 is Hieroglyph's extra pack round there, while voucher/boss/tag
    // start at ante 1 and the loader now refuses 0 for them.
    [Fact]
    public void Antes_ZeroToThirtyNine_IsTheWholeAnteSpace()
    {
        const string jaml = """
            name: antes-0-39
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [0..39]
            """;

        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        Assert.Single(config!.Must);
        var clause = config.Must[0] as IAnteScopedClause;
        Assert.NotNull(clause);
        Assert.Equal(Enumerable.Range(0, 40), clause.Antes);
    }
}
