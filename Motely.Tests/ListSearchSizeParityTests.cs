using Motely.Enums;
using Motely.Filters.Native;

namespace Motely.Tests;

/// <summary>
/// List search returns the same seed no matter how many other seeds share the list.
/// A seed that matches on its own matches inside every larger list that contains it —
/// list membership is the only input, list length carries no meaning.
/// </summary>
public sealed class ListSearchSizeParityTests
{
    private const string KnownSeed = "1946";

    private static string[] Search(string[] seeds)
    {
        var matched = new List<string>();
        using var search = new MotelySearchSettings<NegativeLegendaryJokerSimdFilterDesc.FilterStruct>(
            new NegativeLegendaryJokerSimdFilterDesc()
        )
            .WithAdditionalFilter(new LegendaryJokerShopSoulFilterDesc())
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(matched.Add)
            .Start();
        search.AwaitCompletion();
        return [.. matched];
    }

    /// <summary>
    /// Filler drawn from the engine's own bijective base-35 space (<c>123456789A-Z</c>), so every
    /// entry is a seed Balatro can actually produce.
    /// </summary>
    private static string[] ListOfSize(int size, int knownSeedIndex)
    {
        var list = new List<string>(size);
        for (long i = 0; list.Count < size; i++)
        {
            string filler = SeedMath.SearchIndexToSeed(i, KnownSeed.Length);
            if (filler != KnownSeed)
                list.Add(filler);
        }
        list[knownSeedIndex] = KnownSeed;
        return [.. list];
    }

    [Fact]
    public void KnownSeedMatchesOnItsOwn()
    {
        Assert.Equal([KnownSeed], Search([KnownSeed]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(512)]
    [InlineData(3001)]
    public void KnownSeedMatchesInAListOfAnySize(int size)
    {
        Assert.Contains(KnownSeed, Search(ListOfSize(size, knownSeedIndex: 0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(100)]
    [InlineData(3000)]
    public void KnownSeedMatchesAtAnyPositionInTheList(int index)
    {
        Assert.Contains(KnownSeed, Search(ListOfSize(3001, knownSeedIndex: index)));
    }
}
