namespace Motely.Filters.Native;

using System.Runtime.CompilerServices;

public struct NegativePerkeoFilterDescOld()
    : IMotelySeedFilterDesc<NegativePerkeoFilterDescOld.FilterStruct>
{
    public const int MinAnte = 1;
    public const int MaxAnte = 2;

    public readonly FilterStruct CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        for (int ante = MinAnte; ante <= MaxAnte; ante++)
            ctx.CacheLegendaryJokerStream(ante, MotelyJokerFixedRarityStreamFlags.ExcludeStickers);

        return new FilterStruct();
    }

    public struct FilterStruct() : IMotelySeedFilter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            VectorMask seedMask = VectorMask.NoBitsSet;

            for (int ante = MinAnte; ante <= MaxAnte; ante++)
            {
                var jokerEditionStream = searchContext.CreateLegendaryJokerStream(
                    ante,
                    MotelyJokerFixedRarityStreamFlags.ExcludeStickers,
                    true
                );

                var jokerVector = searchContext.GetNextJoker(ref jokerEditionStream);

                VectorMask negativePerkeoMask = VectorEnum256.Equals(
                    jokerVector.Edition,
                    MotelyItemEdition.Negative
                );
                negativePerkeoMask &= VectorEnum256.Equals(jokerVector.Type, MotelyItemType.Perkeo);

                if (negativePerkeoMask.IsAllFalse())
                    continue;

                seedMask |= searchContext.SearchIndividualSeeds(
                    negativePerkeoMask,
                    (MotelySingleSearchContext searchContext) =>
                    {
                        MotelySingleTarotStream tarotStream = default;
                        MotelySingleSpectralStream spectralStream = default;
                        bool tarotStreamInit = false,
                            spectralStreamInit = false;

                        MotelySingleBoosterPackStream boosterPackStream =
                            searchContext.CreateBoosterPackStream(ante);

                        for (int i = 0; i < 5; i++)
                        {
                            MotelyBoosterPack pack = searchContext.GetNextBoosterPack(
                                ref boosterPackStream
                            );

                            if (pack.GetPackType() == MotelyBoosterPackType.Arcana)
                            {
                                if (!tarotStreamInit)
                                {
                                    tarotStreamInit = true;
                                    tarotStream = searchContext.CreateArcanaPackTarotStream(
                                        ante,
                                        true
                                    );
                                }

                                if (
                                    searchContext.GetNextArcanaPackHasTheSoul(
                                        ref tarotStream,
                                        pack.GetPackSize()
                                    )
                                )
                                    return 1;
                            }

                            if (pack.GetPackType() == MotelyBoosterPackType.Spectral)
                            {
                                if (!spectralStreamInit)
                                {
                                    spectralStreamInit = true;
                                    spectralStream = searchContext.CreateSpectralPackSpectralStream(
                                        ante,
                                        true
                                    );
                                }

                                if (
                                    searchContext.GetNextSpectralPackHasTheSoul(
                                        ref spectralStream,
                                        pack.GetPackSize()
                                    )
                                )
                                    return 1;
                            }
                        }

                        return 0;
                    }
                );
            }

            return seedMask;
        }
    }
}
