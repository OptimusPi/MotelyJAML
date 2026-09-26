using Motely.Analysis;
using Motely.Filters.Jaml;

namespace Motely.Tests;

public sealed class JAMLyzerUnitTests
{
    private static JamlConfig SeedConfig(
        string seed,
        MotelyDeck deck = MotelyDeck.Red,
        MotelyStake stake = MotelyStake.White
    )
    {
        var config = JamlConfigLoader.FromJaml("seeds: []");
        config.Seeds.Add(seed);
        config.Deck = deck;
        config.Stake = stake;
        return config;
    }

    [Theory]
    [InlineData("UNITTEST")]
    [InlineData("ALEEB")]
    [InlineData("1234567")]
    public void Analyze_ReturnsSeedWithNineAntes(string seed)
    {
        var results = MotelyJamlyzer.Analyze(SeedConfig(seed));
        Assert.Single(results);
        Assert.Equal(seed, results[0].Seed);
        Assert.Equal(9, results[0].Antes.Count);
    }

    [Fact]
    public void Analyze_ScopedAnteZeroEmitsAnteZeroRow()
    {
        var config = JamlConfigLoader.FromJaml(
            "must:\n  - legendaryJoker: Perkeo\n    antes: [0, 1]\nseeds: [UNITTEST]"
        );
        var results = MotelyJamlyzer.Analyze(config);
        Assert.Single(results);
        Assert.Equal(2, results[0].Antes.Count);
        Assert.Equal(0, results[0].Antes[0].Ante);
        Assert.Equal(1, results[0].Antes[1].Ante);
        Assert.Equal(4, results[0].Antes[0].Packs.Count);
        Assert.Equal(15, results[0].Antes[0].ShopItems.Count);
    }

    [Fact]
    public void Analyze_AnteOneHasFourPacks()
    {
        var results = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"));
        Assert.Equal(4, results[0].Antes[0].Packs.Count);
        Assert.Equal(4, results[0].Antes[1].Packs.Count);
    }

    [Fact]
    public void Analyze_AnteTwoPlusSixPacks()
    {
        var results = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"));
        for (int i = 2; i < 9; i++)
            Assert.Equal(6, results[0].Antes[i].Packs.Count);
    }

    [Fact]
    public void Analyze_AnteNumbersAreSequential()
    {
        var results = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"));
        for (int i = 0; i < 9; i++)
            Assert.Equal(i, results[0].Antes[i].Ante);
    }

    [Fact]
    public void Analyze_EventRollsLengthMatchesParameter()
    {
        const int rolls = 5;
        var results = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: rolls);
        var events = results[0].Events;
        Assert.Equal(rolls, events.LuckyMoney.Length);
        Assert.Equal(rolls, events.WheelOfFortune.Length);
        Assert.Equal(rolls, events.Misprint.Length);

