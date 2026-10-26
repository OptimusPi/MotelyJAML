using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// Shorthand without antes (<c>- Triboulet</c>) is the mapping twin that omits <c>antes</c>, and a
/// trailing <c>score N</c> is its <c>score: N</c>. Proven field by field, and by scoring one fixed
/// seed list with both forms.
/// </summary>
public sealed class JamlShorthandBareAndScoreTests
{
    private static string[] Clauses(string yaml) => TwinFilters.AllFields(ProofSearch.LoadOrThrow(yaml));

    [Theory]
    [InlineData("Triboulet", "legendaryJoker: Triboulet")]
    [InlineData("canio", "legendaryJoker: Canio")]
    [InlineData("Blueprint", "joker: Blueprint")]
    [InlineData("Negative Perkeo", "legendaryJoker: Perkeo\n    edition: Negative")]
    [InlineData("The Eye", "boss: TheEye")]
    [InlineData("Charm Tag", "tag: CharmTag")]
    [InlineData("Telescope", "voucher: Telescope")]
    [InlineData("Black Hole", "spectralCard: BlackHole")]
    [InlineData("Perkeo in antes 1-8 score 1", "legendaryJoker: Perkeo\n    antes: [1, 2, 3, 4, 5, 6, 7, 8]\n    score: 1")]
    [InlineData("Perkeo in ante 2 score 50", "legendaryJoker: Perkeo\n    antes: [2]\n    score: 50")]
    [InlineData("Blueprint score 5", "joker: Blueprint\n    score: 5")]
    [InlineData("Negative Blueprint in antes 1-3 SCORE 7", "joker: Blueprint\n    edition: Negative\n    antes: [1, 2, 3]\n    score: 7")]
    [InlineData("The Wall in ante 2 score -5", "boss: TheWall\n    antes: [2]\n    score: -5")]
    public void Shorthand_LoadsTheSameClauseAsItsMappingTwin(string shorthand, string mapping)
    {
        Assert.Equal(Clauses($"should:\n  - {mapping}\n"), Clauses($"should:\n  - {shorthand}\n"));
    }

    [Fact]
    public void ZerkeoShapedFilter_FindsExactlyWhatItsMappingTwinFinds()
    {
        TwinFilters.AssertSame(
            """
            deck: Anaglyph
            must:
              - voucher: Hieroglyph
                antes: [1]
            should:
              - legendaryJoker: Perkeo
                antes: [1, 2, 3, 4, 5, 6, 7, 8]
                score: 1
              - legendaryJoker: Triboulet
              - legendaryJoker: Canio
              - legendaryJoker: Chicot
                score: 3
              - joker: Blueprint
              - boss: TheEye
            """,
            """
            deck: Anaglyph
            must:
              - Hieroglyph in ante 1
            should:
              - Perkeo in antes 1-8 score 1
              - Triboulet
              - Canio
              - Chicot score 3
              - Blueprint
              - The Eye
            """);
    }

    private static string ErrorOnLine3(string clause)
    {
        Assert.False(JamlConfigLoader.TryLoad($"name: bad\nmust:\n  - {clause}\n", out _, out var error), "the filter loaded");
        const string Prefix = "JAML line 3: ";
        Assert.StartsWith(Prefix, error);
        return error![Prefix.Length..];
    }

    [Theory]
    [InlineData("Perkeo score", "'Perkeo score': write the score as `score N` at the end, as in `Perkeo score 5`.")]
    [InlineData("Perkeo score five", "'Perkeo score five': the score is a whole number, as in `Perkeo score 5`.")]
    [InlineData("Perkeo score 1 in ante 2", "'Perkeo score 1 in ante 2': write the score as `score N` at the end, as in `Perkeo score 5`.")]
    [InlineData("Perkeo score 1 score 2", "'Perkeo score 1 score 2' says `score` twice: write it once, at the end, as in `Perkeo in ante 1 score 5`.")]
    [InlineData("score 5", "'score 5': write the score as `score N` at the end, as in `Perkeo in ante 1 score 5`.")]
    [InlineData("Perkoe", "'Perkoe' is not a known name; did you mean Perkeo?")]
    [InlineData("Perkoe score 3", "'Perkoe' is not a known name; did you mean Perkeo?")]
    public void BadScoreOrBareName_SaysTheFix(string clause, string message)
    {
        Assert.Equal(message, ErrorOnLine3(clause));
    }

    [Fact]
    public void NoEngineName_HoldsTheWordScore()
    {
        // `score` is read off the end of a line, so a name holding the word would be cut short.
        Assert.DoesNotContain(JamlShorthand.EngineEntries(), e => e.Name.Contains("score", StringComparison.OrdinalIgnoreCase));
    }
}
