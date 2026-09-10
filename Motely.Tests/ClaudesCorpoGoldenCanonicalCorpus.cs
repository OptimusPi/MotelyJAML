using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// Every name the engine ships must load as JAML and plan.
/// Names come from <see cref="JamlSchema"/> / the enums — never a hand-typed list.
/// The RAG copies live in seedfinder.app/corpus; this is the engine lock.
/// </summary>
public sealed class ClaudesCorpoGoldenCanonicalCorpus
{
    public static TheoryData<string, string> EveryNamedItem()
    {
        var data = new TheoryData<string, string>();
        foreach (var (wire, enumType) in ValueEnumWires())
        {
            IEnumerable<string> names =
                wire.Equals("legendaryJoker", StringComparison.OrdinalIgnoreCase)
                    ? Enum.GetNames<MotelyJokerLegendary>()
                    : Enum.GetNames(enumType);
            foreach (var name in names)
                data.Add(wire, name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryNamedItem))]
    public void EveryNamedItem_LoadsAndPlans(string wire, string name)
    {
        AssertLoadsAndPlans(ItemDoc(wire, name), $"{wire}:{name}");
    }

    public static TheoryData<string> EveryDeck() => Names<MotelyDeck>();

    public static TheoryData<string> EveryStake() => Names<MotelyStake>();

    [Theory]
    [MemberData(nameof(EveryDeck))]
    public void EveryDeck_LoadsAndPlans(string deck)
    {
        AssertLoadsAndPlans(
            $$"""
            name: golden-deck-{{deck}}
            author: golden-corpus
            deck: {{deck}}
            stake: White
            must:
              - joker: Joker
                antes: [1]
            """,
            $"deck:{deck}"
        );
    }

    [Theory]
    [MemberData(nameof(EveryStake))]
    public void EveryStake_LoadsAndPlans(string stake)
    {
        AssertLoadsAndPlans(
            $$"""
            name: golden-stake-{{stake}}
            author: golden-corpus
            deck: Red
            stake: {{stake}}
            must:
              - joker: Joker
                antes: [1]
            """,
            $"stake:{stake}"
        );
    }

