using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// A clause with no <c>sources:</c> must default to somewhere the engine can actually produce the
/// item. The shop's playing-card weight is the Magic Trick weight and its spectral weight is Ghost
/// only, so the old shop-only defaults made <c>standardCard:</c> match nothing on every deck and
/// <c>spectralCard:</c> match nothing on fourteen of fifteen. Fixed seed lists throughout; the
/// seeds are the first hits of a sequential walk from batch 0.
/// </summary>
public sealed class CardDefaultSourcesTests
{
    private static HashSet<string> Run(string jaml, string[] seeds)
    {
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        var found = new List<string>();
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(seed =>
            {
                lock (found)
                    found.Add(seed);
            });
        using var search = settings.Start();
        search.AwaitCompletion();
        return found.ToHashSet();
    }

    private static readonly string[] AceSeeds =
        ["11111111", "21111111", "31111111", "41111111", "51111111", "91111111", "C1111111", "E1111111"];

    [Fact]
    public void StandardCard_NoSources_RedDeck_FindsAcesInStandardPacks()
    {
        var found = Run(
            """
            name: ace-default
            deck: Red
            stake: White
            must:
              - standardCard:
                  rank: Ace
                antes: [1, 2]
            """,
            AceSeeds
        );

        Assert.NotEmpty(found);
        Assert.Subset(AceSeeds.ToHashSet(), found);
        Assert.Contains("11111111", found);
        Assert.Contains("E1111111", found);
    }

    /// <summary>
    /// The engine fact behind the default: no deck starts with Magic Trick, so a shop slot never
    /// rolls a playing card and an explicit shop-only source finds nothing — on the very seeds
    /// whose Standard packs hold Aces, and across the whole first 35³ sequential batch.
    /// </summary>
    [Fact]
    public void StandardCard_ExplicitShopOnly_RedDeck_MatchesNothing()
    {
        const string jaml = """
            name: ace-shop-only
            deck: Red
            stake: White
            must:
              - standardCard:
                  rank: Ace
                antes: [1, 2]
                sources:
                  shopItems: [0, 1, 2, 3, 4, 5, 6, 7]
            """;

        Assert.Empty(Run(jaml, AceSeeds));

        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        long hits = 0;
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(0)
            .WithEndBatchIndex(1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(_ => Interlocked.Increment(ref hits));
        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.Equal(0L, hits);
        Assert.True(search.TotalSeedsSearched > 0);
    }

    [Fact]
    public void SpectralCard_NoSources_RedDeck_FindsEctoplasmInSpectralPacks()
    {
        string[] seeds = ["11111111", "21111111", "61111111", "H1111111", "T1111111", "Y1111111", "Z1111111"];
        var found = Run(
            """
            name: ecto-default
            deck: Red
            stake: White
            must:
              - spectralCard: Ectoplasm
                antes: [1, 2, 3, 4]
            """,
            seeds
        );

        Assert.NotEmpty(found);
        Assert.Subset(seeds.ToHashSet(), found);
        Assert.Contains("61111111", found);
        Assert.Contains("H1111111", found);
    }

    /// <summary>
    /// 41111111 has Ectoplasm only in a Ghost shop; H1111111 has it in a Spectral pack. The default
    /// reads both on Ghost, and on Red — where the shop's spectral weight is zero — only the pack.
    /// </summary>
    [Theory]
    [InlineData("Ghost", true)]
    [InlineData("Red", false)]
    public void SpectralCard_NoSources_ShopHalfPaysOnlyOnGhost(string deck, bool shopSeedMatches)
    {
        string[] seeds = ["41111111", "H1111111", "21111111"];
        var found = Run(
            $"""
            name: ecto-{deck}
            deck: {deck}
            stake: White
            must:
              - spectralCard: Ectoplasm
                antes: [1, 2]
            """,
            seeds
        );

        Assert.Contains("H1111111", found);
        Assert.Equal(shopSeedMatches, found.Contains("41111111"));
        Assert.DoesNotContain("21111111", found);
    }

    [Fact]
    public void DefaultSources_CoverEveryEnginePackSlot()
    {
        int[] everySlot = Enumerable.Range(0, MotelyGlobals.LateAntesMaxPackSlot + 1).ToArray();

        Assert.Equal(everySlot, StandardCardFilterDesc.DefaultSources.BoosterPacks);
        Assert.Empty(StandardCardFilterDesc.DefaultSources.ShopItems);

        Assert.Equal(everySlot, SpectralCardFilterDesc.DefaultSources.BoosterPacks);
        Assert.NotEmpty(SpectralCardFilterDesc.DefaultSources.ShopItems);

        Assert.Equal(everySlot, SpectralCardFilterDesc.DefaultSpecialSources.BoosterPacks);
        Assert.Empty(SpectralCardFilterDesc.DefaultSpecialSources.ShopItems);
    }

    [Fact]
    public void ResolveSources_SplitsSpecialFromOrdinary_AndExplicitWins()
    {
        var ordinary = new SpectralCardClause { Spectrals = [MotelySpectralCard.Ectoplasm], Antes = [1] };
        var any = new SpectralCardClause { Antes = [1] };
        var soul = new SpectralCardClause { Spectrals = [MotelySpectralCard.TheSoul], Antes = [1] };
        var mixed = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.BlackHole, MotelySpectralCard.Ectoplasm],
            Antes = [1],
        };
        var explicitSources = new SpectralCardSourceConfig { Seance = [0] };
        var explicitSoul = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.TheSoul],
            Antes = [1],
            Sources = explicitSources,
        };

        Assert.Same(SpectralCardFilterDesc.DefaultSources, SpectralCardFilterDesc.ResolveSources(ordinary));
        Assert.Same(SpectralCardFilterDesc.DefaultSources, SpectralCardFilterDesc.ResolveSources(any));
        Assert.Same(SpectralCardFilterDesc.DefaultSpecialSources, SpectralCardFilterDesc.ResolveSources(soul));
        Assert.Same(SpectralCardFilterDesc.DefaultSpecialSources, SpectralCardFilterDesc.ResolveSources(mixed));
        Assert.Same(explicitSources, SpectralCardFilterDesc.ResolveSources(explicitSoul));
    }
}
