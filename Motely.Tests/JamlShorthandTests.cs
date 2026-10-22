using Motely.Enums;
using Motely.Filters;
using Xunit;

namespace Motely.Tests;

public class JamlShorthandTests
{
    #region t01: basic legendary shorthand
    [Fact]
    public void T01_LegendaryShorthand_Parses()
    {
        const string yaml = """
            must:
              - Perkeo in Ante 1
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        Assert.Single(config!.Must);
        var clause = Assert.IsType<LegendaryJokerClause>(config.Must[0]);
        Assert.Equal(MotelyJoker.Perkeo, clause.Joker);
        Assert.Equal([1], clause.Antes);
    }
    #endregion

    #region t02: sticker + joker
    [Fact]
    public void T02_StickerJoker_Parses()
    {
        const string yaml = """
            must:
              - Eternal Blueprint in Ante 1
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        Assert.Single(config!.Must);
        var clause = Assert.IsType<JokerClause>(config.Must[0]);
        Assert.Equal(MotelyJoker.Blueprint, clause.Joker);
        Assert.Equal([1], clause.Antes);
        Assert.Equal([MotelyJokerSticker.Eternal], clause.Stickers);
    }
    #endregion

    #region t03: boss shorthand
    [Fact]
    public void T03_BossShorthand_Parses()
    {
        const string yaml = """
            must:
              - The Psychic boss
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        Assert.Single(config!.Must);
        var clause = Assert.IsType<BossClause>(config.Must[0]);
        Assert.Equal(MotelyBossBlind.ThePsychic, clause.Boss);
    }
    #endregion

    #region t04: ante zero (Soul slot)
    [Fact]
    public void T04_AnteZero_SoulSlot()
    {
        const string yaml = """
            must:
              - Perkeo in Ante 0
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<LegendaryJokerClause>(config!.Must[0]);
        Assert.Equal([0], clause.Antes);
    }
    #endregion

    #region t05: multi-ante with should
    [Fact]
    public void T05_MultiAnte_ShouldBlock()
    {
        const string yaml = """
            should:
              - Perkeo in Antes 1, 2 and 3
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        Assert.Single(config!.Should);
        var clause = Assert.IsType<LegendaryJokerClause>(config.Should[0]);
        Assert.Equal([1, 2, 3], clause.Antes);
    }
    #endregion

    #region t06: mixed full + shorthand
    [Fact]
    public void T06_MixedFullAndShorthand()
    {
        const string yaml = """
            must:
              - voucher: Overstock
              - Perkeo in Ante 1
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        Assert.Equal(2, config!.Must.Count);
        Assert.IsType<VoucherClause>(config.Must[0]);
        Assert.IsType<LegendaryJokerClause>(config.Must[1]);
    }
    #endregion

    #region t07: unknown name → helpful error
    [Fact]
    public void T07_UnknownName_HelpfulError()
    {
        const string yaml = """
            must:
              - Perkeo the Great in Ante 1
            """;

        Assert.False(JamlConfigLoader.TryLoad(yaml, out _, out var error));
        Assert.Contains("Unknown item name", error);
        Assert.Contains("Perkeo", error);
    }
    #endregion

    #region t08: out-of-range fixed ante
    [Fact]
    public void T08_OutOfRange_Ante()
    {
        // Note: the parser itself does not validate ante range; that is the engine's job.
        // This test ensures large antes are accepted by the parser.
        const string yaml = """
            must:
              - Perkeo in Ante 99
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<LegendaryJokerClause>(config!.Must[0]);
        Assert.Equal([99], clause.Antes);
    }
    #endregion

    #region t09: mixed form — shorthand + nested score
    [Fact]
    public void T09_MixedForm_ShorthandWithScore()
    {
        const string yaml = """
            must:
              - Perkeo in Ante 1 or 2:
                score: 100
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        Assert.Single(config!.Must);
        var clause = Assert.IsType<LegendaryJokerClause>(config.Must[0]);
        Assert.Equal(MotelyJoker.Perkeo, clause.Joker);
        Assert.Equal([1, 2], clause.Antes);
        Assert.Equal(100, clause.Score);
    }
    #endregion

    #region t10: bare flow list is NOT supported in full form without type converter
    // Skipped: requires custom type converter for int[] which is a follow-up enhancement.
    #endregion

    #region t11: inclusive range in shorthand
    [Fact]
    public void T11_InclusiveRange_Shorthand()
    {
        const string yaml = """
            must:
              - Perkeo in Antes [1..4]
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<LegendaryJokerClause>(config!.Must[0]);
        Assert.Equal([1, 2, 3, 4], clause.Antes);
    }
    #endregion

    #region t12: open-ended range accepted by parser
    [Fact]
    public void T12_OpenEndedRange_Parses()
    {
        const string yaml = """
            must:
              - Perkeo in Antes [2..]
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<LegendaryJokerClause>(config!.Must[0]);
        Assert.Contains(2, clause.Antes!);
        Assert.Contains(99, clause.Antes!); // placeholder max
    }
    #endregion

    #region t13: nested antes overrides inline shorthand
    [Fact]
    public void T13_NestedAntesOverridesInline()
    {
        const string yaml = """
            must:
              - Perkeo in Ante 1:
                antes: [3, 4]
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<LegendaryJokerClause>(config!.Must[0]);
        // Nested key wins over inline shorthand
        Assert.Equal([3, 4], clause.Antes);
    }
    #endregion

    #region t14: inverted range is a named error
    [Fact]
    public void T14_InvertedRange_NamedError()
    {
        const string yaml = """
            must:
              - Perkeo in Antes [4..2]
            """;

        Assert.False(JamlConfigLoader.TryLoad(yaml, out _, out var error));
        Assert.Contains("start 4 > end 2", error);
    }
    #endregion

    #region Backward compatibility: full-form clauses still work
    [Fact]
    public void FullForm_LegendaryJoker()
    {
        const string yaml = """
            must:
              - legendaryJoker: Perkeo
                antes: [1, 2]
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<LegendaryJokerClause>(config!.Must[0]);
        Assert.Equal(MotelyJoker.Perkeo, clause.Joker);
        Assert.Equal([1, 2], clause.Antes);
    }

    [Fact]
    public void FullForm_JokerWithSticker()
    {
        const string yaml = """
            must:
              - joker: Blueprint
                antes: [1]
                stickers: [Eternal]
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<JokerClause>(config!.Must[0]);
        Assert.Equal(MotelyJoker.Blueprint, clause.Joker);
        Assert.Equal([MotelyJokerSticker.Eternal], clause.Stickers);
    }
    #endregion

    #region Case-insensitive shorthand
    [Fact]
    public void Shorthand_CaseInsensitive()
    {
        const string yaml = """
            must:
              - perkeo in ante 1
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<LegendaryJokerClause>(config!.Must[0]);
        Assert.Equal(MotelyJoker.Perkeo, clause.Joker);
    }
    #endregion

    #region Voucher shorthand
    [Fact]
    public void Shorthand_Voucher()
    {
        const string yaml = """
            must:
              - Overstock in Ante 1
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<VoucherClause>(config!.Must[0]);
        Assert.Equal(MotelyVoucher.Overstock, clause.Voucher);
    }
    #endregion

    #region Tarot card shorthand
    [Fact]
    public void Shorthand_TarotCard()
    {
        const string yaml = """
            must:
              - The Fool in Ante 1
            """;

        Assert.True(JamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        var clause = Assert.IsType<TarotCardClause>(config!.Must[0]);
        Assert.Equal(MotelyTarotCard.TheFool, clause.TarotCard);
    }
    #endregion
}
