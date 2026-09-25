namespace Motely.Filters.Native;

public struct NegativeCopyFilterDesc()
    : IMotelySeedFilterDesc<NegativeCopyFilterDesc.NegativeCopyFilter>
{
    public readonly NegativeCopyFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        for (int ante = 1; ante <= 8; ante++)
        {
            ctx.CacheBoosterPackStream(ante);
        }
        return new NegativeCopyFilter();
    }

    public struct NegativeCopyFilter() : IMotelySeedFilter
    {
        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            VectorMask hasPotential = VectorMask.NoBitsSet;

            for (int ante = 1; ante <= 8; ante++)
            {
                var shopStream = searchContext.CreateShopItemStream(
                    ante,
                    MotelyShopStreamFlags.ExcludeTarots | MotelyShopStreamFlags.ExcludePlanets,
                    MotelyJokerStreamFlags.Default
                );

                int shopItems = ante switch
                {
                    1 => 4,
                    2 => 12,
                    3 => 25,
                    _ => 35,
                };

                for (int i = 0; i < shopItems; i++)
                {
                    var shopItem = searchContext.GetNextShopItem(ref shopStream);
                    hasPotential |= VectorEnum256.Equals(shopItem.Type, MotelyItemType.Showman);
                }

                var boosterPackStream = searchContext.CreateBoosterPackStream(
                    ante,
                    ante > 1,
                    false
                );
                var buffoonStream = searchContext.CreateBuffoonPackJokerStream(ante);

                int maxboosterPacks = ante == 1 ? 4 : 6;
                for (int i = 0; i < maxboosterPacks; i++)
                {
                    var pack = searchContext.GetNextBoosterPack(ref boosterPackStream);

                    VectorMask isBuffoonPack = VectorEnum256.Equals(
                        pack.GetPackType(),
                        MotelyBoosterPackType.Buffoon
                    );
                    if (isBuffoonPack.IsPartiallyTrue())
                    {
                        VectorMask isNormalSize = VectorEnum256.Equals(
                            pack.GetPackSize(),
                            MotelyBoosterPackSize.Normal
                        );
                        if ((isBuffoonPack & isNormalSize).IsPartiallyTrue())
                        {
                            var contents = searchContext.GetNextBuffoonPackContents(
                                ref buffoonStream,
                                MotelyBoosterPackSize.Normal
                            );
                            for (int j = 0; j < contents.Length; j++)
                            {
                                hasPotential |=
                                    VectorEnum256.Equals(contents[j].Type, MotelyItemType.Showman)
                                    & isBuffoonPack
                                    & isNormalSize;
                            }
                        }

                        VectorMask isJumboSize = VectorEnum256.Equals(
                            pack.GetPackSize(),
                            MotelyBoosterPackSize.Jumbo
                        );
                        if ((isBuffoonPack & isJumboSize).IsPartiallyTrue())
                        {
                            var contents = searchContext.GetNextBuffoonPackContents(
                                ref buffoonStream,
                                MotelyBoosterPackSize.Jumbo
                            );
                            for (int j = 0; j < contents.Length; j++)
                            {
                                hasPotential |=
                                    VectorEnum256.Equals(contents[j].Type, MotelyItemType.Showman)
                                    & isBuffoonPack
                                    & isJumboSize;
                            }
                        }

                        VectorMask isMegaSize = VectorEnum256.Equals(
                            pack.GetPackSize(),
                            MotelyBoosterPackSize.Mega
                        );
                        if ((isBuffoonPack & isMegaSize).IsPartiallyTrue())
                        {
                            var contents = searchContext.GetNextBuffoonPackContents(
                                ref buffoonStream,
                                MotelyBoosterPackSize.Mega
                            );
                            for (int j = 0; j < contents.Length; j++)
                            {
                                hasPotential |=
                                    VectorEnum256.Equals(contents[j].Type, MotelyItemType.Showman)
                                    & isBuffoonPack
                                    & isMegaSize;
                            }
                        }
                    }
                }
            }

            if (hasPotential.IsAllFalse())
                return VectorMask.NoBitsSet;

            return searchContext.SearchIndividualSeeds(
                hasPotential,
                (MotelySingleSearchContext ctx) =>
                {
                    int blueprintCount = 0;
                    int brainstormCount = 0;
                    int invisibleCount = 0;
                    int showmanCount = 0;
                    int negativeShowmanCount = 0;
                    int negativeBlueprint = 0;
                    int negativeBrainstorm = 0;
                    int negativeInvisible = 0;

                    for (int ante = 1; ante <= 8; ante++)
                    {
                        var shopStream = ctx.CreateShopItemStream(
                            ante,
                            MotelyShopStreamFlags.ExcludeTarots
                                | MotelyShopStreamFlags.ExcludePlanets,
                            MotelyJokerStreamFlags.Default
                        );

                        int shopItems = ante switch
                        {
                            1 => 4,
                            2 => 12,
                            3 => 25,
                            4 => 35,
                            5 => 45,
                            6 => 55,
                            7 => 65,
                            8 => 75,
                            _ => 25,
                        };

                        for (int i = 0; i < shopItems; i++)
                        {
                            var shopItem = ctx.GetNextShopItem(ref shopStream);

                            if (shopItem.Type == MotelyItemType.Showman)
                                showmanCount++;

                            if (shopItem.Type == MotelyItemType.Blueprint)
                            {
                                blueprintCount++;
                                if (shopItem.Edition == MotelyItemEdition.Negative)
                                    negativeBlueprint++;
                            }

                            if (shopItem.Type == MotelyItemType.Brainstorm)
                            {
                                brainstormCount++;
                                if (shopItem.Edition == MotelyItemEdition.Negative)
                                    negativeBrainstorm++;
                            }

                            if (shopItem.Type == MotelyItemType.InvisibleJoker)
                            {
                                invisibleCount++;
                                if (shopItem.Edition == MotelyItemEdition.Negative)
                                    negativeInvisible++;
                            }
                        }

                        var boosterPackStream = ctx.CreateBoosterPackStream(ante, ante > 1, false);
                        var buffoonStream = ctx.CreateBuffoonPackJokerStream(ante);

                        int maxboosterPacks = ante == 1 ? 4 : 6;
                        for (int i = 0; i < maxboosterPacks; i++)
                        {
                            var pack = ctx.GetNextBoosterPack(ref boosterPackStream);

                            if (pack.GetPackType() == MotelyBoosterPackType.Buffoon)
                            {
                                var contents = ctx.GetNextBuffoonPackContents(
                                    ref buffoonStream,
                                    pack.GetPackSize()
                                );

                                for (int j = 0; j < contents.Length; j++)
                                {
                                    var item = contents[j];

                                    if (item.Type == MotelyItemType.Showman)
                                    {
                                        if (item.Edition == MotelyItemEdition.Negative)
                                            negativeShowmanCount++;
                                        else
                                            showmanCount++;
                                    }
                                    else if (item.Type == MotelyItemType.Blueprint)
                                    {
                                        if (item.Edition == MotelyItemEdition.Negative)
                                            negativeBlueprint++;
                                        else
                                            blueprintCount++;
                                    }
                                    else if (item.Type == MotelyItemType.Brainstorm)
                                    {
                                        if (item.Edition == MotelyItemEdition.Negative)
                                            negativeBrainstorm++;
                                        else
                                            brainstormCount++;
                                    }
                                    else if (item.Type == MotelyItemType.InvisibleJoker)
                                    {
                                        if (item.Edition == MotelyItemEdition.Negative)
                                            negativeInvisible++;
                                        else
                                            invisibleCount++;
                                    }
                                }
                            }
                        }
                    }

                    int totalCopyJokers = blueprintCount + brainstormCount + invisibleCount;
                    int totalNegatives =
                        negativeBlueprint
                        + negativeBrainstorm
                        + negativeInvisible
                        + negativeShowmanCount;
                    int showmanScore = Math.Min(showmanCount, 1);
                    int endScore = Math.Min(totalCopyJokers, 6) + totalNegatives;

                    return (endScore >= 5) ? 1 : 0;
                }
            );
        }
    }
}
