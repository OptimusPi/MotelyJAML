using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters.Jaml;

/// <summary>
/// Soul / legendary checks must follow the same pack order and RNG order as
/// <see cref="PerkeoObservatoryFilterDesc"/> (pack stream with generated-first for ante 1,
/// then The Soul in arcana/Spectral, then <see cref="MotelySingleSearchContext.GetNextJoker"/>).
/// The older "read soul stream before packs" path mis-aligned streams and matched nothing.
/// </summary>
internal static class LegendarySoulMatcher
{
    internal static bool MatchAnte(
        ref MotelySingleSearchContext ctx,
        int ante,
        LegendaryJokerClause clause,
        int maxBoosterPack
    ) => CountAnte(ref ctx, ante, clause, maxBoosterPack, stopAfterFirstMatch: true) > 0;

    /// <summary>
    /// Walks every targeted arcana/Spectral pack in <paramref name="ante"/> and returns the total
    /// number of soul-legendary matches. Soul stream must be consumed once per soul seen regardless
    /// of whether the joker matches the clause, so short-circuiting mid-ante would misalign later antes
    /// (and, more visibly, hide the second soul in antes like ALEEB's 1–2 with double legendaries).
    /// </summary>
    internal static int CountAnte(
        ref MotelySingleSearchContext ctx,
        int ante,
        LegendaryJokerClause clause,
        int maxBoosterPack,
        bool stopAfterFirstMatch = false
    )
    {
        // Null Sources (no sources: block) → legendary defaults; a non-null block is used as-is.
        var src = clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources;

        // Default CreateBoosterPackStream(ante) uses generatedFirstPack = (ante > 1), so ante 1
        // prepends a synthetic Buffoon — indices and pack types no longer match PerkeoObservatory.
        var packStream = ctx.CreateBoosterPackStream(ante, true, false);

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
                    // PerkeoObservatory: ante 1 Spectral uses soulOnly false; ante 2+ uses true.
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

    /// <summary>
    /// Soul-card-only path: same pack walk / slot rules as <see cref="MatchAnte"/>, returns on first The Soul in a targeted arcana/Spectral pack.
    /// </summary>
    internal static bool MatchAnteShopPackHasSoulOnly(
        ref MotelySingleSearchContext ctx,
        int ante,
        LegendaryJokerSourceConfig src,
        int maxBoosterPack
    )
    {
        var packStream = ctx.CreateBoosterPackStream(ante, true, false);

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

    /// <summary>
    /// Split mode (non-empty arcana and/or Spectral slot lists): only those paths count.
    /// Legacy mode: <see cref="LegendaryJokerSourceConfig.BoosterPacks"/> — slot matches regardless of rolled pack type
    /// (arcana/Spectral branches still gate The Soul).
    /// </summary>
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

    /// <summary>Whether <paramref name="ty"/> matches one of the clause's legendary jokers (no allocation).</summary>
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
