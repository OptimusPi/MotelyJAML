using Motely.Filters;
using Motely.Filters.Jaml;
using Xunit;

namespace Motely.Tests;

/// <summary>
/// ADR-001 revisit: every <c>int[]</c> key accepts range shorthand. <c>1-8</c>, <c>1..8</c> and
/// <c>1 to 8</c> (any case) expand to the integers they name, alone or as items of a list, and a
/// descending range is a load error that names the key.
/// </summary>
public sealed class JamlAnteRangeTests
{
    private static readonly int[] OneThroughEight = [1, 2, 3, 4, 5, 6, 7, 8];

    private static IAnteScopedClause LoadSingleMust(string clauseLines)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(
                $"""
                must:
                  - joker: Blueprint
                {clauseLines}
                """,
                out var config,
                out var error
            ),
            error
        );
        return Assert.IsAssignableFrom<IAnteScopedClause>(Assert.Single(config!.Must));
    }

    [Theory]
    [InlineData("1-8")]
    [InlineData("1..8")]
    [InlineData("1 to 8")]
    [InlineData("1 TO 8")]
    [InlineData("  1 - 8  ")]
    [InlineData("[1-8]")]
    [InlineData("[1..4, 5-8]")]
    public void ScalarRange_ExpandsToEveryAnte(string antes) =>
        Assert.Equal(OneThroughEight, LoadSingleMust($"    antes: {antes}").Antes);

    [Theory]
    [InlineData("[1-3, 7]")]
    [InlineData("[1..3, 7]")]
    [InlineData("[1 to 3, 7]")]
    public void RangeInsideAList_ExpandsInPlace(string antes) =>
        Assert.Equal([1, 2, 3, 7], LoadSingleMust($"    antes: {antes}").Antes);

    [Fact]
    public void SingleElementRange_IsThatOneAnte() =>
        Assert.Equal([4], LoadSingleMust("    antes: 4-4").Antes);

    [Fact]
    public void PlainAntes_StillLoadUnchanged() =>
        Assert.Equal([2, 5], LoadSingleMust("    antes: [2, 5]").Antes);

    [Fact]
    public void Ranges_WorkOnSourceIndicesToo()
    {
        var clause = Assert.IsType<JokerClause>(LoadSingleMust("    sources: { shopItems: 0-3, boosterPacks: [0, 2..3] }"));
        Assert.Equal([0, 1, 2, 3], clause.Sources!.ShopItems);
        Assert.Equal([0, 2, 3], clause.Sources.BoosterPacks);
    }

    [Theory]
    [InlineData("8-1")]
    [InlineData("[1, 5..2]")]
    public void DescendingRange_IsALoadErrorNamingTheKey(string antes)
    {
        Assert.False(
            JamlConfigLoader.TryLoad(
                $"""
                must:
                  - joker: Blueprint
                    antes: {antes}
                """,
                out _,
                out var error
            )
        );
        Assert.Contains("descending range", error);
        Assert.Contains("`antes`", error);
        Assert.Contains("JAML line", error);
    }

    [Theory]
    [InlineData("0-1024")]
    [InlineData("[1, 1..999999999]")]
    public void OversizedRange_IsALoadErrorNotAnAllocation(string antes)
    {
        Assert.False(
            JamlConfigLoader.TryLoad(
                $"""
                must:
                  - joker: Blueprint
                    antes: {antes}
                """,
                out _,
                out var error
            )
        );
        Assert.Contains("at most 1024 values per key", error);
        Assert.Contains("`antes`", error);
    }

    [Theory]
    [InlineData("[0-1023, 0-1023]")]
    [InlineData("[0..600, 601..1024]")]
    [InlineData("[5, 0-1023]")]
    public void RangesPastTheCapTogether_AreALoadErrorNotAnAllocation(string antes)
    {
        Assert.False(
            JamlConfigLoader.TryLoad(
                $"""
                must:
                  - joker: Blueprint
                    antes: {antes}
                """,
                out _,
                out var error
            )
        );
        Assert.Contains("at most 1024 values per key", error);
        Assert.Contains("`antes`", error);
        Assert.Contains("JAML line 3", error);
    }

    // `to` at int.MaxValue used to wrap the expansion counter to int.MinValue and loop forever.
    [Theory]
    [InlineData("2147483647-2147483647", 1)]
    [InlineData("2147483640..2147483647", 8)]
    public async Task RangeEndingAtIntMax_Terminates(string antes, int count)
    {
        // WaitAsync throws TimeoutException instead of hanging the whole run on a regression.
        var loaded = await Task.Run(() => LoadSingleMust($"    antes: {antes}").Antes).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(count, loaded.Length);
        Assert.Equal(int.MaxValue, loaded[^1]);
    }

    [Fact]
    public void WidestAllowedRange_Loads() =>
        Assert.Equal(1024, LoadSingleMust("    antes: 0-1023").Antes.Length);

    [Theory]
    [InlineData("1-8-9")]
    [InlineData("one to eight")]
    public void MalformedRange_IsStillNotAnInteger(string antes)
    {
        Assert.False(
            JamlConfigLoader.TryLoad(
                $"""
                must:
                  - joker: Blueprint
                    antes: {antes}
                """,
                out _,
                out var error
            )
        );
        Assert.Contains("is not an integer", error);
        Assert.Contains("`antes`", error);
    }
}
