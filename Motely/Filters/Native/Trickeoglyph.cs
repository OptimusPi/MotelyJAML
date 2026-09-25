namespace Motely.Filters.Native;

public struct TrickeoglyphFilterDesc()
    : IMotelySeedFilterDesc<TrickeoglyphFilterDesc.TrickeoglyphFilter>
{
    public readonly TrickeoglyphFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        ctx.CacheAnteFirstVoucher(1);
        ctx.CacheAnteFirstVoucher(2);
        ctx.CacheAnteFirstVoucher(3);
        ctx.CacheAnteFirstVoucher(4);
        return new TrickeoglyphFilter();
    }

    public struct TrickeoglyphFilter() : IMotelySeedFilter
    {
        public static bool CheckLegendaryJokerInAnte(
            int ante,
            MotelyItemType targetJoker,
            ref MotelySingleSearchContext searchContext,
            MotelyItemEdition? requiredEdition = null
        )
        {
            var soulStream = searchContext.CreateLegendaryJokerStream(ante);
            var legendaryJoker = searchContext.GetNextJoker(ref soulStream);
            if (legendaryJoker.Type != targetJoker)
                return false;

            if (requiredEdition.HasValue)
            {
                if (legendaryJoker.Edition != requiredEdition.Value)
                {
                    return false;
                }
            }

            var boosterPackStream = searchContext.CreateBoosterPackStream(ante, ante > 1, false);

            int maxboosterPacks = ante == 1 ? 4 : 6;

            for (int i = 0; i < maxboosterPacks; i++)
            {
                var pack = searchContext.GetNextBoosterPack(ref boosterPackStream);

                if (pack.GetPackType() == MotelyBoosterPackType.Arcana)
                {
                    var tarotStream = searchContext.CreateArcanaPackTarotStream(ante, true);
                    if (
                        searchContext.GetNextArcanaPackHasTheSoul(
                            ref tarotStream,
                            pack.GetPackSize()
                        )
                    )
                    {
                        return true;
                    }
                }

                if (pack.GetPackType() == MotelyBoosterPackType.Spectral)
                {
                    var spectralStream = searchContext.CreateSpectralPackSpectralStream(ante, true);
                    if (
                        searchContext.GetNextSpectralPackHasTheSoul(
                            ref spectralStream,
                            pack.GetPackSize()
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            var state = new MotelyVectorRunState();
            VectorMask matchingHiero = VectorMask.NoBitsSet;
            VectorMask matchingMagic = VectorMask.NoBitsSet;

            for (int ante = 2; ante <= 4; ante++)
            {
                VectorEnum256<MotelyVoucher> vouchers = searchContext.GetAnteFirstVoucher(
                    ante,
                    state
                );
                matchingMagic |= VectorEnum256.Equals(vouchers, MotelyVoucher.MagicTrick);
                matchingHiero |= VectorEnum256.Equals(vouchers, MotelyVoucher.Hieroglyph);

                state.ActivateVoucher(vouchers);
            }

            var finalMask = VectorMask.AllBitsSet;

            return searchContext.SearchIndividualSeeds(
                finalMask,
                (MotelySingleSearchContext searchContext) =>
                {
                    bool hasPerkeoNegative = CheckLegendaryJokerInAnte(
                        1,
                        MotelyItemType.Perkeo,
                        ref searchContext
                    );

                    bool hasCanio = CheckLegendaryJokerInAnte(
                        8,
                        MotelyItemType.Canio,
                        ref searchContext
                    );

                    return (hasCanio && hasPerkeoNegative) ? 1 : 0;
                }
            );
        }
    }
}
