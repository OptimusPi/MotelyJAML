namespace Motely.Tests;

/// <summary>
/// Proves the SIMD engine reads the same items as the scalar engine, lane for lane, from the
/// streams both sides implement: the full shop queue (joker/tarot/planet/spectral slot polling)
/// and every pack-content stream (Arcana with Soul rolls, Celestial with Black Hole rolls,
/// Spectral with both, Buffoon). A vectorized stream that drifts from the scalar one by even a
/// single PRNG pull produces different seeds under SIMD prefilters than under --analyze, so this
/// parity is load-bearing for search correctness (PRNG contract: order within a key is law).
/// </summary>
public sealed class VectorScalarParityTests
{
    private const int MaxAnte = 3;
    private const int ShopSlots = 20;

    // Eight 8-char seeds fill all lanes of one vector batch.
    private static readonly string[] Seeds =
    [
        "ALEEBOOO",
        "UNITTEST",
        "KK1XD111",
        "MOTELY77",
        "PIFREAKS",
        "BALATROO",
        "AAAAAAAA",
        "11111111",
    ];

    private sealed class ParityCollector
    {
        public readonly List<string> Mismatches = [];
        public int LanesVerified;

        public void Report(string seed, string stream, int ante, int index, int vector, int scalar)
        {
            lock (Mismatches)
            {
                Mismatches.Add(
                    $"{seed} ante {ante} {stream}[{index}]: vector={vector:X8} scalar={scalar:X8}"
                );
            }
        }
    }

