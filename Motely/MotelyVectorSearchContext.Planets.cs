using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely;

public ref struct MotelyVectorPlanetStream(
    string resampleKey,
    MotelyVectorResampleStream resampleStream,
    MotelyVectorPrngStream blackHolePrngStream
)
{
    public readonly bool IsNull => ResampleStream.IsInvalid;
    public readonly string ResampleKey = resampleKey;
    public MotelyVectorResampleStream ResampleStream = resampleStream;
    public MotelyVectorPrngStream BlackHolePrngStream = blackHolePrngStream;
    public readonly bool IsBlackHoleable => !BlackHolePrngStream.IsInvalid;
}

ref partial struct MotelyVectorSearchContext
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MotelyVectorPlanetStream CreatePlanetStream(
        string source,
        int ante,
        bool blackHoleable,
        bool isCached
    )
    {
        string resampleKey = MotelyPrngKeys.Planet + source + ante;
        return new(
            resampleKey,
            CreateResampleStream(resampleKey, isCached),
            blackHoleable
                ? CreatePrngStream(
                    MotelyPrngKeys.PlanetBlackHole + MotelyPrngKeys.Planet + ante,
                    isCached
                )
                : MotelyVectorPrngStream.Invalid
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorPlanetStream CreateCelestialPackPlanetStream(
        int ante,
        bool isCached = false
    ) => CreatePlanetStream(MotelyPrngKeys.CelestialPackItemSource, ante, true, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorPlanetStream CreateShopPlanetStream(int ante, bool isCached = false) =>
        CreatePlanetStream(MotelyPrngKeys.ShopItemSource, ante, false, isCached);

    public MotelyVectorItemSet GetNextCelestialPackContents(
        ref MotelyVectorPlanetStream planetStream,
        MotelyBoosterPackSize size
    )
    {
        int cardCount = MotelyBoosterPackType.Celestial.GetCardCount(size);
        MotelyVectorItemSet pack = new();
        for (int i = 0; i < cardCount; i++)
            pack.Append(GetNextPlanet(ref planetStream, pack));
        return pack;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItemVector GetNextPlanet(ref MotelyVectorPlanetStream planetStream)
    {
        return GetNextPlanet(ref planetStream, Vector512<double>.AllBitsSet);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItemVector GetNextPlanet(
        ref MotelyVectorPlanetStream planetStream,
        in Vector512<double> mask
    )
    {
        Vector512<double> blackHoleMask;
        if (planetStream.IsBlackHoleable)
        {
            blackHoleMask =
                mask
                & Vector512.GreaterThan(
                    GetNextRandom(ref planetStream.BlackHolePrngStream, mask),
                    Vector512.Create(0.997)
                );
        }
        else
        {
            blackHoleMask = Vector512<double>.Zero;
        }

        Vector256<int> planets;
        if (planetStream.ResampleStream.IsInvalid)
        {
            planets = Vector256.Create(new MotelyItem(MotelyItemType.PlanetExcludedByStream).Value);
        }
        else
        {
            var planetMask = mask & ~blackHoleMask;
            planets = GetNextRandomInt(
                ref planetStream.ResampleStream.InitialPrngStream,
                0,
                MotelyEnum<MotelyPlanetCard>.ValueCount,
                planetMask
            );
            planets = Vector256.Create((int)MotelyItemTypeCategory.PlanetCard) | planets;
        }

        if (!planetStream.IsBlackHoleable)
        {
            return new(planets);
        }

        return new(
            Vector256.ConditionalSelect(
                MotelyVectorUtils.ShrinkDoubleMaskToInt(blackHoleMask),
                Vector256.Create(new MotelyItem(MotelyItemType.BlackHole).Value),
                planets
            )
        );
    }

    public MotelyItemVector GetNextPlanet(
        ref MotelyVectorPlanetStream planetStream,
        in MotelyVectorItemSet itemSet
    )
    {
        Vector512<double> blackHoleMask;
        Vector256<int> blackHoleMaskInt;
        if (planetStream.IsBlackHoleable)
        {
            Vector512<double> validMask = MotelyVectorUtils.ExtendIntMaskToDouble(
                ~itemSet.Contains(MotelyItemType.BlackHole)
            );
            blackHoleMask =
                validMask
                & Vector512.GreaterThan(
                    GetNextRandom(ref planetStream.BlackHolePrngStream, validMask),
                    Vector512.Create(0.997)
                );
            blackHoleMaskInt = MotelyVectorUtils.ShrinkDoubleMaskToInt(blackHoleMask);
        }
        else
        {
            blackHoleMask = Vector512<double>.Zero;
            blackHoleMaskInt = Vector256<int>.Zero;
        }

        Vector256<int> planets;
        if (planetStream.ResampleStream.IsInvalid)
        {
            planets = Vector256.Create(new MotelyItem(MotelyItemType.PlanetExcludedByStream).Value);
        }
        else
        {
            planets = GetNextRandomInt(
                ref planetStream.ResampleStream.InitialPrngStream,
                0,
                MotelyEnum<MotelyPlanetCard>.ValueCount,
                ~blackHoleMask
            );
            planets = Vector256.Create((int)MotelyItemTypeCategory.PlanetCard) | planets;

            int resampleCount = 0;
            while (resampleCount < MotelyVectorResampleLimit)
            {
                Vector256<int> resampleMaskInt = itemSet.Contains(new MotelyItemVector(planets));
                resampleMaskInt &= ~blackHoleMaskInt;
                if (Vector256.EqualsAll(resampleMaskInt, Vector256<int>.Zero))
                    break;
                Vector256<int> nextPlanets = GetNextRandomInt(
                    ref GetResamplePrngStream(
                        ref planetStream.ResampleStream,
                        planetStream.ResampleKey,
                        resampleCount
                    ),
                    0,
                    MotelyEnum<MotelyPlanetCard>.ValueCount,
                    MotelyVectorUtils.ExtendIntMaskToDouble(resampleMaskInt)
                );
                nextPlanets =
                    Vector256.Create((int)MotelyItemTypeCategory.PlanetCard) | nextPlanets;
                planets = Vector256.ConditionalSelect(resampleMaskInt, nextPlanets, planets);
                ++resampleCount;
            }
        }

        return new(
            Vector256.ConditionalSelect(
                blackHoleMaskInt,
                Vector256.Create(new MotelyItem(MotelyItemType.BlackHole).Value),
                planets
            )
        );
    }

    public VectorMask GetNextCelestialPackHasThe(
        ref MotelyVectorPlanetStream planetStream,
        MotelyPlanetCard targetPlanet,
        MotelyBoosterPackSize size
    )
    {
        var contents = GetNextCelestialPackContents(ref planetStream, size);
        return contents.Contains(
            (MotelyItemType)((int)MotelyItemTypeCategory.PlanetCard | (int)targetPlanet)
        );
    }

    public VectorMask GetNextCelestialPackHasThe(
        ref MotelyVectorPlanetStream planetStream,
        MotelyPlanetCard[] targetPlanets,
        MotelyBoosterPackSize size
    )
    {
        var contents = GetNextCelestialPackContents(ref planetStream, size);
        VectorMask hasAnyTarget = VectorMask.NoBitsSet;
        foreach (var target in targetPlanets)
        {
            hasAnyTarget |= contents.Contains(
                (MotelyItemType)((int)MotelyItemTypeCategory.PlanetCard | (int)target)
            );
        }
        return hasAnyTarget;
    }
}
