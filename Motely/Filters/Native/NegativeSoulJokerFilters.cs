namespace Motely.Filters.Native;

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Motely.Filters;

public readonly struct NegativeLegendaryJokerSimdFilterDesc(MotelyItemType? targetJoker = null)
    : IMotelySeedFilterDesc<NegativeLegendaryJokerSimdFilterDesc.FilterStruct>
{
    public const int MinAnte = 1;
    public const int MaxAnte = 2;

    public readonly FilterStruct CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        for (int ante = MinAnte; ante <= MaxAnte; ante++)
        {
            ctx.CacheLegendaryJokerStream(
                ante,
                MotelyJokerFixedRarityStreamFlags.ExcludeJokerType
                    | MotelyJokerFixedRarityStreamFlags.ExcludeStickers
            );
            ctx.CacheLegendaryJokerStream(
                ante,
                MotelyJokerFixedRarityStreamFlags.ExcludeEdition
                    | MotelyJokerFixedRarityStreamFlags.ExcludeStickers
            );
        }

        return new FilterStruct(targetJoker);
    }

    public readonly struct FilterStruct(MotelyItemType? targetJoker) : IMotelySeedFilter
    {
        private static readonly Vector256<int> LegendaryRarityBits = Vector256.Create(
            (int)MotelyItemTypeCategory.Joker | (int)MotelyJokerRarity.Legendary
        );

        private static readonly Vector256<int> RarityMask = Vector256.Create(
            MotelyGlobals.ItemTypeCategoryMask | MotelyGlobals.JokerRarityMask
        );

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            VectorMask seedMask = VectorMask.NoBitsSet;

            for (int ante = MinAnte; ante <= MaxAnte; ante++)
            {
                if (seedMask.IsAllTrue())
                    break;

                var editionStream = searchContext.CreateLegendaryJokerStream(
                    ante,
                    MotelyJokerFixedRarityStreamFlags.ExcludeJokerType
                        | MotelyJokerFixedRarityStreamFlags.ExcludeStickers,
                    true
                );
                VectorMask negativeMask = VectorEnum256.Equals(
                    searchContext.GetNextJoker(ref editionStream).Edition,
                    MotelyItemEdition.Negative
                );

                if (negativeMask.IsAllFalse())
                    continue;

                var typeStream = searchContext.CreateLegendaryJokerStream(
                    ante,
                    MotelyJokerFixedRarityStreamFlags.ExcludeEdition
                        | MotelyJokerFixedRarityStreamFlags.ExcludeStickers,
                    true
                );
                var typeVector = searchContext.GetNextJoker(ref typeStream).Type;

                VectorMask legendaryMask;
                if (targetJoker is { } t)
                    legendaryMask = VectorEnum256.Equals(typeVector, t);
                else
                    legendaryMask = Vector256.Equals(
                        Vector256.BitwiseAnd(typeVector.HardwareVector, RarityMask),
                        LegendaryRarityBits
                    );

                seedMask |= negativeMask & legendaryMask;
            }

            return seedMask;
        }
    }
}

