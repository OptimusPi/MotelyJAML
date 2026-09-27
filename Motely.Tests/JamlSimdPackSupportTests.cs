namespace Motely.Tests;

/// <summary>
/// Pins P1 SIMD pack helpers: ante-1 slot reachability and Charm/Ethereal routing to the
/// single match core (must ≡ should hits on a small seed list).
/// </summary>
public sealed class JamlSimdPackSupportTests
{
    private static readonly string[] Seeds = ["ALEEB", "MOTELY77", "AAAAAAAA", "11111111"];

    [Fact]
    public void SlotReachable_Ante1_EarlySlots_AlwaysOn()
    {
        var none = VectorMask.NoBitsSet;
        for (int p = 0; p <= MotelyGlobals.EarlyAnteMaxPackSlot; p++)
            Assert.True(JamlSimdPackSupport.SlotReachableMask(1, p, none).IsAllTrue());
    }

    [Fact]
    public void SlotReachable_Ante1_LateSlots_FollowExtension()
    {
        var none = VectorMask.NoBitsSet;
        var all = VectorMask.AllBitsSet;
        int late = MotelyGlobals.EarlyAnteMaxPackSlot + 1;
        Assert.True(JamlSimdPackSupport.SlotReachableMask(1, late, none).IsAllFalse());
        Assert.True(JamlSimdPackSupport.SlotReachableMask(1, late, all).IsAllTrue());
    }

    [Fact]
    public void SlotReachable_LateAntes_AlwaysOn()
    {
        var none = VectorMask.NoBitsSet;
        Assert.True(
            JamlSimdPackSupport
                .SlotReachableMask(2, MotelyGlobals.LateAntesMaxPackSlot, none)
                .IsAllTrue()
        );
    }

    [Fact]
    public void NeedsAnte1Extension_OnlyPastEarlyCap()
    {
        Assert.False(JamlSimdPackSupport.NeedsAnte1Extension(MotelyGlobals.EarlyAnteMaxPackSlot));
        Assert.True(
            JamlSimdPackSupport.NeedsAnte1Extension(MotelyGlobals.EarlyAnteMaxPackSlot + 1)
        );
    }

    private static HashSet<string> RunMust(IJamlClause clause)
    {
        var config = new JamlConfig
        {
            Id = "pack-must",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);
        var hits = new HashSet<string>();
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => hits.Add(s));
        using var search = settings.Start();
        search.AwaitCompletion();
        return hits;
    }

    private static HashSet<string> RunShould(IJamlClause clause)
    {
        clause.Score = 1;
        var config = new JamlConfig
        {
            Id = "pack-should",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Should.Add(clause);
        var hits = new HashSet<string>();
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(r =>
            {
                if (r.Score > 0)
                    hits.Add(r.Seed);
            });
        using var search = settings.Start();
        search.AwaitCompletion();
        return hits;
    }

    [Fact]
    public void CharmTag_MustAndShould_SameHits()
    {
        var must = new TarotCardClause
        {
            Tarots = [MotelyTarotCard.TheFool],
            Antes = [1, 2],
            Min = 1,
            Sources = new TarotCardSourceConfig
            {
                BoosterPacks = [0, 1, 2, 3],
                CharmTag = true,
            },
        };
        var should = new TarotCardClause
        {
            Tarots = [MotelyTarotCard.TheFool],
            Antes = [1, 2],
            Min = 1,
            Score = 1,
            Sources = new TarotCardSourceConfig
            {
                BoosterPacks = [0, 1, 2, 3],
                CharmTag = true,
            },
        };
        Assert.Equal(RunMust(must), RunShould(should));
    }

    [Fact]
    public void EtherealTag_MustAndShould_SameHits()
    {
        var must = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.Immolate],
            Antes = [1, 2],
            Min = 1,
            Sources = new SpectralCardSourceConfig
            {
                BoosterPacks = [0, 1, 2, 3],
                EtherealTag = true,
            },
        };
        var should = new SpectralCardClause
        {
            Spectrals = [MotelySpectralCard.Immolate],
            Antes = [1, 2],
            Min = 1,
            Score = 1,
            Sources = new SpectralCardSourceConfig
            {
                BoosterPacks = [0, 1, 2, 3],
                EtherealTag = true,
            },
        };
        Assert.Equal(RunMust(must), RunShould(should));
    }

    [Fact]
    public void HieroglyphSeed_StillMatches_Slot6_Legendary()
    {
        // Regression: the ante-1 slot clamp must not drop Hieroglyph-extended KHTW99TC.
        const string seed = "KHTW99TC";
        var jaml = """
            name: HieroglyphPerkeo
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
                edition: Negative
                antes: [1]
                sources:
                  boosterPacks: [6]
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var err), err);
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator([seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true);
        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.Equal(1, search.MatchingSeeds);
    }
}
