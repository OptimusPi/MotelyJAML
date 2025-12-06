using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely;

public struct MotelyVectorTarotStream(
    string resampleKey,
    MotelyVectorResampleStream resampleStream,
    MotelyVectorPrngStream soulStream
)
{
    public readonly bool IsNull => ResampleKey == null;
    public readonly string ResampleKey = resampleKey;
    public MotelyVectorResampleStream ResampleStream = resampleStream;
    public MotelyVectorPrngStream SoulPrngStream = soulStream;
    public readonly bool IsSoulable => !SoulPrngStream.IsInvalid;

    public readonly MotelySingleTarotStream CreateSingleStream(int lane)
    {
        return new(
            ResampleKey,
            ResampleStream.CreateSingleStream(lane),
            SoulPrngStream.CreateSingleStream(lane)
        );
    }
}

ref partial struct MotelyVectorSearchContext
{
#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private MotelyVectorTarotStream CreateTarotStream(
        string source,
        int ante,
        bool searchTarot,
        bool soulable,
        bool isCached
    )
    {
        return new(
            MotelyPrngKeys.Tarot + source + ante,
            searchTarot
                ? CreateResampleStream(MotelyPrngKeys.Tarot + source + ante, isCached)
                : MotelyVectorResampleStream.Invalid,
            soulable
                ? CreatePrngStream(MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + ante, isCached)
                : MotelyVectorPrngStream.Invalid
        );
    }

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public MotelyVectorTarotStream CreateArcanaPackTarotStream(
        int ante,
        bool soulOnly = false,
        bool isCached = false
    ) => CreateTarotStream(MotelyPrngKeys.ArcanaPackItemSource, ante, !soulOnly, true, isCached);

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public MotelyVectorTarotStream CreateShopTarotStream(int ante, bool isCached = false) =>
        CreateTarotStream(MotelyPrngKeys.ShopItemSource, ante, true, false, isCached);

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public MotelyVectorTarotStream CreateEmperorTarotStream(int ante, bool isCached = false) =>
        CreateTarotStream(MotelyPrngKeys.TarotEmperor, ante, true, false, isCached);

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public MotelyVectorTarotStream CreatePurpleSealTarotStream(int ante, bool isCached = false) =>
        CreateTarotStream(MotelyPrngKeys.SealPurple, ante, true, false, isCached);

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public VectorMask GetNextArcanaPackHasTheSoul(ref MotelyVectorTarotStream tarotStream, MotelyBoosterPackSize size)
    {
        Debug.Assert(tarotStream.IsSoulable, "Tarot pack does not have the soul.");
        Debug.Assert(
            tarotStream.ResampleStream.IsInvalid,
            "This method is only valid for tarot streams created with soul only."
        );

        int cardCount = MotelyBoosterPackType.Arcana.GetCardCount(size);

        Vector512<double> hasSoulMask = Vector512<double>.Zero;

        for (int i = 0; i < cardCount; i++)
        {
            hasSoulMask |= Vector512.GreaterThan(
                GetNextRandom(ref tarotStream.SoulPrngStream, ~hasSoulMask),
                Vector512.Create(0.997)
            );
        }

        return hasSoulMask;
    }

    public MotelyVectorItemSet GetNextArcanaPackContents(
        ref MotelyVectorTarotStream tarotStream,
        MotelyBoosterPackSize size
    )
    {
        int cardCount = MotelyBoosterPackType.Arcana.GetCardCount(size);
        MotelyVectorItemSet pack = new();

        for (int i = 0; i < cardCount; i++)
            pack.Append(GetNextTarot(ref tarotStream, pack));

        return pack;
    }

