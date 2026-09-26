using Motely.Filters;
using Motely.Filters.Jaml;
using Xunit;

namespace Motely.Tests;

/// <summary>
/// <c>and:</c>/<c>or:</c> pass no antes down: each clause writes its own, and an unscoped clause
/// defaults to 1..8 on its own record. Antes on the group is an unknown key.
/// Neg free Oops package: Neg never ante 1, so both arms say [2..8] themselves.
/// </summary>
public class JamlNegFreeOopsAndTests
{
    private const string Seed = "1F5WEAYR";

    private static readonly int[] NegScope = [2, 3, 4, 5, 6, 7, 8];
    private static readonly int[] AllAntes = [1, 2, 3, 4, 5, 6, 7, 8];

    private static (long Matching, int? Score) Run(string jaml)
    {
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        int? score = null;
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator([Seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(r => score = r.Score);
        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, score);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    public void AntesOnTheGroup_IsALoadError(string group)
    {
        Assert.False(
            JamlConfigLoader.TryLoad(
                $"""
                must:
                  - {group}:
                      antes: [2, 3, 4, 5, 6, 7, 8]
                      clauses:
                        - smallBlindTag: NegativeTag
                """,
                out _,
                out var error
            )
        );
        Assert.Contains("'antes'", error);
    }

    [Fact]
    public void EachArm_KeepsItsOwnAntes_AndUnscopedArmsDefaultTo1Through8()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(
                """
                must:
                  - and:
                      clauses:
                        - smallBlindTag: NegativeTag
                          antes: [2, 3, 4, 5, 6, 7, 8]
                        - or:
                            clauses:
                              - uncommonJoker: OopsAll6s
                              - uncommonJoker: OopsAll6s
                                antes: [6]
                """,
                out var config,
                out var error
            ),
            error
        );

        var and = Assert.IsType<AndClause>(Assert.Single(config!.Must));
        Assert.Equal(NegScope, Assert.IsAssignableFrom<IAnteScopedClause>(and.Clauses[0]).Antes);
        var or = Assert.IsType<OrClause>(and.Clauses[1]);
        Assert.Equal(AllAntes, Assert.IsAssignableFrom<IAnteScopedClause>(or.Clauses[0]).Antes);
        Assert.Equal([6], Assert.IsAssignableFrom<IAnteScopedClause>(or.Clauses[1]).Antes);
    }

    [Fact]
    public void NegFreeOops_EachArmScoped2Through8_Hits_1F5WEAYR()
    {
        var (matching, score) = Run(
            """
            name: neg-free-oops
            deck: Anaglyph
            stake: White
            must:
              - and:
                  clauses:
                    - smallBlindTag: NegativeTag
                      antes: [2, 3, 4, 5, 6, 7, 8]
                    - uncommonJoker: OopsAll6s
                      antes: [2, 3, 4, 5, 6, 7, 8]
                      sources: { shopItems: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15] }
            should:
              - uncommonJoker: OopsAll6s
                score: 10
                sources: { shopItems: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15] }
            """
        );
        Assert.Equal(1, matching);
        Assert.True(score is > 0, $"expected Neg-free Oops package on {Seed}, got {score}");
    }
}
