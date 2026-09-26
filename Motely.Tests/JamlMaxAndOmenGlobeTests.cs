namespace Motely.Tests;

/// <summary>
/// Pins P3: Max is a match upper bound for must/exact confirm (not only score cap), and
/// spectralCard sources.omenGlobe routes to the single match core with Arcana substitution.
/// </summary>
public sealed class JamlMaxAndOmenGlobeTests
{
    [Theory]
    [InlineData(0, 1, null, false)]
    [InlineData(1, 1, null, true)]
    [InlineData(2, 1, null, true)]
    [InlineData(2, 1, 1, false)]
    [InlineData(1, 1, 1, true)]
    [InlineData(3, 2, 4, true)]
    [InlineData(5, 2, 4, false)]
    [InlineData(0, 0, 0, true)] // max 0 is a ceiling: exactly none
    [InlineData(1, 0, 0, false)]
    [InlineData(1, 1, 0, false)] // ceiling below the floor: nothing passes
    public void MeetsOccurrenceBounds_MinAndOptionalMax(
        int raw,
        int min,
        int? max,
        bool expected
    )
    {
        var clause = new JokerClause
        {
            Jokers = [MotelyJoker.Blueprint],
            Antes = [1],
            Min = min,
            Max = max,
        };
        Assert.Equal(expected, JamlScoring.MeetsOccurrenceBounds(raw, clause));
    }

    [Fact]
    public void OmenGlobe_IsExactFilterConfirm()
    {
        Assert.True(
            JamlScoring.IsExactFilterConfirm(
                new SpectralCardClause
                {
                    Spectrals = [MotelySpectralCard.Immolate],
                    Antes = [1],
                    Sources = new SpectralCardSourceConfig { OmenGlobe = true },
                }
            )
        );
    }

    [Fact]
    public void OmenGlobe_MustAndShould_SameHitsOnSeedList()
    {
        string[] seeds = ["ALEEB", "MOTELY77", "AAAAAAAA", "11111111"];

        HashSet<string> Run(bool must)
        {
            var clause = new SpectralCardClause
            {
                Spectrals = [MotelySpectralCard.Immolate, MotelySpectralCard.Familiar],
                Antes = [1, 2, 3],
                Min = 1,
                Score = must ? 0 : 1,
                Sources = new SpectralCardSourceConfig
                {
                    OmenGlobe = true,
                    BoosterPacks = [0, 1, 2, 3, 4, 5],
                },
            };
            var config = new JamlConfig
            {
                Id = must ? "omen-must" : "omen-should",
                Deck = MotelyDeck.Red,
                Stake = MotelyStake.White,
            };
            if (must)
                config.Must.Add(clause);
            else
                config.Should.Add(clause);

            var hits = new HashSet<string>();
            var settings = JamlSearchBuilder
                .CreateSettings(config)
                .WithSeedGenerator(seeds, seeds.Length)
                .WithThreadCount(1)
                .WithQuietMode(true);
            if (must)
                settings = settings.WithSeedMatchCallback(s => hits.Add(s));
            else
                settings = settings.WithScoredResultCallback(r =>
                {
                    if (r.Score > 0)
                        hits.Add(r.Seed);
                });

            using var search = settings.Start();
            search.AwaitCompletion();
            return hits;
        }

        Assert.Equal(Run(must: true), Run(must: false));
    }

    [Fact]
    public void OmenGlobe_LoaderAcceptsSourceKey()
    {
        const string jaml = """
            name: omen
            deck: Red
            stake: White
            must:
              - spectralCard: Immolate
                sources:
                  omenGlobe: true
                  boosterPacks: [0, 1, 2, 3]
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var err), err);
        var sc = Assert.IsType<SpectralCardClause>(config!.Must[0]);
        Assert.NotNull(sc.Sources);
        Assert.True(sc.Sources!.OmenGlobe);
    }

    [Fact]
    public void MaxOnMust_RejectsOvershootViaExactConfirm()
    {
        // Boss exact path: Min=1 Max=0 invalid is loader-side; Max=1 with multi-ante can overshoot.
        // MeetsOccurrenceBounds is the gate — covered by theory above; this pins end-to-end load.
        const string jaml = """
            name: max-must
            deck: Red
            stake: White
            must:
              - boss: TheClub
                antes: [1]
                min: 1
                max: 1
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var err), err);
        Assert.Equal(1, config!.Must[0].Min);
        Assert.Equal(1, config.Must[0].Max);
    }
}
