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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorTarotStream CreateArcanaPackTarotStream(
        int ante,
        bool soulOnly = false,
        bool isCached = false
    ) => CreateTarotStream(MotelyPrngKeys.ArcanaPackItemSource, ante, !soulOnly, true, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorTarotStream CreateShopTarotStream(int ante, bool isCached = false) =>
        CreateTarotStream(MotelyPrngKeys.ShopItemSource, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorTarotStream CreateEmperorTarotStream(int ante, bool isCached = false) =>
        CreateTarotStream(MotelyPrngKeys.TarotEmperor, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorTarotStream CreatePurpleSealTarotStream(int ante, bool isCached = false) =>
        CreateTarotStream(MotelyPrngKeys.SealPurple, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VectorMask GetNextArcanaPackHasTheSoul(
        ref MotelyVectorTarotStream tarotStream,
        MotelyBoosterPackSize size
    )
    {
        Debug.Assert(tarotStream.IsSoulable, "Tarot pack does not have the soul.");
        Debug.Assert(
            tarotStream.ResampleStream.IsInvalid,
            "This method is only valid for Tarot streams created with soul only."
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyVectorItemSet GetNextEmperorTarots(ref MotelyVectorTarotStream tarotStream)
    {
        Debug.Assert(!tarotStream.IsSoulable, "Emperor Tarot stream should not have the soul.");

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
                Vector256.Create(new MotelyItem(MotelyItemType.TheSoul).Value),
                tarots
            )
        );
    }

    public MotelyItemVector GetNextTarot(
        ref MotelyVectorTarotStream tarotStream,
        in MotelyVectorItemSet itemSet
    ) => GetNextTarot(ref tarotStream, in itemSet, Vector512<double>.AllBitsSet);

    /// <summary>
    /// Pack draw (deduplicated against <paramref name="itemSet"/>, Soul at most once) for the
    /// lanes in <paramref name="mask"/> only: lanes outside it pull nothing from any stream, so a
    /// walk over packs whose type or size differs per lane stays on the scalar engine's draws.
    /// </summary>
    public MotelyItemVector GetNextTarot(
        ref MotelyVectorTarotStream tarotStream,
        in MotelyVectorItemSet itemSet,
        in Vector512<double> mask
    )
    {
        Vector512<double> soulMaskDbl;
        Vector256<int> soulMaskInt;
        Vector256<int> laneMaskInt = MotelyVectorUtils.ShrinkDoubleMaskToInt(mask);

        if (tarotStream.IsSoulable)
        {
            Vector512<double> soulValidMask =
                mask
                & MotelyVectorUtils.ExtendIntMaskToDouble(
                    ~itemSet.Contains(MotelyItemType.TheSoul)
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
                mask & ~soulMaskDbl
            );

            tarots = Vector256.Create((int)MotelyItemTypeCategory.TarotCard) | tarots;

            int resampleCount = 0;

            while (resampleCount < MotelyVectorResampleLimit)
            {
                Vector256<int> resampleMaskInt = itemSet.Contains(new MotelyItemVector(tarots));

                // Don't resmaple lanes which have the soul, or lanes that drew nothing
                resampleMaskInt &= ~soulMaskInt & laneMaskInt;

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
                Vector256.Create(new MotelyItem(MotelyItemType.TheSoul).Value),
                tarots
            )
        );
    }

    // The pack a player opens is deduplicated (resample rolls replace repeats), so HasThe
    // answers from the same contents walk the scalar engine uses — the raw stream diverges
    // whenever a duplicate resamples into the target.
    public VectorMask GetNextArcanaPackHasThe(
        ref MotelyVectorTarotStream tarotStream,
        MotelyTarotCard targetTarot,
        MotelyBoosterPackSize size
    )
    {
        var contents = GetNextArcanaPackContents(ref tarotStream, size);
        return contents.Contains(
            (MotelyItemType)((int)MotelyItemTypeCategory.TarotCard | (int)targetTarot)
        );
    }

    /// <inheritdoc cref="GetNextArcanaPackHasThe(ref MotelyVectorTarotStream, MotelyTarotCard, MotelyBoosterPackSize)"/>
    public VectorMask GetNextArcanaPackHasThe(
        ref MotelyVectorTarotStream tarotStream,
        MotelyTarotCard[] targetTarots,
        MotelyBoosterPackSize size
    )
    {
        var contents = GetNextArcanaPackContents(ref tarotStream, size);
        VectorMask hasAnyTarget = VectorMask.NoBitsSet;
        foreach (var target in targetTarots)
        {
            hasAnyTarget |= contents.Contains(
                (MotelyItemType)((int)MotelyItemTypeCategory.TarotCard | (int)target)
            );
        }
        return hasAnyTarget;
    }
}
