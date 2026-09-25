namespace Motely.Tests;

public sealed class ShopStandardCardTests
{
    private static (long Matching, List<string> Matched) RunJimmolate(
        string[] seeds,
        MotelyIndividualSeedSearcher predicate
    )
    {
        var matched = new List<string>();
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(predicate)
            .WithSeedMatchCallback(seed =>
            {
                lock (matched)
                    matched.Add(seed);
            });

        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, matched);
    }

    [Fact]
    public void MagicTrickShop_Aleeb_YieldsBarePlayingCardsNotNotImplemented()
    {
        string[] seeds = ["PIROCKS", "ALEEB", "LOVEYAHB"];

        var (matching, matched) = RunJimmolate(
            seeds,
            static (MotelySingleSearchContext ctx) =>
            {
                if (ctx.GetAnteFirstVoucher(1) != MotelyVoucher.MagicTrick)
                    return 0;

                var runState = new MotelyRunState();
                runState.ActivateVoucher(MotelyVoucher.MagicTrick);

                var shop = ctx.CreateShopItemStream(1, runState);
                int standardCards = 0;

                for (int i = 0; i < 64; i++)
                {
                    var item = ctx.GetNextShopItem(ref shop);

                    Assert.NotEqual(MotelyItemType.NotImplemented, item.Type);

                    if (item.TypeCategory == MotelyItemTypeCategory.Standardcard)
                    {
                        Assert.Equal(MotelyItemEdition.None, item.Edition);
                        Assert.Equal(MotelyItemEnhancement.None, item.Enhancement);
                        Assert.Equal(MotelyItemSeal.None, item.Seal);
                        standardCards++;
                    }
                }

                return standardCards > 0 ? 1 : 0;
            }
        );

        Assert.Equal("ALEEB", Assert.Single(matched));
        Assert.Equal(1L, matching);
    }

    [Fact]
    public void WithoutMagicTrick_ShopStreamHasNoStandardCards()
    {
        var (matching, _) = RunJimmolate(
            ["ALEEB"],
            static (MotelySingleSearchContext ctx) =>
            {
                var shop = ctx.CreateShopItemStream(1);
                for (int i = 0; i < 64; i++)
                {
                    var item = ctx.GetNextShopItem(ref shop);
                    if (item.TypeCategory == MotelyItemTypeCategory.Standardcard)
                        return 0;
                    if (item.Type == MotelyItemType.NotImplemented)
                        return 0;
                }
                return 1;
            }
        );

        Assert.Equal(1L, matching);
    }

    [Fact]
    public void ExcludeStandardCards_ReturnsSentinelWhenMagicTrickWouldRollOne()
    {
        var (matching, matched) = RunJimmolate(
            ["ALEEB"],
            static (MotelySingleSearchContext ctx) =>
            {
                if (ctx.GetAnteFirstVoucher(1) != MotelyVoucher.MagicTrick)
                    return 0;

                var runState = new MotelyRunState();
                runState.ActivateVoucher(MotelyVoucher.MagicTrick);

                var shop = ctx.CreateShopItemStream(
                    1,
                    runState,
                    MotelyShopStreamFlags.ExcludeStandardCards
                );

                for (int i = 0; i < 128; i++)
                {
                    var item = ctx.GetNextShopItem(ref shop);
                    if (item.Type == MotelyItemType.StandardCardExcludedByStream)
                        return 1;
                    if (item.TypeCategory == MotelyItemTypeCategory.Standardcard)
                        return 0;
                }

                return 0;
            }
        );

        Assert.Equal("ALEEB", Assert.Single(matched));
        Assert.Equal(1L, matching);
    }
}
