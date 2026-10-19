using Motely.Analysis;

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
        Assert.Equal(6, results[0].Antes[0].Packs.Count);
        Assert.Equal(15, results[0].Antes[0].ShopItems.Count);
    }

    [Fact]
    public void Analyze_AnteOneHasFourPacks_AnteZeroSix()
    {
        // Ante 1's first shop opens after the small blind; ante 0 runs all three shops once
        // Hieroglyph and Petroglyph ease the run back to it, the six packs a clause searches.
        var results = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"));
        Assert.Equal(6, results[0].Antes[0].Packs.Count);
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

    [Theory]
    [InlineData("C3DW94B8", 1)]
    [InlineData("3MLDLU2W", 2)]
    [InlineData("9KIXS1TL", 3)]
    public void Analyze_LegendaryOrder_ReadsTheSameSoulTheScorerFinds(string seed, int expectedSlot)
    {
        // These Red/White seeds each hold one ante-1 Soul at a known pack slot — the same seeds
        // LegendaryPackIndexTests pins the legendaryJoker clause against. The analyser's run-global
        // LegendaryOrder must find exactly that one Soul, at that slot.
        var order = MotelyJamlyzer.Analyze(SeedConfig(seed), eventRolls: 1)[0].LegendaryOrder;
        var entry = Assert.Single(order);
        Assert.Equal(1, entry.Ante);
        Assert.Equal(expectedSlot, entry.PackSlot);

        var legendaries = MotelyEnum<MotelyJokerLegendary>
            .Values.Select(l => (MotelyJoker)((int)MotelyJokerRarity.Legendary | (int)l));
        Assert.Contains(entry.Joker, legendaries);

        // The yielded joker as an item: the engine's own construction, edition carried, so a
        // consumer draws it like any shop or pack card.
        Assert.Equal(new MotelyItem(entry.Joker, entry.Edition), entry.Item);
        Assert.Equal(MotelyItemTypeCategory.Joker, entry.Item.TypeCategory);
        Assert.Equal(entry.Edition, entry.Item.Edition);
    }

    [Theory]
    // Ghost/White seeds with a Soul in ante 1 and another in ante 2. On the first three the raw
    // Joker4 draw for the second Soul repeats the first legendary; with that legendary held the
    // game resamples it. 3VX11111's draws differ, so nothing resamples.
    [InlineData("BHT11111", MotelyJoker.Triboulet, MotelyJoker.Canio)]
    [InlineData("D6L11111", MotelyJoker.Perkeo, MotelyJoker.Triboulet)]
    [InlineData("L4V11111", MotelyJoker.Yorick, MotelyJoker.Perkeo)]
    [InlineData("3VX11111", MotelyJoker.Triboulet, MotelyJoker.Canio)]
    public void Analyze_LegendaryOrder_ResamplesOffTheLegendariesAlreadyHeld(
        string seed,
        MotelyJoker first,
        MotelyJoker second
    )
    {
        var order = MotelyJamlyzer.Analyze(SeedConfig(seed, MotelyDeck.Ghost), eventRolls: 1)[0].LegendaryOrder;
        Assert.True(order.Count >= 2, $"{seed}: a Soul in ante 1 and another in ante 2");
        Assert.Equal([first, second], order.Take(2).Select(l => l.Joker));

        // The filter walks the same Souls the same way: soulOrder matches exactly this order.
        ProofSearch.MustMatchAll(
            $$"""
            name: soul-order
            deck: Ghost
            stake: White
            must:
              - soulOrder:
                  - { legendary: {{first}}, antes: [{{order[0].Ante}}] }
                  - { legendary: {{second}}, antes: [{{order[1].Ante}}] }
                maxAnte: {{order[1].Ante}}
            """,
            seed
        );
    }

    [Fact]
    public void Analyze_AnteZeroSoulStaysShutUnlessALegendaryClauseNamesAnteZero()
    {
        // 1YU11111 (Red/White) has a Soul in ante 0's first pack and one in ante 2's. Ante 0 is in
        // the run only after Hieroglyph or Petroglyph, so with no clause naming it the walk shows
        // ante 0's cards but leaves its Soul shut, and the ante-2 Soul is the first one opened:
        // the stream's first legendary, Triboulet. Opening ante 0 first made it read Yorick.
        var plain = MotelyJamlyzer.Analyze(SeedConfig("1YU11111"), eventRolls: 1)[0];
        Assert.Equal(0, plain.Antes[0].Ante);
        var only = Assert.Single(plain.LegendaryOrder);
        Assert.Equal((2, 0, MotelyJoker.Triboulet), (only.Ante, only.PackSlot, only.Joker));

        // A legendary clause naming ante 0 opens its Soul, first, as the clause itself does.
        var namesAnteZero = JamlConfigLoader.FromJaml("""
            deck: Red
            stake: White
            must:
              - legendaryJoker: Triboulet
                antes: [0, 1, 2]
                sources: { arcanaPacks: [0, 1, 2, 3, 4, 5], spectralPacks: [0, 1, 2, 3, 4, 5] }
            seeds: [1YU11111]
            """);
        Assert.Equal(
            [(0, MotelyJoker.Triboulet), (2, MotelyJoker.Yorick)],
            MotelyJamlyzer.Analyze(namesAnteZero, eventRolls: 1)[0].LegendaryOrder.Select(l => (l.Ante, l.Joker)));

        // The clauses agree with both readings: a clause on ante 2 opens that Soul first.
        const string head = "name: ante-zero\ndeck: Red\nstake: White\nmust:\n";
        ProofSearch.MustMatchAll(
            head + "  - legendaryJoker: Triboulet\n    antes: [2]\n"
                + "    sources: { arcanaPacks: [0, 1, 2, 3, 4, 5], spectralPacks: [0, 1, 2, 3, 4, 5] }\n",
            "1YU11111");
        ProofSearch.MustMatchAll(head + "  - soulOrder:\n      - { legendary: Triboulet, antes: [2] }\n", "1YU11111");
        ProofSearch.MustMatchNone(head + "  - soulOrder:\n      - { legendary: Yorick, antes: [2] }\n", "1YU11111");
        ProofSearch.MustMatchAll(
            head + "  - soulOrder:\n      - { legendary: Triboulet, antes: [0] }\n      - { legendary: Yorick, antes: [2] }\n",
            "1YU11111");
    }

    [Fact]
    public void Analyze_EachAnteCarriesItsPublishedShopDepthAndPackCount()
    {
        // DefaultShopSlots / PackCount are what a consumer sizes an ante with before it has a
        // result; they must be exactly what the result then carries.
        var result = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 1)[0];
        foreach (var ante in result.Antes)
        {
            Assert.Equal(MotelyJamlyzer.DefaultShopSlots(ante.Ante), ante.ShopItems.Count);
            Assert.Equal(MotelyJamlyzer.PackCount(ante.Ante), ante.Packs.Count);
        }
        Assert.Equal(6, MotelyJamlyzer.PackCount(0));
        Assert.Equal(4, MotelyJamlyzer.PackCount(1));
        Assert.Equal(6, MotelyJamlyzer.PackCount(2));
        Assert.Equal(15, MotelyJamlyzer.DefaultShopSlots(1));
        Assert.Equal(50, MotelyJamlyzer.DefaultShopSlots(2));
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

        // And the stitched state must land on the same end-state as the full window. The one
        // difference is the shop: two default-depth pages walked it twice, the full window once.
        Assert.Equal(full.StreamStates with { ShopDefaultWindows = 2 }, page2.StreamStates);

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

        // The legendary order is run-global (one Joker4 walk per seed), so every page reports the
        // whole sequence identically -- it is not sliced by the window like the per-ante queues.
        Assert.Equal(full.LegendaryOrder, page1.LegendaryOrder);
        Assert.Equal(full.LegendaryOrder, page2.LegendaryOrder);
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
        Assert.Equal(full.StreamStates with { ShopDefaultWindows = 3 }, c.StreamStates);

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

            Assert.Equal(f.StreamStates with { ShopDefaultWindows = 2 }, p2.StreamStates);
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
    /// Shop depth is its own dial, independent of <c>eventRolls</c>: exactly 20 shop slots
    /// gives 20, and a deep shop does not deepen the pull and shop-source queues.
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

    /// <summary>
    /// Analyze.SeedsResume promises the shop picks up exactly where the last window stopped. With
    /// shopSlots left at 0 it did not: the bag's ShopOffset never moved, so every resumed window
    /// re-read the same 15 / 50 items. Default windows, explicit windows and a mix of the two must
    /// all tile each ante's one continuous shop queue -- no item repeated, none skipped.
    /// </summary>
    [Fact]
    public void Analyze_ResumedShop_DefaultAndExplicitWindowsTileTheQueue()
    {
        var full = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 0, shopSlots: 200)[0];

        var a = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 0)[0]; // default depth
        var b = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), a.StreamStates, 0)[0]; // default again
        var c = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), b.StreamStates, 0, 7)[0]; // explicit 7
        var d = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), c.StreamStates, 0)[0]; // default again

        Assert.Equal(9, full.Antes.Count);
        for (int i = 0; i < full.Antes.Count; i++)
        {
            int ante = full.Antes[i].Ante;
            int depth = ante <= 1 ? 15 : 50;
            Assert.Equal(depth, a.Antes[i].ShopItems.Count);
            Assert.Equal(7, c.Antes[i].ShopItems.Count);

            var stitched = a.Antes[i]
                .ShopItems.Concat(b.Antes[i].ShopItems)
                .Concat(c.Antes[i].ShopItems)
                .Concat(d.Antes[i].ShopItems)
                .ToArray();
            Assert.Equal(3 * depth + 7, stitched.Length);
            Assert.Equal<IEnumerable<MotelyItem>>(
                full.Antes[i].ShopItems.Take(stitched.Length),
                stitched
            );
        }

        Assert.Equal(7, d.StreamStates.ShopOffset);
        Assert.Equal(3, d.StreamStates.ShopDefaultWindows);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(MotelyJamlyzer.MaxEventRolls + 1, 0)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(20, -1)]
    [InlineData(20, MotelyJamlyzer.MaxShopSlots + 1)]
    [InlineData(20, int.MaxValue)]
    public void Analyze_OutOfRangeWindow_ThrowsArgumentOutOfRange(int eventRolls, int shopSlots)
    {
        // JS can pass anything; the call must say what is wrong.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls, shopSlots)
        );
        // Checked up front, so a seedless config is no way around it.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml("seeds: []"), eventRolls, shopSlots)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MotelyJamlyzerRiderDesc(MotelyJamlyzer.AllAntes, _ => { }, eventRolls, shopSlots)
        );
    }

    [Fact]
    public void Analyze_AtTheCaps_IsAccepted()
    {
        var config = JamlConfigLoader.FromJaml(
            "must:\n  - voucher: Overstock\n    antes: [1]\nseeds: [UNITTEST]"
        );
        var r = MotelyJamlyzer.Analyze(
            config,
            MotelyJamlyzer.MaxEventRolls,
            MotelyJamlyzer.MaxShopSlots
        )[0];
        var ante1 = r.Antes.Single(a => a.Ante == 1);
        Assert.Equal(MotelyJamlyzer.MaxEventRolls, ante1.Pulls.JudgementJokers.Count);
        Assert.Equal(MotelyJamlyzer.MaxShopSlots, ante1.ShopItems.Count);
    }

    public static TheoryData<string, Func<MotelyJamlyzerStreamStates, MotelyJamlyzerStreamStates>> CorruptBags =>
        new()
        {
            { "negative RollOffset", s => s with { RollOffset = -5 } },
            { "RollOffset int.MaxValue", s => s with { RollOffset = int.MaxValue } },
            { "RollOffset past the cap", s => s with { RollOffset = MotelyJamlyzer.MaxEventRolls } },
            { "negative ShopOffset", s => s with { ShopOffset = -5 } },
            { "ShopOffset int.MaxValue", s => s with { ShopOffset = int.MaxValue } },
            { "negative ShopDefaultWindows", s => s with { ShopDefaultWindows = -1 } },
            { "ShopDefaultWindows int.MaxValue", s => s with { ShopDefaultWindows = int.MaxValue } },
            { "NaN state", s => s with { Misprint = double.NaN } },
            { "infinite state", s => s with { LuckyMoney = double.PositiveInfinity } },
            { "negative state", s => s with { Glass = -0.25 } },
            { "state above 1", s => s with { TheWheel = 1.5 } },
        };

    /// <summary>
    /// A bag crosses the JS boundary as a plain object. Before, a negative offset silently left the
    /// first slots of every queue as default(MotelyItem), int.MaxValue overflowed into an empty walk
    /// with a negative RollOffset handed back, 1e9 replayed for minutes, and NaN states rolled
    /// nonsense. Each must be refused with ArgumentOutOfRange naming resumeFrom.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorruptBags))]
    public void Analyze_ResumeFromCorruptBag_ThrowsArgumentOutOfRange(
        string _,
        Func<MotelyJamlyzerStreamStates, MotelyJamlyzerStreamStates> corrupt
    )
    {
        var good = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 10)[0].StreamStates;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), corrupt(good), eventRolls: 10)
        );
        Assert.Equal("resumeFrom", ex.ParamName);
    }

    [Fact]
    public void Analyze_ResumeWithNoSeeds_ReturnsNoRows()
    {
        var bag = MotelyJamlyzer.Analyze(SeedConfig("UNITTEST"), eventRolls: 10)[0].StreamStates;
        // Was ArgumentOutOfRangeException "Index was out of range" from config.Seeds[0].
        Assert.Empty(MotelyJamlyzer.Analyze(JamlConfigLoader.FromJaml("seeds: []"), bag, 10));
    }

    /// <summary>
    /// JAML antes run to 39, and the Jamlyzer redeems one voucher per ante. From ante 33 on every
    /// voucher is redeemed, and the voucher resample loop could never land on an unredeemed one:
    /// Analyze spun forever (in the browser, a frozen tab). Balatro's empty pool offers Blank.
    /// </summary>
    [Fact]
    public async Task Analyze_AntesPastTheVoucherPool_FinishesWithBlank()
    {
        var config = JamlConfigLoader.FromJaml(
            "must:\n  - voucher: Overstock\n    antes: [39]\nseeds: [UNITTEST]"
        );
        // Off the test thread with a deadline: a hang fails here instead of stalling the run.
        var result = await Task.Run(() => MotelyJamlyzer.Analyze(config, eventRolls: 3)[0])
            .WaitAsync(TimeSpan.FromSeconds(60));

        var antes = result.Antes.Where(a => a.Ante >= 1).ToList();
        Assert.Equal(39, antes.Count);
        // Antes 1..32 redeem each of the 32 vouchers exactly once...
        Assert.Equal(
            MotelyEnum<MotelyVoucher>.ValueCount,
            antes.Take(32).Select(a => a.Voucher).Distinct().Count()
        );
        // ...after which the pool is empty.
        Assert.All(antes.Skip(32), a => Assert.Equal(MotelyVoucher.Blank, a.Voucher));
        Assert.All(
            antes.Skip(32),
            a => Assert.All(a.Pulls.VoucherSequence, v => Assert.Equal(MotelyVoucher.Blank, v))
        );
    }

    /// <summary>
    /// Same JAML, same answer: repeated calls agree, and a batched multi-seed call (with scoring,
    /// on the Erratic deck) equals the same seeds analyzed one at a time.
    /// </summary>
    [Fact]
    public void Analyze_IsDeterministic_AndBatchedEqualsOneAtATime()
    {
        string[] seeds = ["UNITTEST", "ALEEB", "1234567", "KK1XD111", "11111111", "ZZZZZZZZ"];
        const string Jaml = """
            deck: Erratic
            must:
              - joker: []
                antes: [1, 2]
            should:
              - joker: Blueprint
                score: 3
              - tarotCard: TheFool
                score: 1
            seeds: []
            """;
        JamlConfig Config(params string[] s)
        {
            var c = JamlConfigLoader.FromJaml(Jaml);
            c.Seeds.AddRange(s);
            return c;
        }

        var batched = MotelyJamlyzer.Analyze(Config(seeds), eventRolls: 6, shopSlots: 9);
        var again = MotelyJamlyzer.Analyze(Config(seeds), eventRolls: 6, shopSlots: 9);
        var single = seeds
            .SelectMany(s => MotelyJamlyzer.Analyze(Config(s), eventRolls: 6, shopSlots: 9))
            .ToList();

        Assert.Equal(seeds, batched.Select(r => r.Seed));
        Assert.All(batched, r => Assert.Equal(52, r.ErraticDeck!.Length));
        Assert.Contains(batched, r => r.Tally is not null);
        Assert.Equal(batched.Select(Fingerprint), again.Select(Fingerprint));
        Assert.Equal(batched.Select(Fingerprint), single.Select(Fingerprint));
    }

    private static string Fingerprint(MotelyJamlyzerSeedResult r) =>
        System.Text.Json.JsonSerializer.Serialize(r);

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