#if !DEBUG
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public MotelyVectorItemSet GetNextEmperorTarots(ref MotelyVectorTarotStream tarotStream)
    {
        Debug.Assert(!tarotStream.IsSoulable, "Emperor tarot stream should not have the soul.");

        MotelyVectorItemSet items = new();

        items.Append(GetNextTarot(ref tarotStream));
        items.Append(GetNextTarot(ref tarotStream, items));

        return items;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItemVector GetNextTarot(ref MotelyVectorTarotStream tarotStream)
    {
        return GetNextTarot(ref tarotStream, Vector512<double>.AllBitsSet);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItemVector GetNextShopTarotOrNull(
        ref MotelyVectorTarotStream tarotStream,
        ref MotelyVectorPrngStream itemTypeStream,
        Vector512<double> totalRate,
        Vector512<double> tarotRate
    )
    {
        // Check what type this slot is
        var itemTypePoll = GetNextRandom(ref itemTypeStream) * totalRate;
        itemTypePoll -= Vector512.Create(20.0); // Skip joker range
        var isTarotSlot = Vector512.LessThan(itemTypePoll, tarotRate);

        // Only advance tarot stream for tarot slots
        var tarot = GetNextTarot(ref tarotStream, isTarotSlot);

        // Return tarot or None for non-tarot slots
        var tarotIntMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(isTarotSlot);
        var noneItem = Vector256<int>.Zero;

        return new MotelyItemVector(
            Vector256.ConditionalSelect(tarotIntMask, tarot.Value, noneItem)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItemVector GetNextTarot(
        ref MotelyVectorTarotStream tarotStream,
        in Vector512<double> mask
    )
    {
        Vector512<double> soulMask;

        if (tarotStream.IsSoulable)
        {
            soulMask =
                mask
                & Vector512.GreaterThan(
                    GetNextRandom(ref tarotStream.SoulPrngStream, mask),
                    Vector512.Create(0.997)
                );
        }
        else
        {
            soulMask = Vector512<double>.Zero;
        }

        Vector256<int> tarots;

        if (tarotStream.ResampleStream.IsInvalid)
        {
            tarots = Vector256.Create(new MotelyItem(MotelyItemType.TarotExcludedByStream).Value);
        }
        else
        {
            // Only advance PRNG for lanes that need it (using mask and not soul mask)
            var tarotMask = mask & ~soulMask;
            tarots = GetNextRandomInt(
                ref tarotStream.ResampleStream.InitialPrngStream,
                0,
                MotelyEnum<MotelyTarotCard>.ValueCount,
                tarotMask
            );

            tarots = Vector256.Create((int)MotelyItemTypeCategory.TarotCard) | tarots;
        }

        if (!tarotStream.IsSoulable)
        {
            return new(tarots);
        }

        return new(
            Vector256.ConditionalSelect(
                MotelyVectorUtils.ShrinkDoubleMaskToInt(soulMask),
                Vector256.Create(new MotelyItem(MotelyItemType.Soul).Value),
                tarots
            )
        );
    }

    public MotelyItemVector GetNextTarot(
        ref MotelyVectorTarotStream tarotStream,
        in MotelyVectorItemSet itemSet
    )
    {
        Vector512<double> soulMaskDbl;
        Vector256<int> soulMaskInt;

        if (tarotStream.IsSoulable)
        {
            Vector512<double> soulValidMask = MotelyVectorUtils.ExtendIntMaskToDouble(
                ~itemSet.Contains(MotelyItemType.Soul)
            );
            soulMaskDbl =
                soulValidMask
                & Vector512.GreaterThan(
                    GetNextRandom(ref tarotStream.SoulPrngStream, soulValidMask),
                    Vector512.Create(0.997)
                );
            soulMaskInt = MotelyVectorUtils.ShrinkDoubleMaskToInt(soulMaskDbl);
        }
        else
        {
            soulMaskDbl = Vector512<double>.Zero;
            soulMaskInt = Vector256<int>.Zero;
        }

        Vector256<int> tarots;

        if (tarotStream.ResampleStream.IsInvalid)
        {
            tarots = Vector256.Create(new MotelyItem(MotelyItemType.TarotExcludedByStream).Value);
        }
        else
        {
            tarots = GetNextRandomInt(
                ref tarotStream.ResampleStream.InitialPrngStream,
                0,
                MotelyEnum<MotelyTarotCard>.ValueCount,
                ~soulMaskDbl
            );

            tarots = Vector256.Create((int)MotelyItemTypeCategory.TarotCard) | tarots;

            int resampleCount = 0;

            while (true)
            {
                Vector256<int> resampleMaskInt = itemSet.Contains(new MotelyItemVector(tarots));

                // Don't resmaple lanes which have the soul
                resampleMaskInt &= ~soulMaskInt;

                if (Vector256.EqualsAll(resampleMaskInt, Vector256<int>.Zero))
                    break;

                Vector256<int> nextTarots = GetNextRandomInt(
                    ref GetResamplePrngStream(
                        ref tarotStream.ResampleStream,
                        tarotStream.ResampleKey,
                        resampleCount
                    ),
                    0,
                    MotelyEnum<MotelyTarotCard>.ValueCount,
                    MotelyVectorUtils.ExtendIntMaskToDouble(resampleMaskInt)
                );

                nextTarots = Vector256.Create((int)MotelyItemTypeCategory.TarotCard) | nextTarots;

                tarots = Vector256.ConditionalSelect(resampleMaskInt, nextTarots, tarots);

                ++resampleCount;
            }
        }

        return new(
            Vector256.ConditionalSelect(
                soulMaskInt,
                Vector256.Create(new MotelyItem(MotelyItemType.Soul).Value),
                tarots
            )
        );
    }

    public VectorMask GetNextArcanaPackHasThe(
        ref MotelyVectorTarotStream tarotStream,
        MotelyTarotCard targetTarot,
        MotelyBoosterPackSize size
    )
    {
        int cardCount = MotelyBoosterPackType.Arcana.GetCardCount(size);
        VectorMask hasTarget = VectorMask.NoBitsSet;

        for (int i = 0; i < cardCount; i++)
        {
            var tarot = GetNextTarot(ref tarotStream);
            // Extract tarot card type using bit masking (similar to PlayingCardSuit pattern)
            var tarotType = new VectorEnum256<MotelyTarotCard>(
                Vector256.BitwiseAnd(
                    tarot.Value,
                    Vector256.Create(Motely.ItemTypeMask & ~Motely.ItemTypeCategoryMask)
                )
            );
            VectorMask isTarget = VectorEnum256.Equals(tarotType, targetTarot);
            hasTarget |= isTarget;

            // Early exit optimization - if all lanes have found the target, no need to continue
            if (hasTarget.IsAllTrue())
                break;
        }

        return hasTarget;
    }

    public VectorMask GetNextArcanaPackHasThe(
        ref MotelyVectorTarotStream tarotStream,
        MotelyTarotCard[] targetTarots,
        MotelyBoosterPackSize size
    )
    {
        int cardCount = MotelyBoosterPackType.Arcana.GetCardCount(size);
        VectorMask hasAnyTarget = VectorMask.NoBitsSet;

        for (int i = 0; i < cardCount; i++)
        {
            var tarot = GetNextTarot(ref tarotStream);
            // Extract tarot card type using bit masking (similar to PlayingCardSuit pattern)
            var tarotType = new VectorEnum256<MotelyTarotCard>(
                Vector256.BitwiseAnd(
                    tarot.Value,
                    Vector256.Create(Motely.ItemTypeMask & ~Motely.ItemTypeCategoryMask)
                )
            );

            VectorMask isAnyTarget = VectorMask.NoBitsSet;
            foreach (var target in targetTarots)
            {
                isAnyTarget |= VectorEnum256.Equals(tarotType, target);
            }

            hasAnyTarget |= isAnyTarget;

            // Early exit optimization - if all lanes have found any target, no need to continue
            if (hasAnyTarget.IsAllTrue())
                break;
        }

        return hasAnyTarget;
    }
}
