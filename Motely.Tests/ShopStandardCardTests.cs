namespace Motely.Tests;

/// <summary>
/// Proof for Magic Trick shop playing cards (b9950e0b). Scalar shop stream used to return
/// <see cref="MotelyItemType.NotImplemented"/> for the standard-card branch; it must emit bare
/// <see cref="MotelyItemTypeCategory.Standardcard"/> items when Magic Trick is active.
/// </summary>
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

    /// <summary>
    /// ALEEB's ante-1 voucher is Magic Trick (seeds/ALEEB.verified.txt). With that voucher
    /// active on the run state, the shop stream must yield at least one bare playing card and
    /// never <see cref="MotelyItemType.NotImplemented"/> on the standard-card branch.
    /// </summary>
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

                    // The old FEAT hole: Magic Trick branch returned NotImplemented.
                    Assert.NotEqual(MotelyItemType.NotImplemented, item.Type);

                    if (item.TypeCategory == MotelyItemTypeCategory.Standardcard)
                    {
                        // create_card('Base', ..., 'sho'): bare rank+suit, no cosmetics.
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

    /// <summary>
    /// Without Magic Trick the standard-card rate is 0 — shop slots stay jokers/tarots/planets.
    /// </summary>
    [Fact]
    public void WithoutMagicTrick_ShopStreamHasNoStandardCards()
    {
        var (matching, _) = RunJimmolate(
            ["ALEEB"],
            static (MotelySingleSearchContext ctx) =>
            {
                // Default run state: no Magic Trick.
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

    /// <summary>
    /// ExcludeStandardCards must surface the sentinel instead of rolling a card.
    /// </summary>
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
                        return 0; // should never materialize under ExcludeStandardCards
                }

                return 0;
            }
        );

        Assert.Equal("ALEEB", Assert.Single(matched));
        Assert.Equal(1L, matching);
    }
}
