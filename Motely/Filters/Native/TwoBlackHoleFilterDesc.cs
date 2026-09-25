using System.Runtime.Intrinsics;

namespace Motely.Filters.Native;

public struct TwoBlackHoleFilterDesc()
    : IMotelySeedFilterDesc<TwoBlackHoleFilterDesc.TwoBlackHoleFilter>
{
    public const int MinBlackHoles = 2;

    public readonly TwoBlackHoleFilter CreateFilter(ref MotelyFilterCreationContext ctx) => new();

    public struct TwoBlackHoleFilter() : IMotelySeedFilter
    {
        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            MotelyVectorBoosterPackStream packStream = searchContext.CreateBoosterPackStream(1);
            searchContext.GetNextBoosterPack(ref packStream);
            VectorEnum256<MotelyBoosterPack> secondPack = searchContext.GetNextBoosterPack(
                ref packStream
            );

            VectorMask matching = VectorEnum256.Equals(
                secondPack.GetPackType(),
                MotelyBoosterPackType.Celestial
            );

            if (matching.IsAllFalse())
                return Vector512<double>.Zero;

            return searchContext.SearchIndividualSeeds(
                matching,
                (MotelySingleSearchContext ctx) =>
                {
                    MotelySingleBoosterPackStream packs = ctx.CreateBoosterPackStream(1);

                    MotelyBoosterPack buffoonPack = ctx.GetNextBoosterPack(ref packs);
                    MotelySingleJokerStream jokerStream = ctx.CreateBuffoonPackJokerStream(1);

                    bool showmanFound = false;
                    int buffoonCards = buffoonPack.GetPackCardCount();
                    for (int i = 0; i < buffoonCards; i++)
                    {
                        if (ctx.GetNextJoker(ref jokerStream).Type == MotelyItemType.Showman)
                            showmanFound = true;
                    }

                    if (!showmanFound)
                        return 0;

                    MotelyBoosterPack celestialPack = ctx.GetNextBoosterPack(ref packs);
                    if (celestialPack.GetPackType() != MotelyBoosterPackType.Celestial)
                        return 0;

                    MotelySinglePlanetStream planetStream = ctx.CreateCelestialPackPlanetStream(1);
                    int celestialCards = celestialPack.GetPackCardCount();
                    int blackHoles = 0;
                    for (int i = 0; i < celestialCards; i++)
                    {
                        if (ctx.GetNextPlanet(ref planetStream).Type == MotelyItemType.BlackHole)
                            blackHoles++;
                    }

                    return (blackHoles >= MinBlackHoles) ? 1 : 0;
                }
            );
        }
    }
}
