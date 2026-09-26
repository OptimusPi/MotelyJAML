using Motely.Analysis;
using Motely.Filters;
using Motely.Filters.Jaml;
using Motely.Filters.Native;

namespace Motely.Tests;

/// <summary>
/// The Jamlyzer riding a search (<see cref="MotelyJamlyzerRiderDesc"/>): every seed the search
/// reports gets its breakdown in the same pass, carrying the search's own score and tally, and that
/// breakdown is identical to what the standalone Jamlyzer says about the seed. Underneath it, the
/// engine contract the rider depends on: the analyze provider sees exactly the reported lanes,
/// never the ones the auto score cutoff dropped.
/// </summary>
public sealed class JamlyzerRiderDescTests
{
    private static readonly string[] Seeds = ["UNITTEST", "ALEEB", "1234567"];

    // A should clause so the search scores; no must, so every listed seed is a find. No antes on
    // the clause on purpose: the search fills 1..8, and the standalone Jamlyzer has to agree.
    private const string ShouldJaml = """
        should:
          - joker: Blueprint
            score: 3
        seeds: []
        """;

    private static JamlConfig Config(params string[] seeds)
    {
        var config = JamlConfigLoader.FromJaml(ShouldJaml);
        foreach (var seed in seeds)
            config.Seeds.Add(seed);
        return config;
    }

    [Fact]
    public void RidesSearch_EveryFindGetsTheStandaloneBreakdownWithTheSearchScore()
    {
        var config = Config();
        var analyzed = new List<MotelyJamlyzerSeedResult>();
        var scored = new List<MotelySeedScore>();

        // Desc before CreateSettings: the builder fills unscoped clauses with antes 1..8 in place,
        // which would narrow the rider to 1..8 while Analyze(config) walks 0..8.
        var desc = MotelyJamlyzer.CreateRiderDesc(config, analyzed.Add, eventRolls: 5);

        using var search = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedAnalyzeProvider(desc)
            .WithScoredResultCallback(row => scored.Add(new(row.Seed, row.Score, row.Tallies)))
            .WithSeedList(Seeds)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .CreateSearch();
        search.Start();
        search.AwaitCompletion();

        Assert.Equal(Seeds.Length, scored.Count);
        // Same seeds, same order: each find's breakdown follows the find.
        Assert.Equal(scored.Select(s => s.Seed), analyzed.Select(a => a.Seed));

        foreach (var a in analyzed)
        {
            var s = scored.Single(x => x.Seed == a.Seed);
            Assert.Equal(s.Score, a.Score);
            Assert.Equal(s.Tally, a.Tally);

            var standalone = MotelyJamlyzer.Analyze(Config(a.Seed), eventRolls: 5)[0];
            Assert.Equal(standalone.Score, a.Score);
            Assert.Equal(standalone.Tally, a.Tally);
            AssertSameBreakdown(standalone, a);
        }
    }

    [Fact]
    public void RidesUnscoredSearch_ScoreZeroTallyNull_ZeroRollsIsSummaryOnly()
    {
        var analyzed = new List<MotelyJamlyzerSeedResult>();
        using var search = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithSeedAnalyzeProvider(
                new MotelyJamlyzerRiderDesc(MotelyJamlyzer.AllAntes, analyzed.Add, eventRolls: 0)
            )
            .WithSeedList(Seeds)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .CreateSearch();
        search.Start();
        search.AwaitCompletion();

        Assert.Equal(Seeds.Order(), analyzed.Select(a => a.Seed).Order());
        Assert.All(
            analyzed,
            a =>
            {
                Assert.Equal(0, a.Score);
                Assert.Null(a.Tally);
                Assert.Equal(9, a.Antes.Count);
                // eventRolls 0: the per-ante summary is all there, every roll queue is empty.
                Assert.Equal(15, a.Antes[1].ShopItems.Count);
                Assert.Equal(4, a.Antes[1].Packs.Count);
                Assert.Empty(a.Antes[1].Pulls.JudgementJokers);
                Assert.Empty(a.Antes[1].ShopStreams.ShopJokers);
                Assert.Empty(a.Events.Misprint);
                Assert.Equal(0, a.StreamStates.RollOffset);
            }
        );
    }

