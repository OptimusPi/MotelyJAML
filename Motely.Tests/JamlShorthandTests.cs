using System.Text.Json;
using System.Text.Json.Serialization;
using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// A clause written as one line (<c>- Perkeo in ante 1 or 2</c>) loads to exactly the clause its
/// mapping twin does: same type, same values, same antes, every other key at its default. Proven
/// field by field, and by searching one fixed seed list with both forms of a filter.
/// </summary>
public sealed class JamlShorthandTests
{
    private static readonly JsonSerializerOptions FieldOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static IMotelyClause OnlyMust(string yaml) => Assert.Single(ProofSearch.LoadOrThrow(yaml).Must);

    private static string Fields(IMotelyClause clause) => JsonSerializer.Serialize(clause, clause.GetType(), FieldOptions);

    [Theory]
    [InlineData("Perkeo in ante 1 or 2", "legendaryJoker: Perkeo\n    antes: [1, 2]")]
    [InlineData("The Eye in ante 3", "boss: TheEye\n    antes: [3]")]
    [InlineData("the eye in ante 3", "boss: TheEye\n    antes: [3]")]
    [InlineData("TheEye in ante 3", "boss: TheEye\n    antes: [3]")]
    [InlineData("Negative Blueprint in antes 1-3", "joker: Blueprint\n    edition: Negative\n    antes: [1, 2, 3]")]
    [InlineData("Polychrome Perkeo in ante 2", "legendaryJoker: Perkeo\n    edition: Polychrome\n    antes: [2]")]
    [InlineData("Blueprint in ante 1, 2 and 3", "joker: Blueprint\n    antes: [1, 2, 3]")]
    [InlineData("Mail In Rebate in antes 1, 2 or 4", "joker: MailInRebate\n    antes: [1, 2, 4]")]
    [InlineData("Charm Tag in ante 1", "tag: CharmTag\n    antes: [1]")]
    [InlineData("Negative Tag in ante 1", "tag: NegativeTag\n    antes: [1]")]
    [InlineData("black hole in ante 2, 3 and 4", "spectralCard: BlackHole\n    antes: [2, 3, 4]")]
    [InlineData("Black Hole in ante 2-3, 5 and 7-8", "spectralCard: BlackHole\n    antes: [2, 3, 5, 7, 8]")]
    [InlineData("The Fool in ante 1", "tarotCard: TheFool\n    antes: [1]")]
    [InlineData("Jupiter in antes 1-2", "planetCard: Jupiter\n    antes: [1, 2]")]
    [InlineData("Telescope in ante 3 or 1 or 3", "voucher: Telescope\n    antes: [1, 3]")]
    [InlineData("Hieroglyph in ante 0", "voucher: Hieroglyph\n    antes: [0]")]
    public void Shorthand_LoadsTheSameClauseAsItsMappingTwin(string shorthand, string mapping)
    {
        var fromShorthand = OnlyMust($"must:\n  - {shorthand}\n");
        var fromMapping = OnlyMust($"must:\n  - {mapping}\n");

        Assert.Equal(fromMapping.GetType(), fromShorthand.GetType());
        Assert.Equal(Fields(fromMapping), Fields(fromShorthand));
        Assert.Equal(1, fromShorthand.Score);
    }

    [Fact]
    public void QuotedShorthand_LoadsLikePlain()
    {
        Assert.Equal(
            Fields(OnlyMust("must:\n  - Perkeo in ante 1 or 2\n")),
            Fields(OnlyMust("must:\n  - \"Perkeo in ante 1 or 2\"\n")));
    }

    [Fact]
    public void ANonLegendaryJoker_IsAJokerClause_ALegendary_ALegendaryJokerClause()
    {
        Assert.IsType<JokerClause>(OnlyMust("must:\n  - Blueprint in ante 1\n"));
        Assert.IsType<LegendaryJokerClause>(OnlyMust("must:\n  - Perkeo in ante 1\n"));
    }

    private const string ShorthandFilter = """
        name: shorthand
        must:
          - Perkeo in ante 1 or 2
        should:
          - The Eye in ante 3
          - Negative Blueprint in antes 1-3
          - Blueprint in antes 1-4
          - Charm Tag in ante 1
          - Black Hole in ante 2, 3 and 4
          - Telescope in antes 1 or 2
          - The Fool in ante 1
          - Jupiter in antes 1-2
        mustNot:
          - The Wall in ante 2
        """;

