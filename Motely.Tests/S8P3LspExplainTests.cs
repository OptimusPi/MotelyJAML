using Motely.Lsp;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// S8.P3 — <c>JamlLanguageService.Explain</c> and the residual hover arms. Every expected
/// string is engine-derived (JamlSchema / engine enums), never a hand-authored vocabulary.
/// </summary>
public sealed class S8P3LspExplainTests
{
    // ── Explain: discriminators ─────────────────────────────────────────────

    [Fact]
    public void Explain_JokerDiscriminator_SpeaksTheEngineSchema()
    {
        var md = JamlLanguageService.Explain("joker");
        Assert.NotNull(md);
        Assert.Contains("`joker`", md);
        Assert.Contains(nameof(MotelyJoker), md);
        Assert.Contains("Clause keys", md);
        Assert.Contains("Sample values", md);
        // Joker clauses expose source keys through the same rail the loader validates.
        Assert.Contains("Source keys", md);
        Assert.Contains("`shopItems`", md);
    }

    [Fact]
    public void Explain_DiscriminatorWithQuery_ListsEngineMatches()
    {
        var md = JamlLanguageService.Explain("joker lucky");
        Assert.NotNull(md);
        Assert.Contains("Matches for `lucky`", md);
        Assert.Contains(nameof(MotelyJoker.LuckyCat), md);
    }

    [Fact]
    public void Explain_RollsInlineDiscriminator_SaysSo()
    {
        // Engine-sourced pick: whichever discriminator the schema marks rolls-inline.
        var disc = JamlSchema.Discriminators.FirstOrDefault(JamlSchema.RollsAreInlineFor);
        Assert.False(string.IsNullOrEmpty(disc), "schema exposes no rolls-inline clause");
        var md = JamlLanguageService.Explain(disc);
        Assert.NotNull(md);
        Assert.Contains("Rolls event", md);
    }

    // ── Explain: root keys ──────────────────────────────────────────────────

    [Theory]
    [InlineData("must", "hard requirements")]
    [InlineData("should", "soft scoring")]
    [InlineData("mustNot", "forbidden")]
    [InlineData("deck", "MotelyDeck")]
    [InlineData("stake", "MotelyStake")]
    [InlineData("seeds", "seed list")]
    [InlineData("name", "title")]
    public void Explain_RootKeys_AnswerFromTheSwitch(string key, string expected)
    {
        var md = JamlLanguageService.Explain(key);
        Assert.NotNull(md);
        Assert.Contains(expected, md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explain_OtherRootKeys_FallBackToGenericLine()
    {
        var uncased = JamlConfig.RootKeys.FirstOrDefault(k =>
            k is not ("must" or "should" or "mustNot" or "deck" or "stake"
                or "seeds" or "name")
        );
        Assert.False(string.IsNullOrEmpty(uncased), "every root key has a bespoke arm");
        var md = JamlLanguageService.Explain(uncased);
        Assert.NotNull(md);
        Assert.Contains("JAML root key", md);
    }

    [Fact]
    public void Explain_Antes_AsClauseKeyFromSchema()
    {
        var md = JamlLanguageService.Explain("antes");
        Assert.NotNull(md);
        Assert.Contains("`antes`", md);
        Assert.Contains("clause key", md);
        Assert.Contains("`joker`", md); // engine-owned owners list
    }

    // ── Explain: vocabulary ─────────────────────────────────────────────────

    [Fact]
    public void Explain_EnumName_NamesItsKindAndEnum()
    {
        var md = JamlLanguageService.Explain("Blueprint");
        Assert.NotNull(md);
        Assert.Contains("`Blueprint`", md);
        Assert.Contains(nameof(MotelyJoker), md);
    }

    [Fact]
    public void Explain_PropertyKindWithQuery_ListsMatches()
    {
        // "edition" is a clause property key whose type is an engine enum — the ListItems
        // fallback, not a discriminator or root key.
        var md = JamlLanguageService.Explain("edition foil");
        Assert.NotNull(md);
        Assert.Contains(nameof(MotelyItemEdition.Foil), md);
    }

    [Fact]
    public void Explain_SmallPropertyKind_ListsAllNames()
    {
        var md = JamlLanguageService.Explain("edition");
        Assert.NotNull(md);
        Assert.Contains("engine names", md);
        Assert.Contains(nameof(MotelyItemEdition.Negative), md);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzNotAThing")]
    [InlineData("zzzNotAKind zzzQuery")]
    public void Explain_UnknownOrEmptyTopics_ReturnNull(string topic)
    {
        Assert.Null(JamlLanguageService.Explain(topic));
    }

    // ── Hover residual arms ─────────────────────────────────────────────────

    [Fact]
    public void Hover_RollsInlineDiscriminator_MentionsRollsEvent()
    {
        var disc = JamlSchema.Discriminators.First(JamlSchema.RollsAreInlineFor);
        var text = $"must:\n  - {disc}: [0]";
        var hover = JamlLanguageService.Hover(text, 1, 4 + disc.Length / 2);
        Assert.NotNull(hover);
        Assert.Contains("Rolls event", hover.Markdown);
    }

    [Fact]
    public void Hover_ClauseKeyInsideClause_NamesTheOwningClause()
    {
        const string text = """
            must:
              - joker: Blueprint
                antes: [1]
            """;
        var hover = JamlLanguageService.Hover(text, 2, 5);
        Assert.NotNull(hover);
        Assert.Contains("key of the **joker** clause", hover.Markdown);
    }

    [Fact]
    public void Hover_UnknownWord_ReturnsNull()
    {
        Assert.Null(JamlLanguageService.Hover("zzzUnknownWord: 1", 0, 2));
    }
}
