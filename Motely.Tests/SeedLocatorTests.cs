using Motely.MCP.Tools;

namespace Motely.Tests;

/// <summary>
/// "Where is X in this seed". Every expected value was read off the engine and cross-checked
/// against the analyzer's own output; the command is in the comment above each pin.
/// Seed 9KOCZ9YO on Ghost White is the one the 2026-10-24 MCP dogfood played.
/// </summary>
public sealed class SeedLocatorTests
{
    private const string Seed = "9KOCZ9YO";

    private static MotelyLocateResult Locate(string item, int minAnte = 1, int maxAnte = 12) =>
        MotelySeedLocator.Locate(Seed, MotelyDeck.Ghost, MotelyStake.White, item, minAnte, maxAnte);

    private static string[] Lines(MotelyLocateResult result) =>
        [.. result.Hits.Select(h => $"{h.Ante} {h.Where} {h.Item}")];

    [Fact]
    public void Ankh_in_the_shop_and_a_pack_matches_the_analyzer()
    {
        // `motely --analyze 9KOCZ9YO --deck Ghost`: ante 1 shop "2) Ankh", ante 2 "5) Ankh",
        // ante 7 "Mega Spectral Pack - Cryptid, Ouija, Ankh, Medium" (its first pack), ante 8 "11) Ankh".
        var result = Locate("Ankh", 1, 8);
        var seen = result.Hits.Where(h => h.Source is MotelyLocateSource.Shop or MotelyLocateSource.Pack);
        Assert.Equal(
            ["1 shop card #2 Ankh", "2 shop card #5 Ankh", "7 Mega Spectral Pack (pack 1) card 3 Ankh", "8 shop card #11 Ankh"],
            seen.Select(h => $"{h.Ante} {h.Where} {h.Item}")
        );
        var ante8 = Assert.Single(seen, h => h.Ante == 8);
        Assert.Equal(11, ante8.Position);

        // The same card the legacy analyzer lists as the 11th of ante 8's shop.
        var analysis = MotelyUnitTestAnalyzer.Analyze(new(Seed, MotelyDeck.Ghost, MotelyStake.White));
        Assert.Equal(MotelyItemType.Ankh, analysis.Antes.Single(a => a.Ante == 8).ShopQueue[10].Type);
    }

    [Fact]
    public void Diet_cola_is_an_ante_4_pack_card_and_ante_10_uncommon_tag_pull()
    {
        // `motely --analyze 9KOCZ9YO --deck Ghost --locate "Diet Cola" --antes 1-12`.
        // The dogfood said "only ante 10's Uncommon Tag pulls"; on Ghost it is also the first card of
        // ante 4's Buffoon Pack (`motely --analyze 9KOCZ9YO --deck Ghost`: "Buffoon Pack - Diet Cola, Mad Joker").
        Assert.Equal(
            [
                "4 Buffoon Pack (pack 1) card 1 Diet Cola",
                "6 Uncommon Tag pull #9 Diet Cola",
                "7 Uncommon Tag pull #18 Diet Cola",
                "10 Uncommon Tag pull #2 Diet Cola",
                "11 Uncommon Tag pull #11 Diet Cola",
                "11 Uncommon Tag pull #19 Diet Cola",
                "12 Uncommon Tag pull #9 Diet Cola",
                "12 Uncommon Tag pull #17 Diet Cola",
            ],
            Lines(Locate("diet cola"))
        );

        // Every pull hit is that index of the Jamlyzer's own Uncommon Tag stream (a clause on
        // antes 1-12 scopes its walk to 1-12).
        var config = JamlConfigLoader.FromJaml(
            "deck: Ghost\nstake: White\nseeds: [9KOCZ9YO]\nshould:\n  - Diet Cola in antes 1-12\n"
        );
        var antes = MotelyJamlyzer.Analyze(config).Single().Antes;
        Assert.Equal(MotelyItemType.DietCola, antes.Single(a => a.Ante == 10).Pulls.UncommonTagJokers[1].Type);
        foreach (var hit in Locate("Diet Cola").Hits.Where(h => h.Source == MotelyLocateSource.UncommonTag))
            Assert.Equal(
                MotelyItemType.DietCola,
                antes.Single(a => a.Ante == hit.Ante).Pulls.UncommonTagJokers[hit.Position - 1].Type
            );
    }

    [Fact]
    public void Legendaries_come_from_the_souls_in_run_order()
    {
        // `--locate Perkeo|Chicot|Canio --antes 1-12`; the filter
        // `Perkeo in ante 1`, `Chicot in antes 1-2`, `Canio in antes 1-12` scores 1 each on
        // `--jaml <it> --seeds 9KOCZ9YO` (Ghost), and Triboulet in antes 1-12 scores 0.
        Assert.Equal(["1 Soul #1 in run order (Arcana Pack, pack 4) Perkeo"], Lines(Locate("Perkeo")));
        Assert.Equal(["2 Soul #2 in run order (Arcana Pack, pack 1) Chicot"], Lines(Locate("chicot")));
        var canio = Assert.Single(Locate("Canio").Hits);
        Assert.Equal((12, MotelyLocateSource.Soul, 3), (canio.Ante, canio.Source, canio.Position));
        Assert.Empty(Locate("Triboulet").Hits);
    }

