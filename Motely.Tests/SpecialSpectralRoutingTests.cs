using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// T4 lock: Soul/BlackHole spectral clauses route to <see cref="SpecialSpectralCardFilterDesc"/>,
/// and that path finds Arcana/Celestial specials the content SIMD cannot see.
/// </summary>
public class SpecialSpectralRoutingTests
{
    [Fact]
    public void ClauseToFilterDesc_RoutesTheSoulToSpecialSpectral()
    {
        var clause = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.TheSoul],
            Antes = [1],
        };

        Assert.True(SpecialSpectralCardFilterDesc.Handles(clause));
        Assert.IsType<SpecialSpectralCardFilterDesc>(JamlSearchBuilder.ClauseToFilterDesc(clause));
    }

    [Fact]
    public void ClauseToFilterDesc_RoutesBlackHoleToSpecialSpectral()
    {
        var clause = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.BlackHole],
            Antes = [1],
        };

        Assert.True(SpecialSpectralCardFilterDesc.Handles(clause));
        Assert.IsType<SpecialSpectralCardFilterDesc>(JamlSearchBuilder.ClauseToFilterDesc(clause));
    }

    [Fact]
    public void ClauseToFilterDesc_KeepsOrdinarySpectralOnContentPath()
    {
        var clause = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.Immolate],
            Antes = [1],
        };

        Assert.False(SpecialSpectralCardFilterDesc.Handles(clause));
        Assert.IsType<SpectralCardFilterDesc>(JamlSearchBuilder.ClauseToFilterDesc(clause));
    }

    [Fact]
    public void ClauseToFilterDesc_MixedSpecialAndOrdinary_StillSpecialPath()
    {
        var clause = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.TheSoul, MotelySpectralCard.Immolate],
            Antes = [1],
        };

        Assert.IsType<SpecialSpectralCardFilterDesc>(JamlSearchBuilder.ClauseToFilterDesc(clause));
    }

    /// <summary>
    /// ALEEB has The Soul in ante-1 Arcana packs (see seeds/ALEEB.verified.txt). The content
    /// spectral SIMD never walks Arcana, so only the special route can match this seed.
    /// </summary>
    [Fact]
    public void TheSoulMust_FindsAleebViaSpecialSpectralRoute()
    {
        var config = new JamlConfig
        {
            Id = "t4-soul-aleeb",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(
            new SpectralCardClause
            {
                Spectrals = [MotelySpectralCard.TheSoul],
                Antes = [1],
            }
        );

        string[] seeds = ["PIROCKS", "ALEEB", "LOVEYAHB"];
        var found = new List<string>();

        var settings = JamlSearchBuilder
            .CreateSettings(config)
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

        Assert.Equal("ALEEB", Assert.Single(found));
        Assert.Equal(1L, search.MatchingSeeds);
    }

    /// <summary>
    /// Standalone edition-only soul stream desc still runs (composition form kept alive for T4).
    /// </summary>
    [Fact]
    public void LegendarySoulEditionFilterDesc_RunsOnSeedBatch()
    {
        var clause = new LegendaryJokerClause
        {
            Jokers = [MotelyJoker.Perkeo],
            Edition = MotelyItemEdition.Negative,
            Antes = [1],
            Min = 1,
        };

        var settings = new MotelySearchSettings<LegendarySoulEditionFilterDesc.LegendarySoulEditionFilter>(
            new LegendarySoulEditionFilterDesc(clause)
        )
            .WithSeedGenerator(["ALEEB"], 1)
            .WithThreadCount(1)
            .WithQuietMode(true);

        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.True(search.TotalSeedsSearched >= 1);
    }
}