public readonly struct LegendaryJokerShopSoulFilterDesc(
    LegendaryJokerSourceConfig? boosterSources = null,
    int[]? searchAntes = null
) : IMotelySeedFilterDesc<LegendaryJokerShopSoulFilterDesc.FilterStruct>
{
    private static readonly int[] DefaultAntes =
    [
        NegativeLegendaryJokerSimdFilterDesc.MinAnte,
        NegativeLegendaryJokerSimdFilterDesc.MaxAnte,
    ];

    private static readonly LegendaryJokerSourceConfig DefaultAllBoosterSources = new()
    {
        BoosterPacks = [0, 1, 2, 3, 4, 5],
    };

    public readonly FilterStruct CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        LegendaryJokerSourceConfig normalized = boosterSources ?? DefaultAllBoosterSources;

        int maxPack = normalized.MaxReferencedBoosterSlot();
        int[] antes = searchAntes ?? DefaultAntes;

        for (int i = 0; i < antes.Length; i++)
            ctx.CacheBoosterPackStream(antes[i], force: true);

        return new FilterStruct(normalized, maxPack, antes);
    }

    public readonly struct FilterStruct(
        LegendaryJokerSourceConfig sources,
        int maxBoosterPack,
        int[] antes
    ) : IMotelySeedFilter
    {
        private readonly LegendaryJokerSourceConfig _sources = sources;
        private readonly int _maxBoosterPack = maxBoosterPack;
        private readonly int[] _antes = antes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_maxBoosterPack < 0)
                return VectorMask.NoBitsSet;

            int maxP = _maxBoosterPack;
            int[] antes = _antes;
            LegendaryJokerSourceConfig src = _sources;

            if (TryVectorPath(ref ctx, src, maxP, antes, out VectorMask result))
                return result;

            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext sctx) =>
                {
                    for (int i = 0; i < antes.Length; i++)
                    {
                        if (
                            LegendarySoulMatcher.MatchAnteShopPackHasSoulOnly(
                                ref sctx,
                                antes[i],
                                src,
                                maxP
                            )
                        )
                            return 1;
                    }
                    return 0;
                }
            );
        }

        private static bool TryVectorPath(
            ref MotelyVectorSearchContext ctx,
            LegendaryJokerSourceConfig src,
            int maxBoosterPack,
            int[] antes,
            out VectorMask hasSoulMask
        )
        {
            hasSoulMask = VectorMask.NoBitsSet;
            bool split = src.ArcanaPacks.Length > 0 || src.SpectralPacks.Length > 0;

            for (int a = 0; a < antes.Length; a++)
            {
                if (hasSoulMask.IsAllTrue())
                    return true;

                int ante = antes[a];
                var packStream = ctx.CreateBoosterPackStream(ante, true, false);

                MotelyVectorTarotStream tarotStream = default;
                MotelyVectorSpectralStream spectralStream = default;
                bool tarotInit = false;
                bool spectralInit = false;

                for (int p = 0; p <= maxBoosterPack; p++)
                {
                    if (hasSoulMask.IsAllTrue())
                        return true;

                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    var packType = pack.GetPackType();
                    var packSize = pack.GetPackSize();

                    bool slotTargeted;
                    if (split)
                    {
                        bool arcSlot = ContainsSlot(src.ArcanaPacks, p);
                        bool specSlot = ContainsSlot(src.SpectralPacks, p);
                        slotTargeted = arcSlot || specSlot;
                    }
                    else
                        slotTargeted = ContainsSlot(src.BoosterPacks, p);

                    if (!slotTargeted)
                        continue;

                    VectorMask isArcana = VectorEnum256.Equals(
                        packType,
                        MotelyBoosterPackType.Arcana
                    );
                    VectorMask isSpectral = VectorEnum256.Equals(
                        packType,
                        MotelyBoosterPackType.Spectral
                    );

                    if (src.RequireMegaPack)
                    {
                        VectorMask isMega = VectorEnum256.Equals(
                            packSize,
                            MotelyBoosterPackSize.Mega
                        );
                        isArcana &= isMega;
                        isSpectral &= isMega;
                    }

                    if (split)
                    {
                        if (!ContainsSlot(src.ArcanaPacks, p))
                            isArcana = VectorMask.NoBitsSet;
                        if (!ContainsSlot(src.SpectralPacks, p))
                            isSpectral = VectorMask.NoBitsSet;
                    }

                    if (isArcana.IsPartiallyTrue())
                    {
                        if (!TryUniformSize(packSize, isArcana, out var size))
                            return false;

                        if (!tarotInit)
                        {
                            tarotInit = true;
                            tarotStream = ctx.CreateArcanaPackTarotStream(ante, true);
                        }

                        hasSoulMask |=
                            ctx.GetNextArcanaPackHasTheSoul(ref tarotStream, size) & isArcana;
                    }

                    if (isSpectral.IsPartiallyTrue())
                    {
                        if (!TryUniformSize(packSize, isSpectral, out var size))
                            return false;

                        if (!spectralInit)
                        {
                            spectralInit = true;
                            spectralStream = ctx.CreateSpectralPackSpectralStream(
                                ante,
                                soulOnly: ante != 1
                            );
                        }

                        hasSoulMask |=
                            ctx.GetNextSpectralPackHasTheSoul(ref spectralStream, size)
                            & isSpectral;
                    }
                }
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsSlot(int[] slots, int slot)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == slot)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryUniformSize(
            VectorEnum256<MotelyBoosterPackSize> sizeVector,
            VectorMask laneMask,
            out MotelyBoosterPackSize size
        )
        {
            size = default;
            bool hasAny = false;
            for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
            {
                if (!laneMask[lane])
                    continue;
                var s = sizeVector[lane];
                if (!hasAny)
                {
                    hasAny = true;
                    size = s;
                }
                else if (s != size)
                    return false;
            }
            return hasAny;
        }
    }
}