    private sealed class ParityFilterDesc(ParityCollector collector)
        : IMotelySeedFilterDesc<ParityFilterDesc.ParityFilter>
    {
        public ParityFilter CreateFilter(ref MotelyFilterCreationContext ctx)
        {
            return new ParityFilter(collector);
        }

        public readonly struct ParityFilter(ParityCollector collector) : IMotelySeedFilter
        {
            private readonly ParityCollector _collector = collector;

            private static int[][] Flatten(in MotelyVectorItemSet set)
            {
                var rows = new int[set.Length][];
                for (int i = 0; i < set.Length; i++)
                {
                    rows[i] = new int[MotelyItemVector.Count];
                    for (int lane = 0; lane < MotelyItemVector.Count; lane++)
                        rows[i][lane] = set.GetItem(i)[lane].Value;
                }
                return rows;
            }

            public VectorMask Filter(ref MotelyVectorSearchContext ctx)
            {
                // [ante] -> [index] -> [lane] item values read by the SIMD engine.
                var shop = new int[MaxAnte + 1][][];
                var arcana = new int[MaxAnte + 1][][];
                var celestial = new int[MaxAnte + 1][][];
                var spectral = new int[MaxAnte + 1][][];
                var buffoon = new int[MaxAnte + 1][][];

                for (int ante = 1; ante <= MaxAnte; ante++)
                {
                    var shopStream = ctx.CreateShopItemStream(ante);
                    shop[ante] = new int[ShopSlots][];
                    for (int slot = 0; slot < ShopSlots; slot++)
                    {
                        var item = ctx.GetNextShopItem(ref shopStream);
                        var row = new int[MotelyItemVector.Count];
                        for (int lane = 0; lane < MotelyItemVector.Count; lane++)
                            row[lane] = item[lane].Value;
                        shop[ante][slot] = row;
                    }

                    // Normal then Mega from one stream: exercises both pack sizes and keeps
                    // consuming the same PRNG key, like consecutive packs in a real ante.
                    var arcanaStream = ctx.CreateArcanaPackTarotStream(ante);
                    var arcanaItems = ctx.GetNextArcanaPackContents(
                        ref arcanaStream,
                        MotelyBoosterPackSize.Normal
                    );
                    var arcanaMega = ctx.GetNextArcanaPackContents(
                        ref arcanaStream,
                        MotelyBoosterPackSize.Mega
                    );
                    arcana[ante] = [.. Flatten(in arcanaItems), .. Flatten(in arcanaMega)];

                    var celestialStream = ctx.CreateCelestialPackPlanetStream(ante);
                    var celestialItems = ctx.GetNextCelestialPackContents(
                        ref celestialStream,
                        MotelyBoosterPackSize.Normal
                    );
                    var celestialMega = ctx.GetNextCelestialPackContents(
                        ref celestialStream,
                        MotelyBoosterPackSize.Mega
                    );
                    celestial[ante] = [.. Flatten(in celestialItems), .. Flatten(in celestialMega)];

                    var spectralStream = ctx.CreateSpectralPackSpectralStream(ante);
                    var spectralItems = ctx.GetNextSpectralPackContents(
                        ref spectralStream,
                        MotelyBoosterPackSize.Normal
                    );
                    var spectralMega = ctx.GetNextSpectralPackContents(
                        ref spectralStream,
                        MotelyBoosterPackSize.Mega
                    );
                    spectral[ante] = [.. Flatten(in spectralItems), .. Flatten(in spectralMega)];

                    var buffoonStream = ctx.CreateBuffoonPackJokerStream(ante);
                    var buffoonItems = ctx.GetNextBuffoonPackContents(
                        ref buffoonStream,
                        MotelyBoosterPackSize.Normal
                    );
                    var buffoonMega = ctx.GetNextBuffoonPackContents(
                        ref buffoonStream,
                        MotelyBoosterPackSize.Mega
                    );
                    buffoon[ante] = [.. Flatten(in buffoonItems), .. Flatten(in buffoonMega)];
                }

                var collector = _collector;

                return ctx.SearchIndividualSeeds(single =>
                {
                    int lane = single.VectorLane;
                    string seed = single.GetSeed();
                    bool allMatch = true;

                    void Check(string stream, int ante, int index, int vectorValue, int scalarValue)
                    {
                        if (vectorValue == scalarValue)
                            return;
                        allMatch = false;
                        collector.Report(seed, stream, ante, index, vectorValue, scalarValue);
                    }

                    for (int ante = 1; ante <= MaxAnte; ante++)
                    {
                        var shopStream = single.CreateShopItemStream(ante);
                        for (int slot = 0; slot < ShopSlots; slot++)
                        {
                            var item = single.GetNextShopItem(ref shopStream);
                            Check("shop", ante, slot, shop[ante][slot][lane], item.Value);
                        }

                        var arcanaStream = single.CreateArcanaPackTarotStream(ante);
                        var scalarArcana = single
                            .GetNextArcanaPackContents(ref arcanaStream, MotelyBoosterPackSize.Normal)
                            .AsArray()
                            .Concat(
                                single
                                    .GetNextArcanaPackContents(
                                        ref arcanaStream,
                                        MotelyBoosterPackSize.Mega
                                    )
                                    .AsArray()
                            )
                            .ToArray();
                        for (int i = 0; i < scalarArcana.Length; i++)
                            Check("arcana", ante, i, arcana[ante][i][lane], scalarArcana[i].Value);

                        var celestialStream = single.CreateCelestialPackPlanetStream(ante);
                        var scalarCelestial = single
                            .GetNextCelestialPackContents(
                                ref celestialStream,
                                MotelyBoosterPackSize.Normal
                            )
                            .AsArray()
                            .Concat(
                                single
                                    .GetNextCelestialPackContents(
                                        ref celestialStream,
                                        MotelyBoosterPackSize.Mega
                                    )
                                    .AsArray()
                            )
                            .ToArray();
                        for (int i = 0; i < scalarCelestial.Length; i++)
                            Check(
                                "celestial",
                                ante,
                                i,
                                celestial[ante][i][lane],
                                scalarCelestial[i].Value
                            );

                        var spectralStream = single.CreateSpectralPackSpectralStream(ante);
                        var scalarSpectral = single
                            .GetNextSpectralPackContents(
                                ref spectralStream,
                                MotelyBoosterPackSize.Normal
                            )
                            .AsArray()
                            .Concat(
                                single
                                    .GetNextSpectralPackContents(
                                        ref spectralStream,
                                        MotelyBoosterPackSize.Mega
                                    )
                                    .AsArray()
                            )
                            .ToArray();
                        for (int i = 0; i < scalarSpectral.Length; i++)
                            Check(
                                "spectral",
                                ante,
                                i,
                                spectral[ante][i][lane],
                                scalarSpectral[i].Value
                            );

                        var buffoonStream = single.CreateBuffoonPackJokerStream(ante);
                        var scalarBuffoon = single
                            .GetNextBuffoonPackContents(ref buffoonStream, MotelyBoosterPackSize.Normal)
                            .AsArray()
                            .Concat(
                                single
                                    .GetNextBuffoonPackContents(
                                        ref buffoonStream,
                                        MotelyBoosterPackSize.Mega
                                    )
                                    .AsArray()
                            )
                            .ToArray();
                        for (int i = 0; i < scalarBuffoon.Length; i++)
                            Check("buffoon", ante, i, buffoon[ante][i][lane], scalarBuffoon[i].Value);
                    }

                    lock (collector.Mismatches)
                    {
                        collector.LanesVerified++;
                    }

                    return allMatch ? 1 : 0;
                });
            }
        }
    }

    [Theory]
    [InlineData(MotelyDeck.Red, MotelyStake.White)]
    [InlineData(MotelyDeck.Ghost, MotelyStake.White)]
    // Gold stake rolls the sticker streams (Eternal/Perishable/Rental), so sticker bits land in
    // item.Value and any vector/scalar sticker divergence fails a lane. Caught the perishable
    // upper-bound bug on eternal-incompatible jokers.
    [InlineData(MotelyDeck.Red, MotelyStake.Gold)]
    [InlineData(MotelyDeck.Ghost, MotelyStake.Gold)]
    public void VectorStreams_MatchScalar_LaneForLane(MotelyDeck deck, MotelyStake stake)
    {
        var collector = new ParityCollector();

        var settings = new MotelySearchSettings<ParityFilterDesc.ParityFilter>(
            new ParityFilterDesc(collector)
        )
            .WithDeck(deck)
            .WithStake(stake)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true);

        using var search = settings.Start();
        search.AwaitCompletion();

        Assert.True(
            collector.Mismatches.Count == 0,
            $"{collector.Mismatches.Count} lane mismatches:\n"
                + string.Join("\n", collector.Mismatches.Take(40))
        );
        Assert.Equal(Seeds.Length, collector.LanesVerified);
        Assert.Equal(Seeds.Length, search.MatchingSeeds);
    }
}