    public static TheoryData<string, string> EveryPlayingCard()
    {
        var data = new TheoryData<string, string>();
        foreach (var card in Enum.GetValues<MotelyStandardCard>())
            data.Add(card.GetRank().ToString(), card.GetSuit().ToString());
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPlayingCard))]
    public void EveryPlayingCard_LoadsAndPlans(string rank, string suit)
    {
        AssertLoadsAndPlans(
            $$"""
            name: golden-card-{{rank}}-{{suit}}
            author: golden-corpus
            deck: Red
            stake: White
            must:
              - standardCard:
                  rank: {{rank}}
                  suit: {{suit}}
                  antes: [1]
            """,
            $"standardCard:{rank}Of{suit}"
        );
    }

    public static TheoryData<string> EveryEdition() => NamesExceptNone<MotelyItemEdition>();

    public static TheoryData<string> EverySeal() => NamesExceptNone<MotelyItemSeal>();

    public static TheoryData<string> EveryEnhancement() => NamesExceptNone<MotelyItemEnhancement>();

    [Theory]
    [MemberData(nameof(EveryEdition))]
    public void EveryEdition_LoadsAndPlans(string edition)
    {
        AssertLoadsAndPlans(
            $$"""
            name: golden-edition-{{edition}}
            author: golden-corpus
            deck: Red
            stake: White
            must:
              - joker: Joker
                edition: {{edition}}
                antes: [1]
            """,
            $"edition:{edition}"
        );
    }

    [Theory]
    [MemberData(nameof(EverySeal))]
    public void EverySeal_LoadsAndPlans(string seal)
    {
        AssertLoadsAndPlans(
            $$"""
            name: golden-seal-{{seal}}
            author: golden-corpus
            deck: Red
            stake: White
            must:
              - standardCard:
                  seal: {{seal}}
                  antes: [1]
            """,
            $"seal:{seal}"
        );
    }

    [Theory]
    [MemberData(nameof(EveryEnhancement))]
    public void EveryEnhancement_LoadsAndPlans(string enhancement)
    {
        AssertLoadsAndPlans(
            $$"""
            name: golden-enhancement-{{enhancement}}
            author: golden-corpus
            deck: Red
            stake: White
            must:
              - standardCard:
                  enhancement: {{enhancement}}
                  antes: [1]
            """,
            $"enhancement:{enhancement}"
        );
    }

    [Fact]
    public void NamedItemCounts_MatchVanillaPools()
    {
        Assert.Equal(150, Enum.GetNames<MotelyJoker>().Length);
        Assert.Equal(61, Enum.GetNames<MotelyJokerCommon>().Length);
        Assert.Equal(64, Enum.GetNames<MotelyJokerUncommon>().Length);
        Assert.Equal(20, Enum.GetNames<MotelyJokerRare>().Length);
        Assert.Equal(5, Enum.GetNames<MotelyJokerLegendary>().Length);
        Assert.Equal(22, Enum.GetNames<MotelyTarotCard>().Length);
        Assert.Equal(12, Enum.GetNames<MotelyPlanetCard>().Length);
        Assert.Equal(18, Enum.GetNames<MotelySpectralCard>().Length);
        Assert.Equal(15, Enum.GetNames<MotelyDeck>().Length);
        Assert.Equal(8, Enum.GetNames<MotelyStake>().Length);
        Assert.Equal(32, Enum.GetNames<MotelyVoucher>().Length);
        Assert.Equal(24, Enum.GetNames<MotelyTag>().Length);
        Assert.Equal(28, Enum.GetNames<MotelyBossBlind>().Length);
        Assert.Equal(
            Enum.GetNames<MotelyStandardcardRank>().Length
                * Enum.GetNames<MotelyStandardcardSuit>().Length,
            Enum.GetNames<MotelyStandardCard>().Length
        );
        Assert.Equal(
            Enum.GetNames<MotelyJokerCommon>().Length
                + Enum.GetNames<MotelyJokerUncommon>().Length
                + Enum.GetNames<MotelyJokerRare>().Length
                + Enum.GetNames<MotelyJokerLegendary>().Length,
            Enum.GetNames<MotelyJoker>().Length
        );
        Assert.Equal(15, Enum.GetNames<MotelyBoosterPack>().Length);
        Assert.Equal(9, Enum.GetNames<MotelyPokerHand>().Length);
    }

    [Fact]
    public void ItemWires_ComeFromSchemaValueEnums()
    {
        var wires = ValueEnumWires().Select(w => w.Wire).ToArray();
        Assert.Contains("joker", wires);
        Assert.Contains("boosterPack", wires);
        Assert.Contains("pokerHand", wires);
        Assert.Contains("legendaryJoker", wires);
        Assert.DoesNotContain("jokers", wires);
        Assert.DoesNotContain("smallBlindTag", wires);
        Assert.NotEmpty(wires);
    }

    private static string ItemDoc(string wire, string name)
    {
        var deck = wire.StartsWith("erratic", StringComparison.OrdinalIgnoreCase)
            ? "Erratic"
            : "Red";
        var sources = wire.Equals("legendaryJoker", StringComparison.OrdinalIgnoreCase)
            ? "\n    sources:\n      arcanaPacks: [0, 1]\n      spectralPacks: [0, 1]"
            : "";
        return $$"""
            name: golden-{{wire}}-{{name}}
            author: golden-corpus
            deck: {{deck}}
            stake: White
            must:
              - {{wire}}: {{name}}
                antes: [1]{{sources}}
            """;
    }

    private static IEnumerable<(string Wire, Type EnumType)> ValueEnumWires()
    {
        foreach (var wire in JamlSchema.Discriminators)
        {
            if (IsAliasWire(wire))
                continue;
            var enumType = JamlSchema.ValueEnumTypeFor(wire);
            if (enumType is not { IsEnum: true })
                continue;
            yield return (wire, enumType);
        }
    }

    /// <summary>
    /// Plurals and blind-specific tag wires share a value enum with the canonical singular.
    /// </summary>
    private static bool IsAliasWire(string wire)
    {
        if (wire is "smallBlindTag" or "bigBlindTag")
            return true;
        if (wire.Length > 1 && wire.EndsWith('s'))
        {
            var singular = wire[..^1];
            if (JamlSchema.ValueEnumTypeFor(singular) is not null)
                return true;
        }
        return false;
    }

    private static TheoryData<string> Names<TEnum>()
        where TEnum : struct, Enum
    {
        var data = new TheoryData<string>();
        foreach (var name in Enum.GetNames<TEnum>())
            data.Add(name);
        return data;
    }

    private static TheoryData<string> NamesExceptNone<TEnum>()
        where TEnum : struct, Enum
    {
        var data = new TheoryData<string>();
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (name == "None")
                continue;
            data.Add(name);
        }
        return data;
    }

    private static void AssertLoadsAndPlans(string jaml, string label)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(jaml, out var config, out var error),
            $"{label}: {error}\n{jaml}"
        );
        Assert.NotNull(config);
        Assert.True(config.HasAnyClauses(), $"{label}: loaded with no clauses");
        _ = JamlSearchBuilder.CreatePlan(config);
    }
}
