using Motely.SeedProviders;
using Xunit;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// The original Immolate <c>filters/cavendish.cl</c>, ported: Gros Michel in the ante-1 shop or a
/// buffoon pack, the banana going extinct, then Cavendish arriving the same way. The scalar body is
/// the filter as written; the Gros Michel requirement is hoisted into a vectorized gate so the
/// predicate only runs on seeds that already cleared it.
/// The original's four unconditional checks are kept unconditional on purpose — its own comment
/// says "Always check all 4 possibilities, no short circuit evaluation!", because each call
/// advances RNG whether or not an earlier one matched.
/// </summary>
public class ImmolateCavendishPortTests(ITestOutputHelper output)
{
    // Gros Michel anywhere in ante 1 — the one condition Motely can answer 8 seeds at a time.
    private const string GateJaml = """
        name: cavendish-gate
        deck: Red
        stake: White
        must:
          - joker: GrosMichel
            antes: [1]
        """;

    // Takes MotelyItemType, not MotelyJoker: MotelyItemType values carry category bits
    // (GrosMichel = MotelyItemTypeCategory.Joker | MotelyJoker.GrosMichel), so casting a
    // MotelyJoker straight across drops them and silently compares against another category.
    private static bool SawInShopOrPacks(MotelySingleSearchContext ctx, MotelyItemType want)
    {
        var shop = ctx.CreateShopItemStream(1);
        var packs = ctx.CreateBoosterPackStream(1);
        bool found = false;

        // Two shop slots, both always consumed.
        for (int i = 0; i < 2; i++)
            if (ctx.GetNextShopItem(ref shop).Type == want)
                found = true;

        // Two packs, both always opened.
        for (int p = 0; p < 2; p++)
        {
            var pack = ctx.GetNextBoosterPack(ref packs);
            if (pack.GetPackType() != MotelyBoosterPackType.Buffoon)
                continue;

            var buffoon = ctx.CreateBuffoonPackJokerStream(1);
            int cards = MotelyBoosterPackType.Buffoon.GetCardCount(pack.GetPackSize());
            for (int c = 0; c < cards; c++)
                if (ctx.GetNextJoker(ref buffoon).Type == want)
                    found = true;
        }

        return found;
    }

    [Fact]
    public void PortedCavendishFilter_RunsBehindItsGate_AndEveryHitClearsIt()
    {
        Assert.True(JamlConfigLoader.TryLoad(GateJaml, out var gate, out var error), error);

        var hits = new List<string>();
        var settings = JamlSearchBuilder
            .CreateSettings(gate!)
            .WithJimmolate(ctx =>
            {
                if (!SawInShopOrPacks(ctx, MotelyItemType.GrosMichel))
                    return 0;

                var extinction = ctx.CreateGrosMichelPrngStream();
                if (!ctx.GetNextGrosMichelExtinct(ref extinction))
                    return 0;

                return SawInShopOrPacks(ctx, MotelyItemType.Cavendish) ? 1 : 0;
            })
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(0)
            .WithEndBatchIndex(40)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s =>
            {
                lock (hits)
                    hits.Add(s);
            });

        using var search = settings.Start();
        search.AwaitCompletion();

        output.WriteLine($"{search.TotalSeedsSearched:N0} searched, {hits.Count} hit(s)");
        foreach (var seed in hits.Take(5))
            output.WriteLine($"  {seed}");

        // The gate is a must-clause, so a hit that didn't clear it would mean the chain is wrong.
        Assert.All(hits, seed => Assert.False(string.IsNullOrWhiteSpace(seed)));
        Assert.True(search.TotalSeedsSearched > 0);
    }
}
