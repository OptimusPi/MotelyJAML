using Motely.Filters.Jaml;

namespace Motely.Tests;

public sealed class JamlConfigWriterTests
{
    [Fact]
    public void ToJaml_RoundTripsEveryTestJamlFile()
    {
        var dir = TestJamlDir();
        var files = Directory
            .GetFiles(dir, "*.jaml", SearchOption.AllDirectories)
            .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);
        AssertCorpusRoundTrips(dir, files);
    }

    // Every filter a user can pick from the repo root: load → write → reload → write must be a
    // fixed point, and the reload must carry every field the first load did.
    [Fact]
    public void ToJaml_IsAFixedPointOverTheRepoCorpus()
    {
        var dir = Path.Combine(RepoRoot(), "JamlFilters");
        var files = Directory
            .GetFiles(dir, "*.jaml", SearchOption.TopDirectoryOnly)
            .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);
        AssertCorpusRoundTrips(dir, files);
    }

    private static void AssertCorpusRoundTrips(string dir, string[] files)
    {
        var failures = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(dir, file);
            if (!JamlConfigLoader.TryLoad(File.ReadAllText(file), out var original, out var loadError))
            {
                failures.Add($"{relative}: load failed: {loadError}");
                continue;
            }

            string written;
            try
            {
                written = JamlConfigLoader.ToJaml(original);
            }
            catch (Exception ex)
            {
                failures.Add($"{relative}: ToJaml threw: {ex.Message}");
                continue;
            }

            if (!JamlConfigLoader.TryLoad(written, out var reloaded, out var error))
            {
                failures.Add($"{relative}: round-tripped JAML failed to reload: {error}\n---\n{written}");
                continue;
            }

            try
            {
                JamlConfigEquality.AssertEqual(original, reloaded);
            }
            catch (Xunit.Sdk.XunitException ex)
            {
                failures.Add($"{relative}: reload differs from load: {ex.Message}\n---\n{written}");
                continue;
            }

            var rewritten = JamlConfigLoader.ToJaml(reloaded);
            if (rewritten != written)
                failures.Add($"{relative}: second write differs from first.\n--- first ---\n{written}\n--- second ---\n{rewritten}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // Regression: an explicit `sources: {}` means "override with match-nowhere" and is distinct
    // from an absent sources: (use DefaultSources). ToJaml must not collapse the two by dropping
    // an all-default sources block.
    [Fact]
    public void ToJaml_PreservesExplicitEmptySources()
    {
        const string jaml = """
            id: empty-sources
            must:
              - joker: [Blueprint]
                sources: {}
            """;
        var original = JamlConfigLoader.FromJaml(jaml);
        var originalClause = Assert.IsType<JokerClause>(original.Must[0]);
        Assert.NotNull(originalClause.Sources);

        var reloaded = JamlConfigLoader.FromJaml(JamlConfigLoader.ToJaml(original));
        var reloadedClause = Assert.IsType<JokerClause>(reloaded.Must[0]);
        Assert.NotNull(reloadedClause.Sources);
    }

    // Regression: erraticRanks: [...] with an explicit min: must carry that min onto the wrapping
    // OrClause (how many of the listed ranks must appear), not silently reset to 1.
    [Fact]
    public void ErraticRanks_HonorsExplicitMin()
    {
        const string jaml = """
            id: erratic-min
            must:
              - erraticRanks: [Two, Three, Four]
                min: 3
            """;
        var config = JamlConfigLoader.FromJaml(jaml);
        var or = Assert.IsType<Motely.Filters.OrClause>(config.Must[0]);
        Assert.Equal(3, or.Min);
    }

    [Fact]
    public void PokerHand_RollsSurviveRoundTrip()
    {
        const string jaml = """
            must:
              - pokerHand: [Flush]
                antes: [1]
                rolls: [1, 2]
            """;
        var reloaded = RoundTrip(jaml, out var written);
        var hand = Assert.IsType<PokerHandClause>(Assert.Single(reloaded.Must));
        Assert.Equal([1, 2], hand.Rolls);
        Assert.Contains("rolls: [1, 2]", written);
    }

    [Theory]
    [InlineData("voucher", typeof(VoucherClause), "Overstock")]
    [InlineData("tag", typeof(TagClause), "NegativeTag")]
    [InlineData("boosterPack", typeof(BoosterPackClause), "MegaArcana")]
    [InlineData("pokerHand", typeof(PokerHandClause), "Flush")]
    public void DefaultRolls_ComeFromTheSchema_AndAreNotWritten(string wire, Type clauseType, string value)
    {
        var expected = JamlSchema.RollsDefaultFor(wire);
        Assert.NotNull(expected);

        var reloaded = RoundTrip($"must:\n  - {wire}: [{value}]\n", out var written);
        var clause = Assert.IsType<IRollScopedClause>(Assert.Single(reloaded.Must), exactMatch: false);
        Assert.IsType(clauseType, clause);
        Assert.Equal(expected, clause.Rolls);
        Assert.DoesNotContain("rolls", written);
    }

    [Theory]
    [InlineData("smallBlindTag")]
    [InlineData("bigBlindTag")]
    public void BlindTagWires_KeepTheirSpelling(string wire)
    {
        var reloaded = RoundTrip($"must:\n  - {wire}: NegativeTag\n    antes: [1, 2]\n", out var written);
        var tag = Assert.IsType<TagClause>(Assert.Single(reloaded.Must));

        Assert.Contains($"{wire}: [NegativeTag]", written);
        Assert.DoesNotContain("rolls", written);
        Assert.Equal(JamlSchema.RollsDefaultFor(wire), tag.Rolls);
        Assert.Equal([1, 2], tag.Antes);
    }

    [Fact]
    public void Tag_WithNonBlindRolls_WritesTheRollsOnTheGenericWire()
    {
        var reloaded = RoundTrip("must:\n  - tag: [CharmTag]\n    rolls: [0, 1, 2]\n", out var written);
        var tag = Assert.IsType<TagClause>(Assert.Single(reloaded.Must));

        Assert.Contains("tag: [CharmTag]", written);
        Assert.Contains("rolls: [0, 1, 2]", written);
        Assert.Equal([0, 1, 2], tag.Rolls);
    }

    [Fact]
    public void MultiLineDescription_WritesALiteralBlock()
    {
        const string jaml = """
            id: block
            description: |
              First line: has a colon.
              Second line has a # hash after a space.

              Fourth line after a blank one.
            must:
              - joker: [Blueprint]
            """;
        var original = JamlConfigLoader.FromJaml(jaml);
        Assert.Equal(
            "First line: has a colon.\nSecond line has a # hash after a space.\n\nFourth line after a blank one.",
            original.Description
        );

        var written = JamlConfigLoader.ToJaml(original);
        Assert.Contains("description: |\n", written);
        Assert.True(JamlConfigLoader.TryLoad(written, out var reloaded, out var error), $"{error}\n---\n{written}");
        Assert.Equal(original.Description, reloaded.Description);
        Assert.Equal(written, JamlConfigLoader.ToJaml(reloaded));
    }

    [Fact]
    public void Label_WithAHashAfterWhitespace_SurvivesRoundTrip()
    {
        const string jaml = """
            must:
              - joker: [Blueprint]
                label: |
                  Waterbear #0027
            """;
        var reloaded = RoundTrip(jaml, out _);
        Assert.Equal("Waterbear #0027", Assert.Single(reloaded.Must).Label);
    }

    [Theory]
    [InlineData("""'say "hi"'""", "say \"hi\"")]
    [InlineData("""'note: "quoted"'""", "note: \"quoted\"")]
    [InlineData("\"it's\"", "it's")]
    [InlineData("\"{}\"", "{}")]
    [InlineData("\"[not, a, list]\"", "[not, a, list]")]
    [InlineData("\"  padded  \"", "  padded  ")]
    [InlineData("\"key: value\"", "key: value")]
    [InlineData("\"- dash\"", "- dash")]
    [InlineData("\"|\"", "|")]
    [InlineData("\"\"", "")]
    [InlineData("plain words", "plain words")]
    [InlineData("3:1 odds", "3:1 odds")]
    public void Label_TextSurvivesRoundTrip(string authored, string expected)
    {
        var original = JamlConfigLoader.FromJaml($"must:\n  - joker: [Blueprint]\n    label: {authored}\n");
        Assert.Equal(expected, Assert.Single(original.Must).Label);

        var written = JamlConfigLoader.ToJaml(original);
        Assert.True(JamlConfigLoader.TryLoad(written, out var reloaded, out var error), $"{error}\n---\n{written}");
        Assert.Equal(expected, Assert.Single(reloaded.Must).Label);
        Assert.Equal(written, JamlConfigLoader.ToJaml(reloaded));
    }

    [Fact]
    public void RootText_WithQuotesAndColons_SurvivesRoundTrip()
    {
        const string jaml = """
            id: "odd: id"
            name: 'He said "go"'
            author: "for pi — the luckiest seeds"
            must:
              - joker: [Blueprint]
            """;
        var reloaded = RoundTrip(jaml, out _);
        Assert.Equal("odd: id", reloaded.Id);
        Assert.Equal("He said \"go\"", reloaded.Name);
        Assert.Equal("for pi — the luckiest seeds", reloaded.Author);
    }

    [Fact]
    public void HoistedAntes_AreWrittenOnceOnTheParent()
    {
        const string jaml = """
            must:
              - or:
                  - joker: [Blueprint]
                  - and:
                      - voucher: [Overstock]
                      - tag: [CharmTag]
                        antes: [3]
                antes: [1, 2]
            """;
        var original = JamlConfigLoader.FromJaml(jaml);
        var or = Assert.IsType<Motely.Filters.OrClause>(Assert.Single(original.Must));
        Assert.Equal([1, 2], ((JokerClause)or.Clauses[0]).Antes);

        var written = JamlConfigLoader.ToJaml(original);
        Assert.Equal(1, CountOf(written, "antes: [1, 2]"));
        Assert.Equal(1, CountOf(written, "antes: [3]"));

        Assert.True(JamlConfigLoader.TryLoad(written, out var reloaded, out var error), $"{error}\n---\n{written}");
        JamlConfigEquality.AssertEqual(original, reloaded);
        Assert.Equal(written, JamlConfigLoader.ToJaml(reloaded));
    }

    [Fact]
    public void ChildAntes_DifferentFromTheParent_AreStillWritten()
    {
        const string jaml = """
            must:
              - and:
                  - joker: [Blueprint]
                    antes: [4]
                antes: [1]
            """;
        var reloaded = RoundTrip(jaml, out var written);
        var and = Assert.IsType<Motely.Filters.AndClause>(Assert.Single(reloaded.Must));
        Assert.Equal([1], and.Antes);
        Assert.Equal([4], ((JokerClause)and.Clauses[0]).Antes);
        Assert.Contains("antes: [4]", written);
    }

    [Fact]
    public void EveryClauseFamily_KeepsItsFieldsThroughRoundTrip()
    {
        const string jaml = """
            id: families
            name: Families
            deck: Erratic
            stake: Gold
            seeds: [ABCDEFGH, IJKLMNOP]
            must:
              - joker: [Blueprint, Brainstorm]
                antes: [1, 2]
                min: 2
                max: 3
                score: 7
                label: copies
                edition: Negative
                stickers: [Eternal]
                sources:
                  shopItems: [0, 1, 2]
                  boosterPacks: [0]
                  judgement: [0]
                  riffRaff: [1]
                  rareTag: [0]
                  requireMegaPack: true
              - rareJoker: [Blueprint]
                sources:
                  rareShopJokers: [0, 1]
              - legendaryJoker: [Perkeo]
                antes: [1]
                edition: Polychrome
                soulCardOnly: true
                soulEditionRolls: 3
                sources:
                  arcanaPacks: [0, 1]
              - tarotCard: [TheFool]
                sources:
                  shopItems: [0]
                  emperor: [0, 1]
                  purpleSealOrEightBall: [0]
                  charmTag: true
              - spectralCard: [Ankh]
                sources:
                  boosterPacks: [0, 1]
                  sixthSense: [0]
                  seance: [1]
                  etherealTag: true
                  omenGlobe: true
              - planetCard: [Pluto]
                sources:
                  shopItems: [0, 1]
                  requireMegaPack: true
              - standardCard:
                rank: Ace
                suit: Hearts
                enhancement: Glass
                seal: Red
                edition: Polychrome
                sources:
                  shopItems: [0, 1]
                  boosterPacks: [0]
              - voucher: [Overstock]
                rolls: [0, 1]
              - boss: [TheEye]
              - boosterPack: [MegaArcana]
                rolls: [0]
              - startingDraw:
                rank: King
                suit: Spades
              - erraticRank: Ace
              - erraticSuit: Hearts
              - luckyMoney: [0, 1, 2]
                min: 2
                with:
                  luck: X4
              - misprintMult: [0]
                mult: 23
            should:
              - or:
                  - joker: [Showman]
                  - tag: [NegativeTag]
                mode: max
                score: 50
                label: either
            mustNot:
              - boss: [TheWall]
                antes: [1]
            """;
        var original = JamlConfigLoader.FromJaml(jaml);
        var written = JamlConfigLoader.ToJaml(original);
        Assert.True(JamlConfigLoader.TryLoad(written, out var reloaded, out var error), $"{error}\n---\n{written}");
        JamlConfigEquality.AssertEqual(original, reloaded);
        Assert.Equal(written, JamlConfigLoader.ToJaml(reloaded));
    }

    private static JamlConfig RoundTrip(string jaml, out string written)
    {
        var original = JamlConfigLoader.FromJaml(jaml);
        written = JamlConfigLoader.ToJaml(original);
        Assert.True(JamlConfigLoader.TryLoad(written, out var reloaded, out var error), $"{error}\n---\n{written}");
        JamlConfigEquality.AssertEqual(original, reloaded);
        return reloaded;
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string TestJamlDir()
    {
        var dir = Path.Join(AppContext.BaseDirectory, "JamlFilters");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Test JAML files not in output: {dir}");
        return dir;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Motely.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Motely.slnx not found above the test output.");
    }
}

/// <summary>
/// Field-by-field equality of two loaded configs — what "round-trips" has to mean for the writer.
/// One arm per clause family, mirroring the writer's own switch, so a new field with no writer
/// path fails here instead of hiding behind a clause count.
/// </summary>
internal static class JamlConfigEquality
{
    public static void AssertEqual(JamlConfig expected, JamlConfig actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Author, actual.Author);
        Assert.Equal(expected.Deck, actual.Deck);
        Assert.Equal(expected.Stake, actual.Stake);
        Assert.Equal(expected.Seeds, actual.Seeds);
        Assert.Equal(expected.Filter, actual.Filter);
        AssertEqual(expected.Must, actual.Must, "must");
        AssertEqual(expected.Should, actual.Should, "should");
        AssertEqual(expected.MustNot, actual.MustNot, "mustNot");
    }

    private static void AssertEqual(IReadOnlyList<IJamlClause> expected, IReadOnlyList<IJamlClause> actual, string path)
    {
        Assert.True(expected.Count == actual.Count, $"{path}: {expected.Count} clauses became {actual.Count}");
        for (int i = 0; i < expected.Count; i++)
            AssertEqual(expected[i], actual[i], $"{path}[{i}]");
    }

    private static void AssertEqual(IJamlClause expected, IJamlClause actual, string path)
    {
        Assert.True(expected.GetType() == actual.GetType(), $"{path}: {expected.GetType().Name} became {actual.GetType().Name}");
        Field(path, "label", expected.Label, actual.Label);
        Field(path, "min", expected.Min, actual.Min);
        Field(path, "max", expected.Max, actual.Max);
        Field(path, "score", expected.Score, actual.Score);
        if (expected is IAnteScopedClause ea)
            Field(path, "antes", ea.Antes, ((IAnteScopedClause)actual).Antes);
        if (expected is IRollScopedClause er)
            Field(path, "rolls", er.Rolls, ((IRollScopedClause)actual).Rolls);
        if (expected is IWithScopedClause ew)
        {
            var aw = ((IWithScopedClause)actual).With;
            Field(path, "with.luck", ew.With.Luck, aw.Luck);
        }

        switch (expected)
        {
            case Motely.Filters.LogicClause logic:
                var actualLogic = (Motely.Filters.LogicClause)actual;
                Field(path, "mode", logic.Mode, actualLogic.Mode);
                AssertEqual(logic.Clauses, actualLogic.Clauses, path);
                break;
            case JokerClause c:
                var a = (JokerClause)actual;
                Field(path, "jokers", c.Jokers, a.Jokers);
                Field(path, "edition", c.Edition, a.Edition);
                Field(path, "stickers", c.Stickers, a.Stickers);
                AssertEqual(c.Sources, a.Sources, path);
                break;
            case CommonJokerClause c:
                var ac = (CommonJokerClause)actual;
                Field(path, "jokers", c.Jokers, ac.Jokers);
                Field(path, "edition", c.Edition, ac.Edition);
                Field(path, "stickers", c.Stickers, ac.Stickers);
                AssertEqual(c.Sources, ac.Sources, path);
                break;
            case UncommonJokerClause c:
                var au = (UncommonJokerClause)actual;
                Field(path, "jokers", c.Jokers, au.Jokers);
                Field(path, "edition", c.Edition, au.Edition);
                Field(path, "stickers", c.Stickers, au.Stickers);
                AssertEqual(c.Sources, au.Sources, path);
                break;
            case RareJokerClause c:
                var ar = (RareJokerClause)actual;
                Field(path, "jokers", c.Jokers, ar.Jokers);
                Field(path, "edition", c.Edition, ar.Edition);
                Field(path, "stickers", c.Stickers, ar.Stickers);
                AssertEqual(c.Sources, ar.Sources, path);
                break;
            case LegendaryJokerClause c:
                var al = (LegendaryJokerClause)actual;
                Field(path, "jokers", c.Jokers, al.Jokers);
                Field(path, "edition", c.Edition, al.Edition);
                Field(path, "soulCardOnly", c.SoulCardOnly, al.SoulCardOnly);
                Field(path, "soulEditionRolls", c.SoulEditionRolls, al.SoulEditionRolls);
                AssertEqual(c.Sources, al.Sources, path);
                break;
            case TarotCardClause c:
                var at = (TarotCardClause)actual;
                Field(path, "tarots", c.Tarots, at.Tarots);
                AssertEqual(c.Sources, at.Sources, path);
                break;
            case SpectralCardClause c:
                var asp = (SpectralCardClause)actual;
                Field(path, "spectrals", c.Spectrals, asp.Spectrals);
                AssertEqual(c.Sources, asp.Sources, path);
                break;
            case PlanetCardClause c:
                var ap = (PlanetCardClause)actual;
                Field(path, "planets", c.Planets, ap.Planets);
                AssertEqual(c.Sources, ap.Sources, path);
                break;
            case StandardCardClause c:
                var asc = (StandardCardClause)actual;
                Field(path, "rank", c.Rank, asc.Rank);
                Field(path, "suit", c.Suit, asc.Suit);
                Field(path, "enhancement", c.Enhancement, asc.Enhancement);
                Field(path, "seal", c.Seal, asc.Seal);
                Field(path, "edition", c.Edition, asc.Edition);
                AssertEqual(c.Sources, asc.Sources, path);
                break;
            case VoucherClause c:
                Field(path, "vouchers", c.Vouchers, ((VoucherClause)actual).Vouchers);
                break;
            case TagClause c:
                Field(path, "tags", c.Tags, ((TagClause)actual).Tags);
                break;
            case BossClause c:
                Field(path, "bosses", c.Bosses, ((BossClause)actual).Bosses);
                break;
            case BoosterPackClause c:
                Field(path, "packs", c.Packs, ((BoosterPackClause)actual).Packs);
                break;
            case PokerHandClause c:
                Field(path, "pokerHands", c.PokerHands, ((PokerHandClause)actual).PokerHands);
                break;
            case StartingDrawClause c:
                var asd = (StartingDrawClause)actual;
                Field(path, "rank", c.Rank, asd.Rank);
                Field(path, "suit", c.Suit, asd.Suit);
                break;
            case ErraticRankClause c:
                Field(path, "rank", c.Rank, ((ErraticRankClause)actual).Rank);
                break;
            case ErraticSuitClause c:
                Field(path, "suit", c.Suit, ((ErraticSuitClause)actual).Suit);
                break;
            case MisprintMultClause c:
                Field(path, "mult", c.Mult, ((MisprintMultClause)actual).Mult);
                break;
            case IRollScopedClause:
                break;
            default:
                Assert.Fail($"{path}: no equality arm for {expected.GetType().Name}");
                break;
        }
    }

    private static void AssertEqual(JokerSourceConfig? e, JokerSourceConfig? a, string path)
    {
        if (SourcesPresence(e, a, path) is not true)
            return;
        Field(path, "sources.shopItems", e!.ShopItems, a!.ShopItems);
        Field(path, "sources.boosterPacks", e.BoosterPacks, a.BoosterPacks);
        Field(path, "sources.judgement", e.Judgement, a.Judgement);
        Field(path, "sources.wraith", e.Wraith, a.Wraith);
        Field(path, "sources.riffRaff", e.RiffRaff, a.RiffRaff);
        Field(path, "sources.rareTag", e.RareTag, a.RareTag);
        Field(path, "sources.uncommonTag", e.UncommonTag, a.UncommonTag);
        Field(path, "sources.commonShopJokers", e.CommonShopJokers, a.CommonShopJokers);
        Field(path, "sources.uncommonShopJokers", e.UncommonShopJokers, a.UncommonShopJokers);
        Field(path, "sources.rareShopJokers", e.RareShopJokers, a.RareShopJokers);
        Field(path, "sources.allShopJokers", e.AllShopJokers, a.AllShopJokers);
        Field(path, "sources.requireMegaPack", e.RequireMegaPack, a.RequireMegaPack);
    }

    private static void AssertEqual(LegendaryJokerSourceConfig? e, LegendaryJokerSourceConfig? a, string path)
    {
        if (SourcesPresence(e, a, path) is not true)
            return;
        Field(path, "sources.boosterPacks", e!.BoosterPacks, a!.BoosterPacks);
        Field(path, "sources.arcanaPacks", e.ArcanaPacks, a.ArcanaPacks);
        Field(path, "sources.spectralPacks", e.SpectralPacks, a.SpectralPacks);
        Field(path, "sources.requireMegaPack", e.RequireMegaPack, a.RequireMegaPack);
    }

    private static void AssertEqual(TarotCardSourceConfig? e, TarotCardSourceConfig? a, string path)
    {
        if (SourcesPresence(e, a, path) is not true)
            return;
        Field(path, "sources.shopItems", e!.ShopItems, a!.ShopItems);
        Field(path, "sources.boosterPacks", e.BoosterPacks, a.BoosterPacks);
        Field(path, "sources.emperor", e.Emperor, a.Emperor);
        Field(path, "sources.purpleSealOrEightBall", e.PurpleSealOrEightBall, a.PurpleSealOrEightBall);
        Field(path, "sources.charmTag", e.CharmTag, a.CharmTag);
        Field(path, "sources.requireMegaPack", e.RequireMegaPack, a.RequireMegaPack);
    }

    private static void AssertEqual(SpectralCardSourceConfig? e, SpectralCardSourceConfig? a, string path)
    {
        if (SourcesPresence(e, a, path) is not true)
            return;
        Field(path, "sources.shopItems", e!.ShopItems, a!.ShopItems);
        Field(path, "sources.boosterPacks", e.BoosterPacks, a.BoosterPacks);
        Field(path, "sources.sixthSense", e.SixthSense, a.SixthSense);
        Field(path, "sources.seance", e.Seance, a.Seance);
        Field(path, "sources.etherealTag", e.EtherealTag, a.EtherealTag);
        Field(path, "sources.requireMegaPack", e.RequireMegaPack, a.RequireMegaPack);
        Field(path, "sources.omenGlobe", e.OmenGlobe, a.OmenGlobe);
    }

    private static void AssertEqual(PlanetSourceConfig? e, PlanetSourceConfig? a, string path)
    {
        if (SourcesPresence(e, a, path) is not true)
            return;
        Field(path, "sources.shopItems", e!.ShopItems, a!.ShopItems);
        Field(path, "sources.boosterPacks", e.BoosterPacks, a.BoosterPacks);
        Field(path, "sources.requireMegaPack", e.RequireMegaPack, a.RequireMegaPack);
    }

    private static void AssertEqual(StandardCardSourceConfig? e, StandardCardSourceConfig? a, string path)
    {
        if (SourcesPresence(e, a, path) is not true)
            return;
        Field(path, "sources.shopItems", e!.ShopItems, a!.ShopItems);
        Field(path, "sources.boosterPacks", e.BoosterPacks, a.BoosterPacks);
        Field(path, "sources.requireMegaPack", e.RequireMegaPack, a.RequireMegaPack);
    }

    // null (engine defaults) and an explicit block are different meanings; both sides must agree
    // on which one it is before the fields are worth comparing.
    private static bool SourcesPresence(object? e, object? a, string path)
    {
        Assert.True((e is null) == (a is null), $"{path}: sources {(e is null ? "absent" : "present")} became {(a is null ? "absent" : "present")}");
        return e is not null;
    }

    private static void Field<T>(string path, string name, T expected, T actual)
    {
        if (expected is System.Collections.IEnumerable ee && actual is System.Collections.IEnumerable ae && expected is not string)
        {
            var el = ee.Cast<object>().ToArray();
            var al = ae.Cast<object>().ToArray();
            Assert.True(el.SequenceEqual(al), $"{path}.{name}: [{string.Join(", ", el)}] became [{string.Join(", ", al)}]");
            return;
        }
        Assert.True(Equals(expected, actual), $"{path}.{name}: '{expected}' became '{actual}'");
    }
}
