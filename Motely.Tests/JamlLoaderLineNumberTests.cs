namespace Motely.Tests;

/// <summary>
/// Clause-level loader errors name the line the bad text is on. VYaml's CurrentMark is the
/// tokenizer's position, and after a plain scalar the tokenizer has already looked ahead to the
/// next key (or past the end of the file), so reading the mark at a value scalar used to give
/// the following line: Zerkeo `antes: 1-8` on 19 reported as 20, M.yml `Showman` on 19 (of 20)
/// reported as 20, and so on. Each case is the shape of a real filter that failed to load.
/// In flow style (every .json filter, `- {joker: X}`, `[1,\n 2]`) the tokenizer reads a whole
/// collection ahead, so no mark is on the right line: lines come from JamlScalarLines, which
/// reads them off the source.
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
        // The line comes from the source walk, not the mark fallback.
        Assert.NotNull(JamlScalarLines.TryBuild(System.Text.Encoding.UTF8.GetBytes(yaml)));
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
        Assert.Contains("`Showman` is not a MotelyJokerCommon (key `commonJoker`)", LoadError(yaml));
    }

    [Fact]
    public void BadDiscriminatorValue_NamesTheKeyAsWritten()
    {
        // `joker: X` binds to the C# property `Jokers`; the error quotes the page's key.
        const string yaml = "name: probe\nmust:\n  - joker: NotAJoker\n";
        AssertLine(yaml, "joker: NotAJoker", 3);
        var error = LoadError(yaml);
        Assert.Contains("`NotAJoker` is not a MotelyJoker (key `joker`)", error);
        Assert.DoesNotContain("`Jokers`", error);
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
    public void FlowSequenceItem_OnContinuationLine_ReportsItsOwnLine()
    {
        // The tokenizer stops at the `,` after `1`, so a mark taken there is on the `[1,` line.
        const string yaml = """
            name: c
            deck: Red
            must:
              - joker: Blueprint
                antes: [1,
                  x, 3]
                score: 2
            """;
        AssertLine(yaml, "x, 3]", 6);
        Assert.Contains("`x` is not an integer (key `antes`)", LoadError(yaml));
    }

    [Fact]
    public void BadValue_FollowedByAKeyWithTheSameText_ReportsItsOwnLine()
    {
        const string yaml = """
            name: v
            must:
              - joker: Blueprint
                score: x
                x: 1
            """;
        AssertLine(yaml, "score: x", 4);
    }

    [Fact]
    public void Json_MultiLine_ReportsTheItemsLine()
    {
        // .json goes through the same loader; the tokenizer reads a whole `{ … }` ahead.
        const string json = """
            {
              "name": "bad2",
              "deck": "Red",
              "must": [
                { "joker": "Blueprint", "antes": [1, 2] },
                { "joker": "NotAJoker" }
              ]
            }
            """;
        AssertLine(json, "NotAJoker", 6);
        Assert.Contains("`NotAJoker` is not a MotelyJoker", LoadError(json));
    }

    [Fact]
    public void Json_OneLine_ReportsLineOne()
    {
        const string json = """{"name":"bad2","deck":"Red","must":[{"joker":"NotAJoker"}]}""";
        AssertLine(json, "NotAJoker", 1);
    }

    [Fact]
    public void Json_BadNestedValue_AfterEscapedStrings_ReportsItsOwnLine()
    {
        const string json = """
            {
              "name": "esc \"quoted\" \u0041",
              "description": "a:b, [c] {d} # not a comment",
              "must": [
                {
                  "joker": "Blueprint",
                  "sources": {
                    "shopItems": [0, 1],
                    "boosterPacks": "x"
                  }
                }
              ]
            }
            """;
        AssertLine(json, "\"boosterPacks\": \"x\"", 9);
    }

    [Fact]
    public void Json_UnknownRootKey_ReportsItsOwnLine()
    {
        const string json = """
            {"name": "x",
             "boses": []}
            """;
        AssertLine(json, "boses", 2);
        Assert.Contains("unknown key 'boses'", LoadError(json));
    }

    [Fact]
    public void FlowMappingItem_ReportsItsOwnLine()
    {
        const string yaml = """
            name: s
            deck: Red
            must:
              - joker: Blueprint

              - {joker: Nope}
              - joker: Baron
            """;
        AssertLine(yaml, "{joker: Nope}", 6);
    }

    [Fact]
    public void ClauseListGivenAScalar_ReportsItsLine()
    {
        const string yaml = """
            name: s
            deck: Red
            must: joker
            should: []
            """;
        AssertLine(yaml, "must: joker", 3);
        Assert.Contains("lists of clauses", LoadError(yaml));
    }

    [Fact]
    public void BadValue_AfterBlockScalarsAnchorsTagsAndQuotes_ReportsItsOwnLine()
    {
        // Everything the line walk has to step over between two scalars.
        const string yaml = """
            %YAML 1.2
            ---
            name: 'it''s # here'
            description: |
              line one: [not] {a} - flow
                # indented, still text

              - x
            author: >-
              folded
              plain
            deck: !!str Red
            should:
              - &bp
                joker: Blueprint
                antes: [1, 2]
              - *bp
              - joker: "Baron"   # a comment: with, [flow] chars
                label: multi
                  line plain
                antes: [1,
                   2, y]
            """;
        AssertLine(yaml, "2, y]", 22);
    }

    [Fact]
    public void BlockUnderANameDiscriminator_SaysSo()
    {
        // NegativePerkeoAnte3FirstArcana.jaml before it was fixed: the clause's keys indented
        // under `legendaryJoker:`. Used to read "`` is not a MotelyJoker".
        const string yaml = """
            name: Negative Perkeo in Ante 3 First Arcana Pack
            deck: Red
            must:
              - legendaryJoker:
                  jokers: [Perkeo]
                  edition: Negative
                  antes: [3]
            """;
        AssertLine(yaml, "legendaryJoker:", 4);
        var error = LoadError(yaml);
        Assert.Contains("`legendaryJoker:` takes a name like `legendaryJoker: Perkeo`, not a block of keys", error);
        Assert.Contains("jokers:, edition:, antes:", error);
        Assert.DoesNotContain("``", error);
    }

    [Fact]
    public void BlockWhereASingleValueGoes_SaysSo()
    {
        const string yaml = """
            name: s
            must:
              - joker: Blueprint
                score:
                  value: 2
            """;
        AssertLine(yaml, "score:", 4);
        Assert.Contains("`score` takes a single value, not a block of keys (value:)", LoadError(yaml));
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
