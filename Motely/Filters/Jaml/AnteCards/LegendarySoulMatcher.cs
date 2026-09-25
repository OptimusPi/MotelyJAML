using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters.Jaml;

internal static class LegendarySoulMatcher
{
    internal static bool MatchAnte(
        ref MotelySingleSearchContext ctx,
        int ante,
        LegendaryJokerClause clause,
        int maxBoosterPack
    ) => CountAnte(ref ctx, ante, clause, maxBoosterPack, stopAfterFirstMatch: true) > 0;

    internal static int CountAnte(
        ref MotelySingleSearchContext ctx,
        int ante,
        LegendaryJokerClause clause,
        int maxBoosterPack,
        bool stopAfterFirstMatch = false
    )
    {
        var src = clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources;

        var packStream = ctx.CreateBoosterPackStream(ante);

        MotelySingleTarotStream tarotStream = default;
        MotelySingleSpectralStream spectralStream = default;
        bool tarotInit = false;
        bool spectralInit = false;
        MotelySingleJokerFixedRarityStream soulStream = default;
        bool soulStreamInited = false;

        int count = 0;

        for (int p = 0; p <= maxBoosterPack; p++)
        {
            var pack = ctx.GetNextBoosterPack(ref packStream);

            bool isTarget =
                IsBoosterSlotTargetForLegendary(src, p, pack)
                && (!src.RequireMegaPack || pack.GetPackSize() == MotelyBoosterPackSize.Mega);

            if (pack.GetPackType() == MotelyBoosterPackType.Arcana)
            {
                if (!tarotInit)
                {
                    tarotInit = true;
                    tarotStream = ctx.CreateArcanaPackTarotStream(ante, true);
                }

                bool hasSoul = ctx.GetNextArcanaPackHasTheSoul(ref tarotStream, pack.GetPackSize());

                if (!isTarget || !hasSoul)
                    continue;

                if (clause.SoulCardOnly)
                {
                    count++;
                    if (stopAfterFirstMatch)
                        return count;
                    continue;
                }

                if (!soulStreamInited)
                {
                    soulStream = ctx.CreateLegendaryJokerStream(ante);
                    soulStreamInited = true;
                }

                var legendaryJoker = ctx.GetNextJoker(ref soulStream);
                if (LegendaryJokerMatchesFull(clause, legendaryJoker))
                {
                    count++;
                    if (stopAfterFirstMatch)
                        return count;
                }
            }
            else if (pack.GetPackType() == MotelyBoosterPackType.Spectral)
            {
                if (!spectralInit)
                {
                    spectralInit = true;
                    spectralStream = ctx.CreateSpectralPackSpectralStream(
                        ante,
                        soulOnly: ante != 1
                    );
                }

                bool hasSoul = ctx.GetNextSpectralPackHasTheSoul(
                    ref spectralStream,
                    pack.GetPackSize()
                );

                if (!isTarget || !hasSoul)
                    continue;

                if (clause.SoulCardOnly)
                {
                    count++;
                    if (stopAfterFirstMatch)
                        return count;
                    continue;
                }

                if (!soulStreamInited)
                {
                    soulStream = ctx.CreateLegendaryJokerStream(ante);
                    soulStreamInited = true;
                }

                var legendaryJoker = ctx.GetNextJoker(ref soulStream);
                if (LegendaryJokerMatchesFull(clause, legendaryJoker))
                {
                    count++;
                    if (stopAfterFirstMatch)
                        return count;
                }
            }
        }

        return count;
    }

    internal static bool MatchAnteShopPackHasSoulOnly(
        ref MotelySingleSearchContext ctx,
        int ante,
        LegendaryJokerSourceConfig src,
        int maxBoosterPack
    )
    {
        var packStream = ctx.CreateBoosterPackStream(ante);

        MotelySingleTarotStream tarotStream = default;
        MotelySingleSpectralStream spectralStream = default;
        bool tarotInit = false;
        bool spectralInit = false;

        for (int p = 0; p <= maxBoosterPack; p++)
        {
            var pack = ctx.GetNextBoosterPack(ref packStream);

            bool isTarget =
                IsBoosterSlotTargetForLegendary(src, p, pack)
                && (!src.RequireMegaPack || pack.GetPackSize() == MotelyBoosterPackSize.Mega);

            if (pack.GetPackType() == MotelyBoosterPackType.Arcana)
            {
                if (!tarotInit)
                {
                    tarotInit = true;
                    tarotStream = ctx.CreateArcanaPackTarotStream(ante, true);
                }

                bool hasSoul = ctx.GetNextArcanaPackHasTheSoul(ref tarotStream, pack.GetPackSize());
                if (hasSoul && isTarget)
                    return true;
            }
            else if (pack.GetPackType() == MotelyBoosterPackType.Spectral)
            {
                if (!spectralInit)
                {
                    spectralInit = true;
                    spectralStream = ctx.CreateSpectralPackSpectralStream(
                        ante,
                        soulOnly: ante != 1
                    );
                }

                bool hasSoul = ctx.GetNextSpectralPackHasTheSoul(
                    ref spectralStream,
                    pack.GetPackSize()
                );
                if (hasSoul && isTarget)
                    return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsBoosterSlotTargetForLegendary(
        LegendaryJokerSourceConfig src,
        int p,
        MotelyBoosterPack pack
    )
    {
        bool split = src.ArcanaPacks.Length > 0 || src.SpectralPacks.Length > 0;
        if (!split)
        {
            for (int i = 0; i < src.BoosterPacks.Length; i++)
            {
                if (src.BoosterPacks[i] == p)
                    return true;
            }

            return false;
        }

        var type = pack.GetPackType();
        if (type == MotelyBoosterPackType.Arcana)
        {
            for (int i = 0; i < src.ArcanaPacks.Length; i++)
            {
                if (src.ArcanaPacks[i] == p)
                    return true;
            }

            return false;
        }

        if (type == MotelyBoosterPackType.Spectral)
        {
            for (int i = 0; i < src.SpectralPacks.Length; i++)
            {
                if (src.SpectralPacks[i] == p)
                    return true;
            }

            return false;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TypeMatchesLegendary(LegendaryJokerClause clause, MotelyItemType ty)
    {
        var jokers = JamlDisc.OrEmpty(clause.Jokers);
        for (int i = 0; i < jokers.Length; i++)
        {
            if (ty == (MotelyItemType)((int)MotelyItemTypeCategory.Joker | (int)jokers[i]))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool LegendaryJokerMatchesFull(
        LegendaryJokerClause clause,
        Motely.MotelyItem joker
    )
    {
        if (JamlDisc.IsCategoryAny(clause.Jokers))
            return !clause.Edition.HasValue || joker.Edition == clause.Edition.Value;

        if (!TypeMatchesLegendary(clause, joker.Type))
            return false;
        return !clause.Edition.HasValue || joker.Edition == clause.Edition.Value;
    }
}
