using Motely.Lsp;

namespace Motely.Tests;

/// <summary>
/// Proves the LSP brain answers from the engine: diagnostics are the real
/// <c>JamlConfigLoader</c> speaking, hover/completion vocabulary is the engine's enums via the
/// generated <c>JamlSchema</c>. Every assertion here would break if a parallel grammar snuck in.
/// </summary>
public sealed class JamlLanguageServiceTests
{
    private const string Sample = """
        deck: Red
        stake: White
        must:
          - joker: Blueprint
            antes: [1, 2]
        """;

    // ── Diagnostics ─────────────────────────────────────────────────────────────

    [Fact]
    public void Diagnose_ValidConfig_IsClean()
    {
        Assert.Empty(JamlLanguageService.Diagnose(Sample));
    }

    [Fact]
    public void Diagnose_TagVoucher_IsClean()
    {
        var text = File.ReadAllText(Path.Combine("JamlFilters", "tag-voucher.jaml"));
        Assert.Empty(JamlLanguageService.Diagnose(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Diagnose_BlankText_IsClean(string text)
    {
        Assert.Empty(JamlLanguageService.Diagnose(text));
    }

    [Fact]
    public void Diagnose_UnknownEnumValue_ReportsOneError()
    {
        var diagnostics = JamlLanguageService.Diagnose("deck: NotARealDeck\nstake: White\n");
        var d = Assert.Single(diagnostics);
        Assert.Equal(JamlDiagnosticSeverity.Error, d.Severity);
        Assert.StartsWith("JAML", d.Code);
        Assert.Equal(0, d.Span.StartLine);
        Assert.Contains("NotARealDeck", d.Message);
        Assert.Contains("… +", d.Message); // capped known list, not every MotelyDeck name inline
    }

    [Fact]
    public void Diagnose_UnknownJokerValue_UnderlinesTheValueToken()
    {
        const string text = """
            deck: Red
            stake: White
            must:
              - joker: NotARealJoker
            """;
        var d = Assert.Single(JamlLanguageService.Diagnose(text));
        Assert.Equal(3, d.Span.StartLine);
        Assert.Contains("NotARealJoker", d.Message);
        Assert.Contains("… +", d.Message); // capped known list
        // Full MotelyJoker dump is huge; capped message stays short.
        Assert.True(d.Message.Length < 400, $"message too long ({d.Message.Length}): {d.Message}");
        // Value starts after "  - joker: "
        Assert.True(d.Span.StartColumn >= 10, $"expected value column, got {d.Span.StartColumn}");
    }

    [Fact]
    public void Diagnose_GarbageText_ReportsOneError()
    {
        var diagnostics = JamlLanguageService.Diagnose("::: not jaml at all :::");
        Assert.Single(diagnostics);
    }

    [Fact]
    public void Diagnose_SyntaxError_CarriesTheParsersSpan()
    {
        var text = "name: ok\nmust\n";
        var diagnostic = Assert.Single(JamlLanguageService.Diagnose(text));
        Assert.Equal("JAML0001", diagnostic.Code);
        Assert.Equal(1, diagnostic.Span.StartLine);
    }

    [Fact]
    public void Diagnose_UnknownKey_ThatAlsoAppearsAsAValue_UnderlinesTheKeyNotTheFirstMatch()
    {
        // "boses" is both the name value (line 0) and the unknown key (line 1). Locating the
        // squiggle by string-searching the document finds line 0; the engine's own key span
        // finds the key itself on line 1. This is exactly the drift the span-carrying loader
        // removes — the diagnostic points at what the loader rejected, not at a lookalike word.
        var text = "name: boses\nboses:\n  - joker: Blueprint\n";
        var diagnostic = Assert.Single(JamlLanguageService.Diagnose(text));
        Assert.Contains("boses", diagnostic.Message);
        Assert.Equal(1, diagnostic.Span.StartLine);
        Assert.Equal(0, diagnostic.Span.StartColumn);
        Assert.Equal("boses".Length, diagnostic.Span.EndColumn);
    }

    [Fact]
    public void Diagnose_BadBossValue_BuiltThroughItsDescriptor_IsPositioned()
    {
        // Boss builds through its descriptor now, reading its value via the span-carrying reader.
        // A value outside MotelyBossBlind is reported on the clause's line — the desc rail carries
        // a real diagnostic, not a swallowed or location-less one.
        var text = "must:\n  - boss: NotARealBoss\n";
        var diagnostic = Assert.Single(JamlLanguageService.Diagnose(text));
        Assert.Contains("NotARealBoss", diagnostic.Message);
        Assert.Equal(1, diagnostic.Span.StartLine);
    }

    [Fact]
    public void Complete_ValuePrefix_ReplaceSpanCoversTypedText()
    {
        var text = "must:\n  - joker: Lu";
        var items = JamlLanguageService.Complete(text, 1, text.Split('\n')[1].Length);
        var lucky = Assert.Single(items, i => i.Label == "LuckyCat");
        Assert.Equal(1, lucky.ReplaceSpan.StartLine);
        Assert.Equal(11, lucky.ReplaceSpan.StartColumn); // 'L' of Lu
        Assert.Equal(13, lucky.ReplaceSpan.EndColumn);
    }

    // ── Hover ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Hover_OnDiscriminator_DescribesClause()
    {
        // line 3: "  - joker: Blueprint" — cursor inside "joker"
        var hover = JamlLanguageService.Hover(Sample, 3, 5);
        Assert.NotNull(hover);
        Assert.Contains("**joker**", hover.Markdown);
        Assert.Contains("clause", hover.Markdown);
    }

    [Fact]
    public void Hover_OnEnumValue_NamesEngineEnum()
    {
        // cursor inside "Blueprint"
        var hover = JamlLanguageService.Hover(Sample, 3, 13);
        Assert.NotNull(hover);
        Assert.Contains("Blueprint", hover.Markdown);
        Assert.Contains("MotelyJoker", hover.Markdown);
    }

    [Fact]
    public void Hover_OnClauseKey_NamesOwningClause()
    {
        // line 4: "    antes: [1, 2]" — cursor inside "antes"
        var hover = JamlLanguageService.Hover(Sample, 4, 6);
        Assert.NotNull(hover);
        Assert.Contains("antes", hover.Markdown);
        Assert.Contains("joker", hover.Markdown);
    }

    [Theory]
    [InlineData(-1, 0)] // line before the document
    [InlineData(99, 0)] // line after the document
    [InlineData(0, 99)] // character beyond the line: clamps to last word or nothing
    public void Hover_OutsideAnyWord_DoesNotThrow(int line, int character)
    {
        _ = JamlLanguageService.Hover(Sample, line, character);
    }

    [Fact]
    public void Hover_OnWhitespace_ReturnsNull()
    {
        // line 2 is "must:" — column far past the colon on line 3's indent
        Assert.Null(JamlLanguageService.Hover("deck: Red\n\nmust:\n", 1, 0));
    }

    // ── Completion: keys ────────────────────────────────────────────────────────

    [Fact]
    public void Complete_AtRoot_OffersRootKeys()
    {
        var items = JamlLanguageService.Complete("de", 0, 2);
        Assert.Contains(items, i => i.Label.Equals("deck", StringComparison.OrdinalIgnoreCase));
        Assert.All(items, i => Assert.Equal("key", i.Kind));
    }

    [Fact]
    public void Complete_ListItemInMust_OffersDiscriminators()
    {
        var text = "must:\n  - jo";
        var items = JamlLanguageService.Complete(text, 1, 6);
        Assert.Contains(items, i => i.Label.Equals("joker", StringComparison.OrdinalIgnoreCase));
        Assert.All(items, i => Assert.Equal("discriminator", i.Kind));
    }

    [Fact]
    public void Complete_InsideClause_OffersClauseKeys()
    {
        var text = "must:\n  - joker: Blueprint\n    an";
        var items = JamlLanguageService.Complete(text, 2, 6);
        Assert.Contains(items, i => i.Label.Equals("antes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Complete_InsideSourcesBlock_OffersSourceKeys()
    {
        var text = "must:\n  - joker: Blueprint\n    sources:\n      ";
        var items = JamlLanguageService.Complete(text, 3, 6);
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal("key", i.Kind));
    }

    [Fact]
    public void Complete_InsideWithBlock_OffersWithKeys()
    {
        var text = "must:\n  - luckyMoney: 20\n    with:\n      ";
        var items = JamlLanguageService.Complete(text, 3, 6);
        Assert.NotEmpty(items);
    }

    // ── Completion: values ──────────────────────────────────────────────────────

    [Fact]
    public void Complete_RootKeyValue_OffersEngineEnumNames()
    {
        var items = JamlLanguageService.Complete("deck: R", 0, 7);
        Assert.Contains(items, i => i.Label == "Red");
        Assert.All(items, i => Assert.Equal("value", i.Kind));
    }

    [Fact]
    public void Complete_DiscriminatorValue_OffersEnumNames_NotAnyToken()
    {
        var text = "must:\n  - voucher: ";
        var items = JamlLanguageService.Complete(text, 1, text.Length - text.IndexOf('\n') - 1);
        Assert.Contains(items, i => i.Label == "Hieroglyph");
        // Category any is empty disc / props-only — no "Any" token on the wire.
        Assert.DoesNotContain(items, i => i.Label == "Any");
    }

    [Fact]
    public void Complete_DiscriminatorValue_PrefixFiltersAndRanksStartsFirst()
    {
        var text = "must:\n  - voucher: Over";
        var line = "  - voucher: Over";
        var items = JamlLanguageService.Complete(text, 1, line.Length);
        Assert.NotEmpty(items);
        // "Overstock"/"OverstockPlus" start with the prefix and must outrank contains-matches.
        Assert.StartsWith("Overstock", items[0].Label);
    }

    [Fact]
    public void Complete_UnknownContext_IsEmpty()
    {
        Assert.Empty(JamlLanguageService.Complete("notakey: xyz", 0, 12));
    }

    [Fact]
    public void Complete_BeyondDocument_DoesNotThrow()
    {
        _ = JamlLanguageService.Complete(Sample, 99, 0);
        _ = JamlLanguageService.Complete(Sample, -1, -1);
    }
}