    [Fact]
    public void A_later_first_ante_keeps_the_run_order()
    {
        // The walk still opens ante 1's and 2's Souls, so ante 12's is still the third.
        var canio = Assert.Single(Locate("Canio", 10, 12).Hits);
        Assert.Equal(3, canio.Position);
        Assert.Empty(Locate("Perkeo", 2, 12).Hits);
    }

    [Fact]
    public void Tags_bosses_and_vouchers()
    {
        // `--locate "negative tag" --antes 1-12`: ante 11's big blind. The filter line
        // `Negative Tag in ante 11` scores 1 on 9KOCZ9YO Ghost, `... in ante 10` scores 0.
        var tag = Assert.Single(Locate("negative tag").Hits);
        Assert.Equal((11, MotelyLocateSource.BigBlindTag, "Negative Tag"), (tag.Ante, tag.Source, tag.Item));

        // `motely --analyze 9KOCZ9YO --deck Ghost`: ante 1 "Boss: The Hook", "Tags: Economy Tag,
        // Uncommon Tag"; ante 5 "Tags: Buffoon Tag, Uncommon Tag"; ante 8 "Boss: Amber Acorn", "Voucher: Grabber".
        Assert.Equal(["1 Boss The Hook"], Lines(Locate("hook", 1, 8)));
        Assert.Equal(["8 Boss Amber Acorn"], Lines(Locate("Amber Acorn", 1, 8)));
        Assert.Equal(["8 Voucher Grabber"], Lines(Locate("grabber", 1, 8)));
        Assert.Equal(["1 Big Blind Tag Uncommon Tag", "5 Big Blind Tag Uncommon Tag"], Lines(Locate("Uncommon Tag", 1, 8)));
        Assert.Equal(["1 Small Blind Tag Economy Tag", "6 Big Blind Tag Economy Tag"], Lines(Locate("Economy Tag", 1, 8)));
    }

    [Fact]
    public void An_edition_word_keeps_that_edition_only()
    {
        Assert.Empty(Locate("Negative Diet Cola").Hits);
        Assert.Equal("Negative Diet Cola", Locate("Negative Diet Cola").Name);
        Assert.All(Locate("Diet Cola").Hits, h => Assert.Equal(MotelyItemEdition.None, h.Edition));
    }

    [Fact]
    public void An_unknown_name_gets_the_shorthand_correction()
    {
        var ex = Assert.Throws<FormatException>(() => Locate("Diet Coke"));
        Assert.Equal("'Diet Coke' is not a known name; did you mean DietCola?", ex.Message);
        var edition = Assert.Throws<FormatException>(() => Locate("Negative Ankh"));
        Assert.Contains("only a joker takes an edition", edition.Message);
    }

    [Fact]
    public void Nothing_found_says_how_deep_it_looked()
    {
        // `--locate Yorick --antes 1-12` on 9KOCZ9YO Ghost: no hits.
        var result = Locate("Yorick");
        Assert.Empty(result.Hits);
        Assert.Equal("antes 1-12, 20 pull rolls and 15-50 shop cards per ante", result.Depth);
    }

    [Fact]
    public void Bad_antes_and_seeds_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Locate("Ankh", 3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Locate("Ankh", 1, MotelySeedLocator.MaxAnte + 1));
        Assert.Throws<ArgumentException>(
            () => MotelySeedLocator.Locate("BAD0SEED", MotelyDeck.Red, MotelyStake.White, "Ankh")
        );
    }

    [Fact]
    public void Mcp_seed_locate_reports_hits_and_refuses_stdin()
    {
        var result = MotelyTools.Locate(Seed, "Ankh", deck: "ghost", antes: [8]);
        Assert.Equal(("Ghost", "Ankh", "ante 8, 20 pull rolls and 50 shop cards per ante"), (result.Deck, result.Item, result.Searched));
        var shop = Assert.Single(result.Hits, h => h.Source == "Shop");
        Assert.Equal((8, 11, "shop card #11", "Ankh", "None"), (shop.Ante, shop.Position, shop.Where, shop.Item, shop.Edition));

        Assert.Throws<ArgumentException>(() => MotelyTools.Locate("-", "Ankh"));
        var bad = Assert.Throws<ArgumentException>(() => MotelyTools.Locate(Seed, "Diet Coke"));
        Assert.Contains("did you mean DietCola", bad.Message);
    }

    [Fact]
    public void Mcp_seed_locate_is_annotated_readonly_idempotent_closed()
    {
        var method = typeof(MotelyTools).GetMethod(nameof(MotelyTools.Locate))!;
        var attr = (ModelContextProtocol.Server.McpServerToolAttribute)
            Attribute.GetCustomAttribute(method, typeof(ModelContextProtocol.Server.McpServerToolAttribute))!;
        Assert.Equal("seed_locate", attr.Name);
        Assert.True(attr.ReadOnly);
        Assert.False(attr.Destructive);
        Assert.True(attr.Idempotent);
        Assert.False(attr.OpenWorld);
        Assert.True(attr.UseStructuredContent);
    }
}