    private const string MappingFilter = """
        name: mapping
        must:
          - legendaryJoker: Perkeo
            antes: [1, 2]
        should:
          - boss: TheEye
            antes: [3]
          - joker: Blueprint
            edition: Negative
            antes: [1, 2, 3]
          - joker: Blueprint
            antes: [1, 2, 3, 4]
          - tag: CharmTag
            antes: [1]
          - spectralCard: BlackHole
            antes: [2, 3, 4]
          - voucher: Telescope
            antes: [1, 2]
          - tarotCard: TheFool
            antes: [1]
          - planetCard: Jupiter
            antes: [1, 2]
        mustNot:
          - boss: TheWall
            antes: [2]
        """;

    /// <summary>A fixed pile of seeds, the same on every run.</summary>
    private static string[] SeedPile(int count)
    {
        const string alphabet = "123456789ABCDEFGHIJKLMNPQRSTUVWXYZ";
        var random = new Random(20261025);
        var seeds = new string[count];
        Span<char> chars = stackalloc char[8];
        for (int i = 0; i < count; i++)
        {
            for (int c = 0; c < chars.Length; c++)
                chars[c] = alphabet[random.Next(alphabet.Length)];
            seeds[i] = new string(chars);
        }
        return seeds;
    }

    private static List<string> Search(string yaml, string[] seeds)
    {
        var config = ProofSearch.LoadOrThrow(yaml);
        var results = new List<string>();
        using var search = MotelySearchBuilder
            .CreateSettings(config, 0)
            .WithAutoScoreCutoff(false)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(2)
            .WithQuietMode(true)
            .WithScoredResultCallback(r =>
            {
                var line = $"{r.Seed},{r.Score},{string.Join(",", r.TallyValuesSpan.ToArray())}";
                lock (results)
                    results.Add(line);
            })
            .Start();
        search.AwaitCompletion();
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    [Fact]
    public void ShorthandFilter_FindsExactlyWhatItsMappingTwinFinds()
    {
        var seeds = SeedPile(40_000);
        var shorthand = Search(ShorthandFilter, seeds);
        var mapping = Search(MappingFilter, seeds);

        Assert.NotEmpty(mapping);
        Assert.Equal(mapping, shorthand);
    }

    [Fact]
    public void MixedList_KeepsOrderAndBothForms()
    {
        var config = ProofSearch.LoadOrThrow("""
            must:
              - The Eye in ante 3
              - joker: Blueprint
                antes: [1, 2]
                score: 5
              - Charm Tag in ante 1
            """);

        Assert.Collection(
            config.Must,
            c => Assert.Equal([MotelyBossBlind.TheEye], Assert.IsType<BossClause>(c).Bosses),
            c => Assert.Equal(5, Assert.IsType<JokerClause>(c).Score),
            c => Assert.Equal([MotelyTag.CharmTag], Assert.IsType<TagClause>(c).Tags));
    }

    [Fact]
    public void ShorthandInsideOr_IsAClause()
    {
        var or = Assert.IsType<OrClause>(OnlyMust("""
            must:
              - or:
                  - Perkeo in ante 1
                  - boss: TheEye
                    antes: [3]
            """));
        Assert.Equal(2, or.Clauses.Length);
        Assert.IsType<LegendaryJokerClause>(or.Clauses[0]);
    }

    private static string LoadError(string yaml)
    {
        Assert.False(JamlConfigLoader.TryLoad(yaml, out _, out var error), "the filter loaded");
        return error!;
    }

    /// <summary>The error for <paramref name="clause"/> written on line 5, after a mapping clause:
    /// it must name that line, and what follows "JAML line 5: " is returned.</summary>
    private static string ErrorOnLine5(string clause)
    {
        var error = LoadError($"name: bad\nmust:\n  - boss: TheEye\n    antes: [3]\n  - {clause}\n");
        const string Prefix = "JAML line 5: ";
        Assert.StartsWith(Prefix, error);
        return error[Prefix.Length..];
    }

    [Fact]
    public void UnknownName_SuggestsTheClosest()
    {
        Assert.Equal("'Perkoe' is not a known name; did you mean Perkeo?", ErrorOnLine5("Perkoe in ante 1"));
        Assert.Contains("BlackHole", ErrorOnLine5("blak hole in ante 2"));
    }

    [Fact]
    public void UnknownName_WithAnEdition_SuggestsForTheNameAlone()
    {
        Assert.Equal("'Bluprint' is not a known name; did you mean Blueprint?", ErrorOnLine5("Negative Bluprint in ante 1"));
    }

    [Fact]
    public void MissingAntes_SaysToAddInAnte()
    {
        Assert.Equal(
            "'Perkeo' says no antes: add `in ante N`, as in `Perkeo in ante 1`, or `in antes 1-8` for any ante.",
            ErrorOnLine5("Perkeo"));
        Assert.Equal("'Perkeo ante 1': write `in` before the antes, as in `Perkeo in ante 1`.", ErrorOnLine5("Perkeo ante 1"));
        Assert.Equal("'Perkeo in 1': write `ante` after `in`, as in `Perkeo in ante 1`.", ErrorOnLine5("Perkeo in 1"));
        Assert.Contains("no ante numbers after `ante`", ErrorOnLine5("Perkeo in ante"));
    }

    [Theory]
    [InlineData("Perkeo in ante 3-1", "ante range '3-1' runs backwards: write 1-3.")]
    [InlineData("Perkeo in ante 1 2", "ante list '1 2' is not one the shorthand reads: write `ante 1`, `antes 1 or 2`, `antes 1, 2 and 3` or `antes 1-3`.")]
    [InlineData("Perkeo in ante 1,", "ante list '1,' is not one the shorthand reads: write `ante 1`, `antes 1 or 2`, `antes 1, 2 and 3` or `antes 1-3`.")]
    [InlineData("Perkeo in ante one", "ante list 'one' is not one the shorthand reads: write `ante 1`, `antes 1 or 2`, `antes 1, 2 and 3` or `antes 1-3`.")]
    [InlineData("Perkeo in ante 1-", "ante list '1-' is not one the shorthand reads: write `ante 1`, `antes 1 or 2`, `antes 1, 2 and 3` or `antes 1-3`.")]
    [InlineData("Perkeo in ante 1-1000", "ante '1000' is out of range: antes run from 0 to 999.")]
    public void BadAnteList_SaysTheAcceptedForms(string clause, string message)
    {
        Assert.Equal(message, ErrorOnLine5(clause));
    }

    [Theory]
    [InlineData("Negative The Eye in ante 3", "'Negative The Eye': only a joker takes an edition, and TheEye is a boss; drop `Negative`.")]
    [InlineData("Foil The Fool in ante 1", "'Foil The Fool': only a joker takes an edition, and TheFool is a tarot card; drop `Foil`.")]
    [InlineData("Holographic Charm Tag in ante 1", "'Holographic Charm Tag': only a joker takes an edition, and CharmTag is a tag; drop `Holographic`.")]
    public void Edition_OnATypeWithoutOne_IsAnError(string clause, string message)
    {
        Assert.Equal(message, ErrorOnLine5(clause));
    }

    [Fact]
    public void AmbiguousName_ListsEachFamily_AndPointsAtTheMappingForm()
    {
        // No two engine families share a name today, so the guard is proven on a table that does.
        var index = JamlShorthand.BuildIndex([
            new(JamlShorthand.Family.Tarot, "TheWheelOfFortune", (int)MotelyTarotCard.TheWheelOfFortune),
            new(JamlShorthand.Family.Spectral, "TheWheelOfFortune", (int)MotelySpectralCard.Hex),
        ]);

        var ex = Assert.Throws<FormatException>(() => JamlShorthand.Parse("The Wheel of Fortune in ante 1", index));
        Assert.Equal(
            "'The Wheel of Fortune' is a tarot card (`tarotCard: TheWheelOfFortune`) and a spectral card "
                + "(`spectralCard: TheWheelOfFortune`): use the mapping form to say which, e.g. `tarotCard: TheWheelOfFortune`.",
            ex.Message);
    }

    [Fact]
    public void EngineNames_AreOneFamilyEach()
    {
        // If an engine update ever adds a name to two families, shorthand reports it as ambiguous;
        // this pins today's state so that change is noticed.
        var shared = JamlShorthand
            .EngineEntries()
            .GroupBy(e => JamlShorthand.Normalize(e.Name))
            .Where(g => g.Select(e => e.Family).Distinct().Count() > 1)
            .Select(g => g.Key);
        Assert.Empty(shared);
    }

    [Fact]
    public void ShorthandBoss_InAnImpossibleAnte_GetsTheFilterCheckWarning()
    {
        var config = ProofSearch.LoadOrThrow("must:\n  - The Eye in ante 1 or 2\nshould:\n  - The Ox in antes 5-6\n");

        var warnings = FilterCheck.Warnings(config);
        Assert.Contains(warnings, w => w.StartsWith("must boss TheEye: antes [1, 2] are all impossible", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.StartsWith("should boss TheOx: ante(s) 5 can never hold it", StringComparison.Ordinal));
    }

    [Fact]
    public void ShorthandFilter_LoadsFromJsonToo()
    {
        // JSON is JAML: a string in a clause list is shorthand there as well.
        var json = JamlConfigLoader.ToJson(ShorthandFilter);
        var fromJson = ProofSearch.LoadOrThrow(json);
        var fromYaml = ProofSearch.LoadOrThrow(ShorthandFilter);
        Assert.Equal(fromYaml.Should.Select(Fields), fromJson.Should.Select(Fields));
    }
}