    [Fact]
    public void AutoCutoff_AnalyzeProviderSeesExactlyTheReportedSeeds()
    {
        // One report batch of eight high scorers teaches the clamp 'Z'; the next batch's '1' seeds
        // are scored but dropped before the scored callback — and must be dropped from analysis
        // too, or the host gets breakdowns for finds it was never told about.
        string[] seeds =
        [
            .. Enumerable.Range(0, 8).Select(i => $"ZZZZZZZ{(char)('A' + i)}"),
            .. Enumerable.Range(0, 8).Select(i => $"1111111{(char)('A' + i)}"),
        ];
        var reported = new List<MotelySeedScore>();
        var desc = new RecordingAnalyzeDesc();

        using var search = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithSeedScoreProvider(new FirstCharScoreDesc())
            .WithSeedAnalyzeProvider(desc)
            .WithScoredResultCallback(row => reported.Add(new(row.Seed, row.Score, row.Tallies)))
            .WithAutoScoreCutoff(true)
            .WithSeedList(seeds)
            .WithProviderBatchSeedCount(8)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .CreateSearch();
        search.Start();
        search.AwaitCompletion();

        // The clamp has to have bitten, or this proves nothing.
        Assert.Equal(8, reported.Count);
        Assert.All(reported, r => Assert.StartsWith("Z", r.Seed));

        Assert.Equal(reported.Select(r => r.Seed), desc.Seeds);
        Assert.Equal(reported.Select(r => r.Score), desc.Scores);
    }

    private static void AssertSameBreakdown(
        MotelyJamlyzerSeedResult expected,
        MotelyJamlyzerSeedResult actual
    )
    {
        Assert.Equal(expected.Seed, actual.Seed);
        Assert.Equal(expected.Antes.Count, actual.Antes.Count);
        for (int i = 0; i < expected.Antes.Count; i++)
        {
            var e = expected.Antes[i];
            var a = actual.Antes[i];
            Assert.Equal(e.Ante, a.Ante);
            Assert.Equal(e.Boss, a.Boss);
            Assert.Equal(e.Voucher, a.Voucher);
            Assert.Equal(e.SmallBlindTag, a.SmallBlindTag);
            Assert.Equal(e.BigBlindTag, a.BigBlindTag);
            Assert.Equal<IEnumerable<MotelyItem>>(e.ShopItems, a.ShopItems);
            Assert.Equal(e.Packs.Select(p => p.Pack), a.Packs.Select(p => p.Pack));
            for (int p = 0; p < e.Packs.Count; p++)
                Assert.Equal<IEnumerable<MotelyItem>>(e.Packs[p].Items, a.Packs[p].Items);
            Assert.Equal<IEnumerable<MotelyItem>>(e.Pulls.EmperorTarots, a.Pulls.EmperorTarots);
            Assert.Equal<IEnumerable<MotelyVoucher>>(
                e.Pulls.VoucherSequence,
                a.Pulls.VoucherSequence
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                e.ShopStreams.ShopJokers,
                a.ShopStreams.ShopJokers
            );
        }
        Assert.Equal<IEnumerable<int>>(expected.Events.Misprint, actual.Events.Misprint);
        Assert.Equal<IEnumerable<MotelyItemEdition>>(
            expected.Events.WheelOfFortune,
            actual.Events.WheelOfFortune
        );
        Assert.Equal(expected.StreamStates, actual.StreamStates);
        Assert.Equal(expected.ErraticDeck, actual.ErraticDeck);
    }

    /// <summary>Score = the seed's first character, so a batch of Z-seeds teaches the clamp a high bar.</summary>
    private sealed class FirstCharScoreDesc : IMotelySeedScoreDesc<FirstCharScoreDesc.Provider>
    {
        public Provider CreateScoreProvider(ref MotelyFilterCreationContext ctx) => new();

        public readonly struct Provider : IMotelySeedScoreProvider
        {
            public VectorMask Score(
                ref MotelyVectorSearchContext ctx,
                MotelyScoredSeedResult[] buffer,
                VectorMask baseFilterMask,
                int scoreThreshold = 0
            ) =>
                ctx.SearchIndividualSeeds(
                    baseFilterMask,
                    single =>
                    {
                        string seed = single.GetSeed();
                        buffer[single.VectorLane].Reset(seed, seed[0]);
                        return 1;
                    }
                );
        }
    }

    /// <summary>Records the seeds and lane-aligned scores the engine hands the analyze provider.</summary>
    private sealed class RecordingAnalyzeDesc
        : IMotelySeedAnalyzeDesc<RecordingAnalyzeDesc.Provider>
    {
        public readonly List<string> Seeds = [];
        public readonly List<int> Scores = [];

        public Provider CreateAnalyzeProvider(ref MotelyFilterCreationContext ctx) => new(this);

        public readonly struct Provider(RecordingAnalyzeDesc owner) : IMotelySeedAnalyzeProvider
        {
            public void Analyze(
                ref MotelyVectorSearchContext ctx,
                VectorMask reportedMask,
                MotelyScoredSeedResult[]? scores
            )
            {
                for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
                {
                    if (!reportedMask[lane])
                        continue;
                    string seed = ctx.GetSeed(lane);
                    owner.Seeds.Add(seed);
                    // Lane-aligned: this lane's row is this seed's row.
                    Assert.Equal(seed, scores![lane].Seed);
                    owner.Scores.Add(scores[lane].Score);
                }
            }
        }
    }
}
