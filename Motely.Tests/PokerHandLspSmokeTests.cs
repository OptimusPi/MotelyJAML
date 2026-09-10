using Motely.Lsp;

namespace Motely.Tests;

/// <summary>LSP + schema rail for pokerHand — engine vocab only, no TS mirror.</summary>
public sealed class PokerHandLspSmokeTests
{
    [Fact]
    public void Schema_KnowsPokerHand()
    {
        Assert.True(JamlSchema.IsKnownDiscriminator("pokerHand"));
        Assert.True(JamlSchema.IsKnownDiscriminator("pokerHands"));
        Assert.Equal(typeof(MotelyPokerHand), JamlSchema.ValueEnumTypeFor("pokerHand"));
        Assert.Contains("antes", JamlSchema.ClauseKeysFor("pokerHand"));
    }

    [Fact]
    public void Complete_OffersPokerHand_AndEnumValues()
    {
        var discs = JamlLanguageService.Complete("must:\n  - pok", 1, 6);
        Assert.Contains(discs, i => i.Label.Equals("pokerHand", StringComparison.OrdinalIgnoreCase));

        var text = "must:\n  - pokerHand: Fou";
        var items = JamlLanguageService.Complete(text, 1, text.Split('\n')[1].Length);
        Assert.Contains(items, i => i.Label.Equals("FourOfAKind", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Diagnose_AcceptsPokerHandDocument()
    {
        const string jaml = """
            deck: Red
            stake: White
            must:
              - pokerHand: FourOfAKind
                antes: [1]
            """;
        Assert.Empty(JamlLanguageService.Diagnose(jaml));
    }
}
