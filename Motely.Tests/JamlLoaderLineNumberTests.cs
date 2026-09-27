namespace Motely.Tests;

/// <summary>
/// Clause-level loader errors name the line the bad text is on. VYaml's CurrentMark is the
/// tokenizer's position, and after a plain scalar the tokenizer has already looked ahead to the
/// next key (or past the end of the file), so reading the mark at a value scalar used to give
/// the following line: Zerkeo `antes: 1-8` on 19 reported as 20, M.yml `Showman` on 19 (of 20)
/// reported as 20, and so on. Each case is the shape of a real filter that failed to load.
/// </summary>
public sealed class JamlLoaderLineNumberTests
{
    private static string LoadError(string yaml)
    {
        var ok = JamlConfigLoader.TryLoad(yaml, out _, out var error);
        Assert.False(ok);
        return error!;
    }

    /// <summary>1-based line of the first line containing <paramref name="marker"/>.</summary>
    private static int LineOf(string yaml, string marker)
    {
        var lines = yaml.Split('\n');
        var i = Array.FindIndex(lines, l => l.Contains(marker, StringComparison.Ordinal));
        Assert.True(i >= 0, $"marker `{marker}` not in the test YAML");
        return i + 1;
    }

    private static void AssertLine(string yaml, string marker, int expected)
    {
        Assert.Equal(expected, LineOf(yaml, marker)); // the test's own arithmetic
        var error = LoadError(yaml);
        Assert.StartsWith($"JAML line {expected}:", error);
    }

    [Fact]
    public void RangeShorthand_InBlockMapping_ReportsItsOwnLine()
    {
        // Zerkeo.jaml before 0a77ab2: `antes: 1-8` followed by another key.
        const string yaml = """
            name: zerkeo
            deck: Red
            should:
              - joker: Perkeo
                antes: 1-8
                score: 1
            """;
        AssertLine(yaml, "antes: 1-8", 5);
        Assert.Contains("`1-8` is not an integer (key `antes`)", LoadError(yaml));
    }

    [Fact]
    public void StrayPipe_OnScore_ReportsItsOwnLine()
    {
        // ColaOopsLite.jaml before 0a77ab2: `score: 1|`.
        const string yaml = """
            name: cola
            should:
              - voucher: Petroglyph
                antes: [1,2]
                score: 1|
              - joker: OopsAll6s
                score: 10
            """;
        AssertLine(yaml, "score: 1|", 5);
    }

    [Fact]
    public void WrongRarityDiscriminator_NearEndOfFileWithoutNewline_ReportsItsOwnLine()
    {
        // M.yml before 0a77ab2: `commonJoker: Showman` two lines from the end, no final newline.
        const string yaml = "name: m\nshould:\n  - rareJoker: InvisibleJoker\n    antes: [1]\n"
                          + "  - commonJoker: Showman\n    antes: [1, 2]\n    score: 80";
        AssertLine(yaml, "commonJoker: Showman", 5);
        Assert.Contains("`Showman` is not a MotelyJokerCommon", LoadError(yaml));
    }

    [Fact]
    public void BadValue_OnLastLineWithoutNewline_ReportsThatLine()
    {
        const string yaml = "name: last\nmust:\n  - joker: Blueprint\n    score: x";
        AssertLine(yaml, "score: x", 4);
    }

    [Fact]
    public void UnknownKey_WithBlockValue_ReportsTheKeyLine()
    {
        // faceding.jaml before 0a77ab2: `or:` indented as a key of the smallBlindTag clause.
        const string yaml = """
            name: faceding
            should:
              - and:
                - smallBlindTag: NegativeTag
                  or:
                  - jokers: [SockAndBuskin, HangingChad, Photograph]
                    sources:
                      shopItems: [2,3]
                  antes: [3]
            """;
        AssertLine(yaml, "or:", 5);
        Assert.Contains("unknown key 'or'", LoadError(yaml));
    }

    [Fact]
    public void NestedMapValue_UnderDiscriminator_ReportsTheInnerKeyLine()
    {
        // loki.jaml before 910d6c2: `rank: K` inside `standardCard:`.
        const string yaml = """
            name: loki
            must:
              - standardCard:
                  rank: K
                  seal: Red
            """;
        AssertLine(yaml, "rank: K", 4);
    }

    [Fact]
    public void BadValue_AfterCommentsAndBlankLines_ReportsItsOwnLine()
    {
        const string yaml = """
            name: comments
            must:
              - joker: Blueprint
                # a comment

                # another
                antes: 1-8   # trailing comment

                score: 2
            """;
        AssertLine(yaml, "antes: 1-8", 7);
    }

    [Fact]
    public void BlockSequenceItems_ReportTheirOwnLines()
    {
        const string first = """
            name: seq
            must:
              - joker: Blueprint
                antes:
                  - x
                  - 2
            """;
        AssertLine(first, "- x", 5);

        const string middle = """
            name: seq
            must:
              - joker: Blueprint
                antes:
                  - 1
                  - x
                  - 3
            """;
        AssertLine(middle, "- x", 6);

        const string lastAfterBlank = """
            name: seq
            must:
              - joker: Blueprint
                antes:
                  - 1
                  # skip

                  - x
            """;
        AssertLine(lastAfterBlank, "- x", 8);
    }

    [Fact]
    public void FlowSequenceItem_ReportsItsOwnLine()
    {
        const string yaml = """
            name: flow
            must:
              - joker: Blueprint
                antes: [1, x]
                score: 2
            """;
        AssertLine(yaml, "antes: [1, x]", 4);
    }

    [Fact]
    public void UnknownClauseKey_ReportsTheKeyLine()
    {
        const string yaml = """
            name: typo
            must:
              - joker: Blueprint
                antez: [1]
                score: 2
            """;
        AssertLine(yaml, "antez:", 4);
    }

    [Fact]
    public void BadValue_AfterNestedBlocks_ReportsItsOwnLine()
    {
        const string yaml = """
            name: nested
            must:
              - joker: Blueprint
                sources:
                  shopItems: [0, 1]
                  boosterPacks:
                    - 0
                    - 1
                score: x
            """;
        AssertLine(yaml, "score: x", 9);
    }

    [Fact]
    public void BadNestedValue_ReportsItsOwnLine()
    {
        const string yaml = """
            name: nested
            must:
              - joker: Blueprint
                sources:
                  shopItems: [0, 1]
                  boosterPacks: x
                score: 2
            """;
        AssertLine(yaml, "boosterPacks: x", 6);
    }

    [Fact]
    public void ScalarClause_ReportsItsOwnLine()
    {
        const string yaml = """
            name: scalar
            must:
              - Blueprint
              - joker: Baron
            """;
        AssertLine(yaml, "- Blueprint", 3);
        Assert.Contains("a clause must be a mapping", LoadError(yaml));
    }

    [Fact]
    public void ClauseList_NullAndEmpty_StillLoad()
    {
        var config = JamlConfigLoader.FromJaml("""
            name: lists
            must:
            should: []
            mustNot:
              - joker: Baron
            """);
        Assert.Empty(config.Must);
        Assert.Empty(config.Should);
        Assert.Single(config.MustNot);
    }

    [Fact]
    public void ClauseList_AnchorAndAlias_StillLoad()
    {
        var config = JamlConfigLoader.FromJaml("""
            name: anchors
            must:
              - &bp
                joker: Blueprint
                antes: [1, 2]
            should:
              - *bp
              - joker: Baron
            """);
        Assert.Single(config.Must);
        Assert.Equal(2, config.Should.Count);
        Assert.Same(config.Must[0], config.Should[0]);
    }
}
