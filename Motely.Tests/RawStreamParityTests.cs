using System.Runtime.Intrinsics;

namespace Motely.Tests;

/// <summary>
/// R3 parity for the vector stream surface no shipped desc calls yet: specialty joker
/// streams (Judgement/Wraith/RiffRaff/RareTag/UncommonTag), raw fixed-rarity shop joker
/// streams, 8-Ball / Omen Globe rolls, the pack HasThe helpers, per-lane spectral pack
/// contents and masked buffoon contents. Every value the SIMD side reads must equal the
/// scalar engine's read, lane for lane — this is the test class that catches the
/// missing-category-bits family of bugs. Black stake so sticker rolls execute.
/// </summary>
public sealed class RawStreamParityTests
{
    private const int MaxAnte = 2;

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

    private sealed class Collector
    {
        public readonly List<string> Mismatches = [];
        public int LanesVerified;

        public void Report(string seed, string what, int ante, int index, string vector, string scalar)
        {
            lock (Mismatches)
            {
                Mismatches.Add($"{seed} ante {ante} {what}[{index}]: vector={vector} scalar={scalar}");
            }
        }
    }

    private sealed class RawParityDesc(Collector collector)
        : IMotelySeedFilterDesc<RawParityDesc.RawParityFilter>
    {
        public RawParityFilter CreateFilter(ref MotelyFilterCreationContext ctx) => new(collector);

        public readonly struct RawParityFilter(Collector collector) : IMotelySeedFilter
        {
            private readonly Collector _collector = collector;

            private const int SpecialtyPulls = 3;
            private const int RawPulls = 2;
            private const int LuckRolls = 4;

            private static int StripCategory(int value) =>
                value & (MotelyGlobals.ItemTypeMask & ~MotelyGlobals.ItemTypeCategoryMask);

            public VectorMask Filter(ref MotelyVectorSearchContext ctx)
            {
                int lanes = MotelyItemVector.Count;
                // [ante][pull][lane] for item streams; [ante][roll] lane-masks for luck rolls.
                var specialty = new Dictionary<string, int[][][]>();
                foreach (var name in (string[])["judgement", "wraith", "riffRaff", "rareTag", "uncommonTag", "rawCommon", "rawUncommon", "rawRare"])
                    specialty[name] = new int[MaxAnte + 1][][];
                var eightBall = new uint[LuckRolls];
                var omen = new uint[LuckRolls];
                var arcanaHasOne = new uint[MaxAnte + 1];
                var arcanaHasAny = new uint[MaxAnte + 1];
                var celestialHasOne = new uint[MaxAnte + 1];
                var celestialHasAny = new uint[MaxAnte + 1];
                var spectralHasOne = new uint[MaxAnte + 1];
                var spectralHasAny = new uint[MaxAnte + 1];
                var perLaneTypes = new int[MaxAnte + 1][][];
                var buffoonMasked = new int[MaxAnte + 1][][];

                int[][] Pull(ref MotelyVectorJokerFixedRarityStream stream, ref MotelyVectorSearchContext c, int pulls)
                {
                    var rows = new int[pulls][];
                    for (int i = 0; i < pulls; i++)
                    {
                        var item = c.GetNextJoker(ref stream);
                        rows[i] = new int[lanes];
                        for (int lane = 0; lane < lanes; lane++)
                            rows[i][lane] = item[lane].Value;
                    }
                    return rows;
                }

                int[][] PullMixed(ref MotelyVectorJokerStream stream, ref MotelyVectorSearchContext c, int pulls)
                {
                    var rows = new int[pulls][];
                    for (int i = 0; i < pulls; i++)
                    {
                        var item = c.GetNextJoker(ref stream);
                        rows[i] = new int[lanes];
                        for (int lane = 0; lane < lanes; lane++)
                            rows[i][lane] = item[lane].Value;
                    }
                    return rows;
                }

                for (int ante = 1; ante <= MaxAnte; ante++)
                {
                    var judgement = ctx.CreateJudgementJokerStream(ante);
                    specialty["judgement"][ante] = PullMixed(ref judgement, ref ctx, SpecialtyPulls);
                    var wraith = ctx.CreateWraithJokerStream(ante);
                    specialty["wraith"][ante] = PullMixed(ref wraith, ref ctx, SpecialtyPulls);
                    var riffRaff = ctx.CreateRiffRaffJokerStream(ante);
                    specialty["riffRaff"][ante] = Pull(ref riffRaff, ref ctx, SpecialtyPulls);
                    var rareTag = ctx.CreateRareTagJokerStream(ante);
                    specialty["rareTag"][ante] = Pull(ref rareTag, ref ctx, SpecialtyPulls);
                    var uncommonTag = ctx.CreateUncommonTagJokerStream(ante);
                    specialty["uncommonTag"][ante] = Pull(ref uncommonTag, ref ctx, SpecialtyPulls);
                    var rawCommon = ctx.CreateCommonShopJokerStream(ante);
                    specialty["rawCommon"][ante] = Pull(ref rawCommon, ref ctx, RawPulls);
                    var rawUncommon = ctx.CreateUncommonShopJokerStream(ante);
                    specialty["rawUncommon"][ante] = Pull(ref rawUncommon, ref ctx, RawPulls);
                    var rawRare = ctx.CreateRareShopJokerStream(ante);
                    specialty["rawRare"][ante] = Pull(ref rawRare, ref ctx, RawPulls);

                    var arcanaOne = ctx.CreateArcanaPackTarotStream(ante);
                    arcanaHasOne[ante] = ctx.GetNextArcanaPackHasThe(
                        ref arcanaOne, MotelyTarotCard.TheFool, MotelyBoosterPackSize.Normal
                    ).Value;
                    var arcanaAny = ctx.CreateArcanaPackTarotStream(ante);
                    arcanaHasAny[ante] = ctx.GetNextArcanaPackHasThe(
                        ref arcanaAny,
                        [MotelyTarotCard.Death, MotelyTarotCard.TheFool, MotelyTarotCard.Justice],
                        MotelyBoosterPackSize.Mega
                    ).Value;

                    var celestialOne = ctx.CreateCelestialPackPlanetStream(ante);
                    celestialHasOne[ante] = ctx.GetNextCelestialPackHasThe(
                        ref celestialOne, MotelyPlanetCard.Pluto, MotelyBoosterPackSize.Normal
                    ).Value;
                    var celestialAny = ctx.CreateCelestialPackPlanetStream(ante);
                    celestialHasAny[ante] = ctx.GetNextCelestialPackHasThe(
                        ref celestialAny,
                        [MotelyPlanetCard.Pluto, MotelyPlanetCard.Mercury, MotelyPlanetCard.Venus],
                        MotelyBoosterPackSize.Mega
                    ).Value;

                    var spectralOne = ctx.CreateSpectralPackSpectralStream(ante);
                    spectralHasOne[ante] = ctx.GetNextSpectralPackHasThe(
                        ref spectralOne, MotelySpectralCard.Sigil, MotelyBoosterPackSize.Normal
                    ).Value;
                    var spectralAny = ctx.CreateSpectralPackSpectralStream(ante);
                    spectralHasAny[ante] = ctx.GetNextSpectralPackHasThe(
                        ref spectralAny,
                        [MotelySpectralCard.Sigil, MotelySpectralCard.Grim, MotelySpectralCard.Aura],
                        MotelyBoosterPackSize.Mega
                    ).Value;

                    var perLaneStream = ctx.CreateSpectralPackSpectralStream(ante);
                    var perLane = ctx.GetNextSpectralPackContentsPerLane(
                        ref perLaneStream,
                        new VectorEnum256<MotelyBoosterPackSize>(
                            Vector256.Create((int)MotelyBoosterPackSize.Normal)
                        ),
                        VectorMask.AllBitsSet
                    );
                    perLaneTypes[ante] = new int[2][];
                    for (int i = 0; i < 2; i++)
                    {
                        perLaneTypes[ante][i] = new int[lanes];
                        for (int lane = 0; lane < lanes; lane++)
                            perLaneTypes[ante][i][lane] = perLane.GetItem(i)[lane].Value;
                    }

                    var buffoonStream = ctx.CreateBuffoonPackJokerStream(ante);
                    var masked = ctx.GetNextBuffoonPackContentsMasked(
                        ref buffoonStream,
                        MotelyBoosterPackSize.Normal,
                        Vector512<double>.AllBitsSet
                    );
                    buffoonMasked[ante] = new int[masked.Length][];
                    for (int i = 0; i < masked.Length; i++)
                    {
                        buffoonMasked[ante][i] = new int[lanes];
                        for (int lane = 0; lane < lanes; lane++)
                            buffoonMasked[ante][i][lane] = masked.GetItem(i)[lane].Value;
                    }
                }

                var ebStream = ctx.CreateEightBallPrngStream();
                for (int i = 0; i < LuckRolls; i++)
                    eightBall[i] = ctx.GetNextEightBallTarot(ref ebStream).Value;
                var omenStream = ctx.CreateOmenGlobePrngStream();
                for (int i = 0; i < LuckRolls; i++)
                    omen[i] = ctx.GetNextOmenGlobeSpectral(ref omenStream).Value;

                var collector = _collector;

                return ctx.SearchIndividualSeeds(single =>
                {
                    int lane = single.VectorLane;
                    string seed = single.GetSeed();
                    bool allMatch = true;

                    void Check(string what, int ante, int index, string vector, string scalar)
                    {
                        if (vector == scalar)
                            return;
                        allMatch = false;
                        collector.Report(seed, what, ante, index, vector, scalar);
                    }

                    void CheckJokers(string what, int ante, int[][] vectorRows, Func<int, MotelyItem> next)
                    {
                        for (int i = 0; i < vectorRows.Length; i++)
                            Check(what, ante, i, $"{vectorRows[i][lane]:X8}", $"{next(i).Value:X8}");
                    }

                    for (int ante = 1; ante <= MaxAnte; ante++)
                    {
                        var judgement = single.CreateJudgementJokerStream(ante);
                        CheckJokers("judgement", ante, specialty["judgement"][ante], _ => single.GetNextJoker(ref judgement));
                        var wraith = single.CreateWraithJokerStream(ante);
                        CheckJokers("wraith", ante, specialty["wraith"][ante], _ => single.GetNextJoker(ref wraith));
                        var riffRaff = single.CreateRiffRaffJokerStream(ante);
                        CheckJokers("riffRaff", ante, specialty["riffRaff"][ante], _ => single.GetNextJoker(ref riffRaff));
                        var rareTag = single.CreateRareTagJokerStream(ante);
                        CheckJokers("rareTag", ante, specialty["rareTag"][ante], _ => single.GetNextJoker(ref rareTag));
                        var uncommonTag = single.CreateUncommonTagJokerStream(ante);
                        CheckJokers("uncommonTag", ante, specialty["uncommonTag"][ante], _ => single.GetNextJoker(ref uncommonTag));
                        var rawCommon = single.CreateCommonShopJokerStream(ante);
                        CheckJokers("rawCommon", ante, specialty["rawCommon"][ante], _ => single.GetNextJoker(ref rawCommon));
                        var rawUncommon = single.CreateUncommonShopJokerStream(ante);
                        CheckJokers("rawUncommon", ante, specialty["rawUncommon"][ante], _ => single.GetNextJoker(ref rawUncommon));
                        var rawRare = single.CreateRareShopJokerStream(ante);
                        CheckJokers("rawRare", ante, specialty["rawRare"][ante], _ => single.GetNextJoker(ref rawRare));

                        bool PackHas(MotelyItem[] cards, int[] targets)
                        {
                            foreach (var card in cards)
                                foreach (var t in targets)
                                    if (StripCategory(card.Value) == t)
                                        return true;
                            return false;
                        }

                        var arcanaOne = single.CreateArcanaPackTarotStream(ante);
                        var arcanaNormal = single
                            .GetNextArcanaPackContents(ref arcanaOne, MotelyBoosterPackSize.Normal)
                            .AsArray();
                        Check("arcanaHasOne", ante, 0,
                            ((arcanaHasOne[ante] >> lane) & 1) == 1 ? "T" : "F",
                            PackHas(arcanaNormal, [(int)MotelyTarotCard.TheFool]) ? "T" : "F");
                        var arcanaAny = single.CreateArcanaPackTarotStream(ante);
                        var arcanaMega = single
                            .GetNextArcanaPackContents(ref arcanaAny, MotelyBoosterPackSize.Mega)
                            .AsArray();
                        Check("arcanaHasAny", ante, 0,
                            ((arcanaHasAny[ante] >> lane) & 1) == 1 ? "T" : "F",
                            PackHas(arcanaMega, [(int)MotelyTarotCard.Death, (int)MotelyTarotCard.TheFool, (int)MotelyTarotCard.Justice]) ? "T" : "F");

                        var celestialOne = single.CreateCelestialPackPlanetStream(ante);
                        var celestialNormal = single
                            .GetNextCelestialPackContents(ref celestialOne, MotelyBoosterPackSize.Normal)
                            .AsArray();
                        Check("celestialHasOne", ante, 0,
                            ((celestialHasOne[ante] >> lane) & 1) == 1 ? "T" : "F",
                            PackHas(celestialNormal, [(int)MotelyPlanetCard.Pluto]) ? "T" : "F");
                        var celestialAny = single.CreateCelestialPackPlanetStream(ante);
                        var celestialMega = single
                            .GetNextCelestialPackContents(ref celestialAny, MotelyBoosterPackSize.Mega)
                            .AsArray();
                        Check("celestialHasAny", ante, 0,
                            ((celestialHasAny[ante] >> lane) & 1) == 1 ? "T" : "F",
                            PackHas(celestialMega, [(int)MotelyPlanetCard.Pluto, (int)MotelyPlanetCard.Mercury, (int)MotelyPlanetCard.Venus]) ? "T" : "F");

                        var spectralOne = single.CreateSpectralPackSpectralStream(ante);
                        var spectralNormal = single
                            .GetNextSpectralPackContents(ref spectralOne, MotelyBoosterPackSize.Normal)
                            .AsArray();
                        Check("spectralHasOne", ante, 0,
                            ((spectralHasOne[ante] >> lane) & 1) == 1 ? "T" : "F",
                            PackHas(spectralNormal, [(int)MotelySpectralCard.Sigil]) ? "T" : "F");
                        var spectralAny = single.CreateSpectralPackSpectralStream(ante);
                        var spectralMega = single
                            .GetNextSpectralPackContents(ref spectralAny, MotelyBoosterPackSize.Mega)
                            .AsArray();
                        Check("spectralHasAny", ante, 0,
                            ((spectralHasAny[ante] >> lane) & 1) == 1 ? "T" : "F",
                            PackHas(spectralMega, [(int)MotelySpectralCard.Sigil, (int)MotelySpectralCard.Grim, (int)MotelySpectralCard.Aura]) ? "T" : "F");

                        // Per-lane contents with all lanes Normal (2 cards): the stored value is
                        // the masked Type vector, so compare types.
                        var perLaneScalar = single.CreateSpectralPackSpectralStream(ante);
                        var scalarSpectralPack = single
                            .GetNextSpectralPackContents(ref perLaneScalar, MotelyBoosterPackSize.Normal)
                            .AsArray();
                        for (int i = 0; i < 2; i++)
                            Check("perLane", ante, i,
                                $"{perLaneTypes[ante][i][lane]:X8}",
                                $"{(int)scalarSpectralPack[i].Type:X8}");

                        var buffoonStream = single.CreateBuffoonPackJokerStream(ante);
                        var scalarBuffoon = single
                            .GetNextBuffoonPackContents(ref buffoonStream, MotelyBoosterPackSize.Normal)
                            .AsArray();
                        for (int i = 0; i < scalarBuffoon.Length; i++)
                            Check("buffoonMasked", ante, i,
                                $"{buffoonMasked[ante][i][lane]:X8}",
                                $"{scalarBuffoon[i].Value:X8}");
                    }

                    var ebScalar = single.CreateEightBallPrngStream();
                    for (int i = 0; i < LuckRolls; i++)
                        Check("eightBall", 0, i,
                            ((eightBall[i] >> lane) & 1) == 1 ? "T" : "F",
                            single.GetNextEightBallTarot(ref ebScalar) ? "T" : "F");
                    var omenScalar = single.CreateOmenGlobePrngStream();
                    for (int i = 0; i < LuckRolls; i++)
                        Check("omen", 0, i,
                            ((omen[i] >> lane) & 1) == 1 ? "T" : "F",
                            single.GetNextOmenGlobeSpectral(ref omenScalar) ? "T" : "F");

                    lock (collector.Mismatches)
                    {
                        collector.LanesVerified++;
                    }
                    return allMatch ? 1 : 0;
                });
            }
        }
    }

    [Fact]
    public void RawVectorStreams_MatchScalar_LaneForLane()
    {
        var collector = new Collector();
        using var search = new MotelySearchSettings<RawParityDesc.RawParityFilter>(
            new RawParityDesc(collector)
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.Black)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();

        Assert.Equal(Seeds.Length, collector.LanesVerified);
        if (collector.Mismatches.Count > 0)
            File.WriteAllLines(
                Path.Combine(Path.GetTempPath(), "raw-parity-mismatches.txt"),
                collector.Mismatches
            );
        Assert.Empty(collector.Mismatches);
        Assert.Equal(Seeds.Length, (int)search.MatchingSeeds);
    }
}
