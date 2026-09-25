using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely;

public ref struct MotelyVectorSpectralStream(
    string resampleKey,
    MotelyVectorResampleStream resampleStream,
    MotelyVectorPrngStream soulBlackHolePrngStream
)
{
    public readonly bool IsNull => ResampleStream.IsInvalid;
    public readonly string ResampleKey = resampleKey;
    public MotelyVectorResampleStream ResampleStream = resampleStream;
    public MotelyVectorPrngStream SoulBlackHolePrngStream = soulBlackHolePrngStream;
    public readonly bool IsSoulBlackHoleable => !SoulBlackHolePrngStream.IsInvalid;
}

ref partial struct MotelyVectorSearchContext
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MotelyVectorSpectralStream CreateSpectralStream(
        string source,
        int ante,
        bool searchSpectral,
        bool soulBlackHoleable,
        bool isCached
    )
    {
        return new(
            MotelyPrngKeys.Spectral + source + ante,
            searchSpectral
                ? CreateResampleStream(MotelyPrngKeys.Spectral + source + ante, isCached)
                : MotelyVectorResampleStream.Invalid,
            soulBlackHoleable
                ? CreatePrngStream(
                    MotelyPrngKeys.SpectralSoulBlackHole + MotelyPrngKeys.Spectral + ante,
                    isCached
                )
                : MotelyVectorPrngStream.Invalid
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorSpectralStream CreateShopSpectralStream(int ante, bool isCached = false) =>
        CreateSpectralStream(MotelyPrngKeys.ShopItemSource, ante, true, false, isCached);

    public MotelyVectorSpectralStream CreateSpectralPackSpectralStream(
        int ante,
        bool soulOnly = false,
        bool isCached = false
    ) =>
        CreateSpectralStream(
            MotelyPrngKeys.SpectralPackItemSource,
            ante,
            !soulOnly,
            true,
            isCached
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorSpectralStream CreateSixthSenseSpectralStream(
        int ante,
        bool isCached = false
    ) => CreateSpectralStream(MotelyPrngKeys.JokerSixthSense, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorSpectralStream CreateSeanceSpectralStream(int ante, bool isCached = false) =>
        CreateSpectralStream(MotelyPrngKeys.JokerSeance, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItemVector GetNextSpectral(ref MotelyVectorSpectralStream stream)
    {
        return GetNextSpectral(ref stream, Vector512<double>.AllBitsSet);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItemVector GetNextSpectral(
        ref MotelyVectorSpectralStream stream,
        in Vector512<double> mask
    )
    {
        if (stream.IsNull)
        {
            return new MotelyItemVector(Vector256<int>.Zero);
        }

        Vector256<int> items = Vector256<int>.Zero;
        Vector512<double> activeMask = mask;

        if (stream.IsSoulBlackHoleable)
        {
            Vector512<double> randomSoul = GetNextRandom(
                ref stream.SoulBlackHolePrngStream,
                activeMask
            );
            Vector512<double> maskSoul =
                activeMask & Vector512.GreaterThan(randomSoul, Vector512.Create(0.997));
            Vector256<int> maskSoulInt = MotelyVectorUtils.ShrinkDoubleMaskToInt(maskSoul);
            items = Vector256.ConditionalSelect(
                maskSoulInt,
                Vector256.Create((int)MotelyItemType.TheSoul),
                items
            );
            activeMask = Vector512.AndNot(activeMask, maskSoul);

            if (!Vector512.EqualsAll(activeMask, Vector512<double>.Zero))
            {
                Vector512<double> randomBH = GetNextRandom(
                    ref stream.SoulBlackHolePrngStream,
                    activeMask
                );
                Vector512<double> maskBH =
                    activeMask & Vector512.GreaterThan(randomBH, Vector512.Create(0.997));
                Vector256<int> maskBHInt = MotelyVectorUtils.ShrinkDoubleMaskToInt(maskBH);
                items = Vector256.ConditionalSelect(
                    maskBHInt,
                    Vector256.Create((int)MotelyItemType.BlackHole),
                    items
                );
                activeMask = Vector512.AndNot(activeMask, maskBH);
            }
        }

        if (Vector512.EqualsAll(activeMask, Vector512<double>.Zero))
        {
            return new MotelyItemVector(items);
        }

        Vector256<int> spectralEnums = GetNextRandomInt(
            ref stream.ResampleStream.InitialPrngStream,
            0,
            MotelyEnum<MotelySpectralCard>.ValueCount,
            activeMask
        );
        Vector256<int> spectralItems = Vector256.BitwiseOr(
            spectralEnums,
            Vector256.Create((int)MotelyItemTypeCategory.SpectralCard)
        );
        var shrunkMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(activeMask);
        items = Vector256.ConditionalSelect(shrunkMask, spectralItems, items);

        int resampleCount = 0;
        while (resampleCount < MotelyVectorResampleLimit)
        {
            Vector256<int> resampleMaskInt =
                Vector256.Equals(items, Vector256.Create((int)MotelyItemType.TheSoul))
                | Vector256.Equals(items, Vector256.Create((int)MotelyItemType.BlackHole));
            if (Vector256.EqualsAll(resampleMaskInt, Vector256<int>.Zero))
                break;

            Vector512<double> resampleMask = MotelyVectorUtils.ExtendIntMaskToDouble(
                resampleMaskInt
            );
            Vector256<int> newEnums = GetNextRandomInt(
                ref GetResamplePrngStream(
                    ref stream.ResampleStream,
                    stream.ResampleKey,
                    resampleCount
                ),
                0,
                MotelyEnum<MotelySpectralCard>.ValueCount,
                resampleMask
            );
            Vector256<int> newItems = Vector256.BitwiseOr(
                newEnums,
                Vector256.Create((int)MotelyItemTypeCategory.SpectralCard)
            );
            items = Vector256.ConditionalSelect(resampleMaskInt, newItems, items);
            ++resampleCount;
        }

        return new MotelyItemVector(items);
    }

    public MotelyVectorItemSet GetNextSpectralPackContents(
        ref MotelyVectorSpectralStream spectralStream,
        MotelyBoosterPackSize size
    ) =>
        GetNextSpectralPackContents(
            ref spectralStream,
            MotelyBoosterPackType.Spectral.GetCardCount(size)
        );

    public MotelyVectorItemSet GetNextSpectralPackContents(
        ref MotelyVectorSpectralStream spectralStream,
        int size
    )
    {
        Debug.Assert(size <= MotelyVectorItemSet.MaxLength);

        MotelyVectorItemSet pack = new();

        for (int i = 0; i < size; i++)
            pack.Append(GetNextSpectral(ref spectralStream, pack));

        return pack;
    }

    public MotelyItemVector GetNextSpectral(
        ref MotelyVectorSpectralStream stream,
        in MotelyVectorItemSet itemSet
    )
    {
        Vector512<double> soulMaskDbl;
        Vector256<int> soulMaskInt;
        Vector512<double> blackHoleMaskDbl;
        Vector256<int> blackHoleMaskInt;

        if (stream.IsSoulBlackHoleable)
        {
            Vector512<double> soulValidMask = MotelyVectorUtils.ExtendIntMaskToDouble(
                ~itemSet.Contains(MotelyItemType.TheSoul)
            );
            soulMaskDbl =
                soulValidMask
                & Vector512.GreaterThan(
                    GetNextRandom(ref stream.SoulBlackHolePrngStream, soulValidMask),
                    Vector512.Create(0.997)
                );
            soulMaskInt = MotelyVectorUtils.ShrinkDoubleMaskToInt(soulMaskDbl);

            Vector512<double> blackHoleValidMask =
                MotelyVectorUtils.ExtendIntMaskToDouble(
                    ~itemSet.Contains(MotelyItemType.BlackHole)
                ) & ~soulMaskDbl;
            blackHoleMaskDbl =
                blackHoleValidMask
                & Vector512.GreaterThan(
                    GetNextRandom(ref stream.SoulBlackHolePrngStream, blackHoleValidMask),
                    Vector512.Create(0.997)
                );
            blackHoleMaskInt = MotelyVectorUtils.ShrinkDoubleMaskToInt(blackHoleMaskDbl);
        }
        else
        {
            soulMaskDbl = Vector512<double>.Zero;
            soulMaskInt = Vector256<int>.Zero;
            blackHoleMaskDbl = Vector512<double>.Zero;
            blackHoleMaskInt = Vector256<int>.Zero;
        }

        Vector256<int> spectrals;

        if (stream.ResampleStream.IsInvalid)
        {
            spectrals = Vector256.Create(
                new MotelyItem(MotelyItemType.SpectralExcludedByStream).Value
            );
        }
        else
        {
            Vector512<double> rollMask = ~soulMaskDbl & ~blackHoleMaskDbl;
            spectrals = GetNextRandomInt(
                ref stream.ResampleStream.InitialPrngStream,
                0,
                MotelyEnum<MotelySpectralCard>.ValueCount,
                rollMask
            );
            spectrals = Vector256.BitwiseOr(
                spectrals,
                Vector256.Create((int)MotelyItemTypeCategory.SpectralCard)
            );

            int resampleCount = 0;
            while (resampleCount < MotelyVectorResampleLimit)
            {
                Vector256<int> resampleMaskInt =
                    (
                        itemSet.Contains(new MotelyItemVector(spectrals))
                        | Vector256.Equals(
                            spectrals,
                            Vector256.Create((int)MotelyItemType.TheSoul)
                        )
                        | Vector256.Equals(
                            spectrals,
                            Vector256.Create((int)MotelyItemType.BlackHole)
                        )
                    )
                    & ~soulMaskInt
                    & ~blackHoleMaskInt;

                if (Vector256.EqualsAll(resampleMaskInt, Vector256<int>.Zero))
                    break;

                Vector256<int> nextSpectrals = GetNextRandomInt(
                    ref GetResamplePrngStream(
                        ref stream.ResampleStream,
                        stream.ResampleKey,
                        resampleCount
                    ),
                    0,
                    MotelyEnum<MotelySpectralCard>.ValueCount,
                    MotelyVectorUtils.ExtendIntMaskToDouble(resampleMaskInt)
                );

                nextSpectrals = Vector256.BitwiseOr(
                    nextSpectrals,
                    Vector256.Create((int)MotelyItemTypeCategory.SpectralCard)
                );

                spectrals = Vector256.ConditionalSelect(resampleMaskInt, nextSpectrals, spectrals);

                ++resampleCount;
            }
        }

        return new(
            Vector256.ConditionalSelect(
                soulMaskInt,
                Vector256.Create((int)MotelyItemType.TheSoul),
                Vector256.ConditionalSelect(
                    blackHoleMaskInt,
                    Vector256.Create((int)MotelyItemType.BlackHole),
                    spectrals
                )
            )
        );
    }

    public MotelyVectorItemSet GetNextSpectralPackContentsPerLane(
        ref MotelyVectorSpectralStream spectralStream,
        VectorEnum256<MotelyBoosterPackSize> packSizes,
        VectorMask isSpectralPack
    )
    {
        MotelyVectorItemSet pack = new();

        VectorMask isNormalSize = VectorEnum256.Equals(packSizes, MotelyBoosterPackSize.Normal);
        VectorMask isJumboSize = VectorEnum256.Equals(packSizes, MotelyBoosterPackSize.Jumbo);
        VectorMask isMegaSize = VectorEnum256.Equals(packSizes, MotelyBoosterPackSize.Mega);

        for (int cardIndex = 0; cardIndex < MotelyVectorItemSet.MaxLength; cardIndex++)
        {
            VectorMask shouldIncludeCard = cardIndex switch
            {
                0 or 1 => VectorMask.AllBitsSet,
                2 or 3 => VectorMask.AllBitsSet ^ isNormalSize,
                _ => VectorMask.NoBitsSet,
            };

            var Spectral = GetNextSpectral(ref spectralStream);

            var selectionMask = MotelyVectorUtils.VectorMaskToConditionalSelectMask(
                shouldIncludeCard
            );

            var excludedType = Vector256.Create((int)MotelyItemType.SpectralExcludedByStream);
            var maskedSpectral = new MotelyItemVector(
                Vector256.ConditionalSelect(
                    selectionMask,
                    Spectral.Type.HardwareVector,
                    excludedType
                )
            );

            pack.Append(maskedSpectral);
        }

        return pack;
    }

    public VectorMask GetNextSpectralPackHasTheSoul(
        ref MotelyVectorSpectralStream spectralStream,
        MotelyBoosterPackSize size
    )
    {
        Debug.Assert(spectralStream.IsSoulBlackHoleable, "Spectral pack does not have the soul.");

        int cardCount = MotelyBoosterPackType.Spectral.GetCardCount(size);
        VectorMask hasTheSoul = VectorMask.NoBitsSet;
        VectorMask hasBlackHole = VectorMask.NoBitsSet;

        for (int i = 0; i < cardCount; i++)
        {
            Vector512<double> random = GetNextRandom(ref spectralStream.SoulBlackHolePrngStream);
            VectorMask isSoul = new(
                (uint)
                    Vector512.ExtractMostSignificantBits(
                        Vector512.GreaterThan(random, Vector512.Create(0.997))
                    )
            );

            if (!isSoul.IsAllFalse())
            {
                hasTheSoul |= isSoul;

                for (; i < cardCount; i++)
                {
                    Vector512<double> randomBH = GetNextRandom(
                        ref spectralStream.SoulBlackHolePrngStream
                    );
                    hasBlackHole |= new VectorMask(
                        (uint)
                            Vector512.ExtractMostSignificantBits(
                                Vector512.GreaterThan(randomBH, Vector512.Create(0.997))
                            )
                    );
                }
                break;
            }

            if (!hasBlackHole.IsAllFalse())
            {
                Vector512<double> randomBH = GetNextRandom(
                    ref spectralStream.SoulBlackHolePrngStream
                );
                hasBlackHole |= new VectorMask(
                    (uint)
                        Vector512.ExtractMostSignificantBits(
                            Vector512.GreaterThan(randomBH, Vector512.Create(0.997))
                        )
                );
            }
        }

        return hasTheSoul;
    }

    public VectorMask GetNextSpectralPackHasThe(
        ref MotelyVectorSpectralStream spectralStream,
        MotelySpectralCard targetSpectral,
        MotelyBoosterPackSize size
    )
    {
        var contents = GetNextSpectralPackContents(ref spectralStream, size);
        return contents.Contains(
            (MotelyItemType)((int)MotelyItemTypeCategory.SpectralCard | (int)targetSpectral)
        );
    }

    public VectorMask GetNextSpectralPackHasThe(
        ref MotelyVectorSpectralStream spectralStream,
        MotelySpectralCard[] targetSpectrals,
        MotelyBoosterPackSize size
    )
    {
        var contents = GetNextSpectralPackContents(ref spectralStream, size);
        VectorMask hasAnyTarget = VectorMask.NoBitsSet;
        foreach (var target in targetSpectrals)
        {
            hasAnyTarget |= contents.Contains(
                (MotelyItemType)((int)MotelyItemTypeCategory.SpectralCard | (int)target)
            );
        }
        return hasAnyTarget;
    }
}