        // Per-ante pulls + shop-source queues are also rolls-length (Emperor is 2 per use).
        var ante1 = results[0].Antes[1];
        Assert.Equal(0, results[0].Antes[0].Ante);
        Assert.Equal(rolls, ante1.Pulls.JudgementJokers.Count);
        Assert.Equal(rolls * 2, ante1.Pulls.EmperorTarots.Count);
        Assert.Equal(rolls, ante1.ShopStreams.ShopJokers.Count);
        Assert.Equal(rolls, ante1.ShopStreams.RareShopJokers.Count);
        Assert.Equal(rolls, ante1.ShopStreams.ShopTarots.Count);
        Assert.Equal(rolls, ante1.ShopStreams.ShopPlanets.Count);
        Assert.Equal(rolls, ante1.ShopStreams.ShopSpectrals.Count);
    }

    [Fact]
    public void Analyze_ResumeFromStateBag_ContinuesExactlyWhereItStopped()
    {
        // One uninterrupted window of 20.
        var full = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 20)[0];

        // First 10, then resume from the returned state bag for 10 more.
        var page1 = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 10)[0];
        var page2 = MotelyJamlyzer.Analyze(
            SeedConfig("UNITTEST"),
            page1.StreamStates,
            eventRolls: 10
        )[0];

        // page1 ++ page2 must reconstruct the full window exactly (no re-roll, no drift).
        Assert.Equal<IEnumerable<MotelyItemEdition>>(
            full.Events.WheelOfFortune,
            page1.Events.WheelOfFortune.Concat(page2.Events.WheelOfFortune)
        );
        Assert.Equal<IEnumerable<int>>(
            full.Events.Misprint,
            page1.Events.Misprint.Concat(page2.Events.Misprint)
        );
        Assert.Equal<IEnumerable<bool>>(
            full.Events.LuckyMoney,
            page1.Events.LuckyMoney.Concat(page2.Events.LuckyMoney)
        );

        // And the stitched state must land on the same end-state as the full window.
        Assert.Equal(full.StreamStates, page2.StreamStates);

        // Composite (pulls/shop) streams resume by offset-replay — gate the resample-backed ones
        // (Emperor, vouchers) and a shop stream, per ante. These are what would diverge silently
        // if offset-replay were wrong.
        for (int a = 0; a < full.Antes.Count; a++)
        {
            var fa = full.Antes[a];
            var p1 = page1.Antes[a];
            var p2 = page2.Antes[a];

            // Every pulls member.
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.JudgementJokers,
                p1.Pulls.JudgementJokers.Concat(p2.Pulls.JudgementJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.WraithJokers,
                p1.Pulls.WraithJokers.Concat(p2.Pulls.WraithJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.EmperorTarots,
                p1.Pulls.EmperorTarots.Concat(p2.Pulls.EmperorTarots)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.PurpleSealTarots,
                p1.Pulls.PurpleSealTarots.Concat(p2.Pulls.PurpleSealTarots)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.SixthSenseSpectrals,
                p1.Pulls.SixthSenseSpectrals.Concat(p2.Pulls.SixthSenseSpectrals)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.SeanceSpectrals,
                p1.Pulls.SeanceSpectrals.Concat(p2.Pulls.SeanceSpectrals)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.RiffRaffJokers,
                p1.Pulls.RiffRaffJokers.Concat(p2.Pulls.RiffRaffJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.RareTagJokers,
                p1.Pulls.RareTagJokers.Concat(p2.Pulls.RareTagJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.UncommonTagJokers,
                p1.Pulls.UncommonTagJokers.Concat(p2.Pulls.UncommonTagJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.Pulls.LegendaryJokers,
                p1.Pulls.LegendaryJokers.Concat(p2.Pulls.LegendaryJokers)
            );
            Assert.Equal<IEnumerable<MotelyVoucher>>(
                fa.Pulls.VoucherSequence,
                p1.Pulls.VoucherSequence.Concat(p2.Pulls.VoucherSequence)
            );

            // Every shop-source member.
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.ShopStreams.ShopJokers,
                p1.ShopStreams.ShopJokers.Concat(p2.ShopStreams.ShopJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.ShopStreams.CommonShopJokers,
                p1.ShopStreams.CommonShopJokers.Concat(p2.ShopStreams.CommonShopJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.ShopStreams.UncommonShopJokers,
                p1.ShopStreams.UncommonShopJokers.Concat(p2.ShopStreams.UncommonShopJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.ShopStreams.RareShopJokers,
                p1.ShopStreams.RareShopJokers.Concat(p2.ShopStreams.RareShopJokers)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.ShopStreams.ShopTarots,
                p1.ShopStreams.ShopTarots.Concat(p2.ShopStreams.ShopTarots)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.ShopStreams.ShopPlanets,
                p1.ShopStreams.ShopPlanets.Concat(p2.ShopStreams.ShopPlanets)
            );
            Assert.Equal<IEnumerable<MotelyItem>>(
                fa.ShopStreams.ShopSpectrals,
                p1.ShopStreams.ShopSpectrals.Concat(p2.ShopStreams.ShopSpectrals)
            );
        }
    }

    [Fact]
    public void Analyze_ChainedResume_ThreeUnequalPagesReconstructFullWindow()
    {
        // 5 + 8 + 7 = 20, three different page sizes chained through the state bag.
        var full = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 20)[0];
        var a = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 5)[0];
        var b = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), a.StreamStates, eventRolls: 8)[0];
        var c = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), b.StreamStates, eventRolls: 7)[0];

        Assert.Equal(20, full.StreamStates.RollOffset);
        Assert.Equal(5, a.StreamStates.RollOffset);
        Assert.Equal(13, b.StreamStates.RollOffset);
        Assert.Equal(20, c.StreamStates.RollOffset);
        Assert.Equal(full.StreamStates, c.StreamStates);

        Assert.Equal<IEnumerable<int>>(
            full.Events.Misprint,
            a.Events.Misprint.Concat(b.Events.Misprint).Concat(c.Events.Misprint)
        );
        Assert.Equal<IEnumerable<MotelyItem>>(
            full.Antes[0].Pulls.EmperorTarots,
            a.Antes[0]
                .Pulls.EmperorTarots.Concat(b.Antes[0].Pulls.EmperorTarots)
                .Concat(c.Antes[0].Pulls.EmperorTarots)
        );
        Assert.Equal<IEnumerable<MotelyItem>>(
            full.Antes[7].ShopStreams.ShopPlanets,
            a.Antes[7]
                .ShopStreams.ShopPlanets.Concat(b.Antes[7].ShopStreams.ShopPlanets)
                .Concat(c.Antes[7].ShopStreams.ShopPlanets)
        );
    }

    [Fact]
    public void Analyze_MultipleSeeds_ReturnsOneResultEach()
    {
        var config = JamlConfigLoader.FromJaml("seeds: []");
        config.Seeds.Add("UNITTEST");
        config.Seeds.Add("ALEEB");
        config.Seeds.Add("1234567");

        var results = MotelyJamlyzer.Analyze(config);
        Assert.Equal(3, results.Count);
        Assert.Equal("UNITTEST", results[0].Seed);
        Assert.Equal("ALEEB", results[1].Seed);
        Assert.Equal("1234567", results[2].Seed);
    }

    [Fact]
    public void Analyze_MultiSeedResume_EachSeedScrollsIndependently()
    {
        string[] seeds = ["UNITTEST", "ALEEB", "1234567"];

        static JamlConfig Config(string[] seeds)
        {
            var c = JamlConfigLoader.FromJaml("seeds: []");
            foreach (var s in seeds)
                c.Seeds.Add(s);
            return c;
        }

        // Each seed's uninterrupted 20-roll window, keyed by seed.
        var full = MotelyJamlyzer.Analyze(Config(seeds), eventRolls: 20).ToDictionary(r => r.Seed);

        // Page all three seeds together: 10 rolls, then resume each from ITS OWN bag for 10 more.
        var page1 = MotelyJamlyzer.Analyze(Config(seeds), eventRolls: 10);
        var resume = page1.ToDictionary(r => r.Seed, r => r.StreamStates);
        var page2 = MotelyJamlyzer.Analyze(Config(seeds), resume, eventRolls: 10);

        // Each seed's stitched end-state equals that seed's full-window end-state — bags stay per-seed.
        foreach (var p2 in page2)
        {
            var p1 = page1.Single(r => r.Seed == p2.Seed);
            var f = full[p2.Seed];

            Assert.Equal(f.StreamStates, p2.StreamStates);
            Assert.Equal<IEnumerable<MotelyItemEdition>>(
                f.Events.WheelOfFortune,
                p1.Events.WheelOfFortune.Concat(p2.Events.WheelOfFortune)
            );
            Assert.Equal<IEnumerable<int>>(
                f.Events.Misprint,
                p1.Events.Misprint.Concat(p2.Events.Misprint)
            );
        }
    }

    [Fact]
    public void Analyze_MultiSeedResume_SeedAbsentFromMapStartsFresh()
    {
        var config = JamlConfigLoader.FromJaml("seeds: []");
        config.Seeds.Add("UNITTEST");
        config.Seeds.Add("ALEEB");

        // Map carries only UNITTEST's bag; ALEEB is absent → must start fresh at offset 0, not throw.
        var seeded = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 10)[0];
        var fresh = MotelyJamlyzer.Analyze(SeedConfig("ALEEB"), eventRolls: 10)[0];

        var resume = new Dictionary<string, MotelyJamlyzerStreamStates>
        {
            [seeded.Seed] = seeded.StreamStates,
        };
        var results = MotelyJamlyzer.Analyze(config, resume, eventRolls: 10);

        var aleeb = results.Single(r => r.Seed == "ALEEB");
        Assert.Equal(10, aleeb.StreamStates.RollOffset); // fresh window, not resumed
        Assert.Equal(fresh.StreamStates, aleeb.StreamStates);

        var unittest = results.Single(r => r.Seed == "UNITTEST");
        Assert.Equal(20, unittest.StreamStates.RollOffset); // resumed: 10 + 10
    }

    [Fact]
    public void Analyze_GhostDeck_Runs()
    {
        var results = MotelyJamlyzer.Analyze(
            SeedConfig("KK1XD111", MotelyDeck.Ghost, MotelyStake.Black)
        );
        Assert.Single(results);
        Assert.Equal(9, results[0].Antes.Count);
    }

    [Fact]
    public void ComputeAntes_NoAnteClause_ReturnsZeroThroughEight()
    {
        var config = JamlConfigLoader.FromJaml("seeds: []");
        var antes = MotelyJamlyzer.ComputeAntes(config);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], antes);
    }

    [Fact]
    public void Analyze_ShopItems_PagedAndResumed_ReconstructsContinuousStream()
    {
        var full = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), shopSlots: 50)[0];
        var a = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), shopSlots: 25)[0];
        var b = MotelyJamlyzer.Analyze(
            SeedConfig("UNITTEST"),
            a.StreamStates,
            shopSlots: 25
        )[0];

        Assert.Equal(50, full.Antes[1].ShopItems.Count);
        Assert.Equal(25, a.Antes[1].ShopItems.Count);
        Assert.Equal(25, b.Antes[1].ShopItems.Count);

        Assert.Equal<IEnumerable<MotelyItem>>(
            full.Antes[1].ShopItems,
            a.Antes[1].ShopItems.Concat(b.Antes[1].ShopItems)
        );
    }

    /// <summary>
    /// Shop depth is its own dial. It used to ride <c>eventRolls</c> behind a
    /// <c>!= 20</c> guard, which made 20 a dead value -- asking for exactly 20 shop slots
    /// silently returned the ante-1 default of 15 -- and made a deep shop allocate an equally
    /// deep array for all eighteen pull and shop-source queues.
    /// </summary>
    [Fact]
    public void Analyze_ShopSlots_IsIndependentOfEventRolls()
    {
        Assert.Equal(
            20,
            MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), shopSlots: 20)[0].Antes[1]
                .ShopItems.Count
        );

        // eventRolls sizes the roll queues and leaves the shop on its default.
        var rolls = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 200)[0];
        Assert.Equal(15, rolls.Antes[1].ShopItems.Count);
        Assert.Equal(200, rolls.Antes[1].ShopStreams.ShopTarots.Count);

        // ...and shopSlots sizes the shop and leaves the roll queues on theirs.
        var shop = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), shopSlots: 200)[0];
        Assert.Equal(200, shop.Antes[1].ShopItems.Count);
        Assert.Equal(20, shop.Antes[1].ShopStreams.ShopTarots.Count);
    }

    /// <summary>A deep ante-1 shop is one call and the items keep coming: the stream never dries up.</summary>
    [Fact]
    public void Analyze_ShopSlots_WalksPastTheAnteOneDefault()
    {
        var deep = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), shopSlots: 500)[0];
        Assert.Equal(500, deep.Antes[1].ShopItems.Count);

        // The first 15 are still exactly what the default walk returns -- deepening the walk
        // extends the queue, it does not shift it.
        var shallow = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"))[0];
        Assert.Equal<IEnumerable<MotelyItem>>(
            shallow.Antes[1].ShopItems,
            deep.Antes[1].ShopItems.Take(15)
        );
    }
}
