using Motely.SeedProviders;
using Xunit;
using Xunit.Abstractions;

namespace Motely.Tests;

public class ImmolateCavendishPortTests(ITestOutputHelper output)
{
    private const string GateJaml = """
        name: cavendish-gate
        deck: Red
        stake: White
        must:
          - joker: GrosMichel
            antes: [1]
        """;

    private static bool SawInShopOrPacks(MotelySingleSearchContext ctx, MotelyItemType want)
    {
        var shop = ctx.CreateShopItemStream(1);
        var packs = ctx.CreateBoosterPackStream(1);
        bool found = false;

        for (int i = 0; i < 2; i++)
            if (ctx.GetNextShopItem(ref shop).Type == want)
                found = true;

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

        Assert.All(hits, seed => Assert.False(string.IsNullOrWhiteSpace(seed)));
        Assert.True(search.TotalSeedsSearched > 0);
    }
}
