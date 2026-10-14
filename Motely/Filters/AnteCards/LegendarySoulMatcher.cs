using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters;

/// <summary>
/// The exact Soul / legendary walk (scalar verification pass; the SIMD filter only finds Souls).
/// Packs are walked in the slot numbering every other family uses —
/// <see cref="MotelySingleSearchContext.CreateBoosterPackStream(int, bool)"/>'s default, where ante 1
/// slot 0 is the fixed first-shop Buffoon — and The Soul is read off the Arcana / Spectral streams in
/// pack order.
///
/// The legendary face is not per ante. Balatro keys the pool <c>Joker4</c> with no ante, so the Nth
/// Soul opened in the run gets draw N. A <see cref="SoulWalk"/> carries that one stream (and the
/// opened-Soul count) across every ante the clause walks; each targeted Soul is opened and advances
/// it. The edition comes from the ante's own <c>edisou{ante}</c> stream, read once per Soul opened there.
/// </summary>
internal static class LegendarySoulMatcher
{
    /// <summary>State that lives across antes for one clause walk.</summary>
    internal struct SoulWalk(MotelySingleLegendaryJokerStream faces)
    {
        public MotelySingleLegendaryJokerStream Faces = faces;

        /// <summary>Souls opened so far — the next Soul's index for <see cref="LegendaryJokerClause.Rolls"/>.</summary>
        public int Opened;

        /// <summary>Set once every listed roll has been passed; the walk can stop.</summary>
        public bool Done;
    }

    /// <summary>
    /// Opens every targeted Soul in <paramref name="ante"/> (slots 0..<paramref name="maxBoosterPack"/>)
    /// and returns how many match the clause. Antes must be walked in ascending order with the same
    /// <paramref name="walk"/>.
    /// </summary>
    internal static int CountAnte(
        ref MotelySingleSearchContext ctx,
        int ante,
        LegendaryJokerClause clause,
        int maxBoosterPack,
        ref SoulWalk walk
    )
    {
        // Null Sources (no sources: block) → legendary defaults; a non-null block is used as-is.
        var src = clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources;
        int lastRoll = clause.Rolls.Length > 0 ? MaxOf(clause.Rolls) : int.MaxValue;

        var packStream = ctx.CreateBoosterPackStream(ante);

        MotelySingleTarotStream tarotStream = default;
        MotelySingleSpectralStream spectralStream = default;
        bool tarotInit = false;
        bool spectralInit = false;
        MotelySingleJokerFixedRarityStream editionStream = default;
        bool editionInit = false;

        int count = 0;

        for (int p = 0; p <= maxBoosterPack; p++)
        {
            var pack = ctx.GetNextBoosterPack(ref packStream);
            var type = pack.GetPackType();

            bool hasSoul;
            if (type == MotelyBoosterPackType.Arcana)
            {
                if (!tarotInit)
                {
                    tarotInit = true;
                    tarotStream = ctx.CreateArcanaPackTarotStream(ante, true);
                }

                hasSoul = ctx.GetNextArcanaPackHasTheSoul(ref tarotStream, pack.GetPackSize());
            }
            else if (type == MotelyBoosterPackType.Spectral)
            {
                if (!spectralInit)
                {
                    spectralInit = true;
                    // PerkeoObservatory: ante 1 Spectral uses soulOnly false; ante 2+ uses true.
                    spectralStream = ctx.CreateSpectralPackSpectralStream(ante, soulOnly: ante != 1);
                }

                hasSoul = ctx.GetNextSpectralPackHasTheSoul(ref spectralStream, pack.GetPackSize());
            }
            else
            {
                continue;
            }

            bool isTarget =
                IsBoosterSlotTargetForLegendary(src, p, pack)
                && (!src.RequireMegaPack || pack.GetPackSize() == MotelyBoosterPackSize.Mega);
            if (!hasSoul || !isTarget)
                continue;

            // The player opens this Soul.
            int roll = walk.Opened++;
            bool counted = clause.Rolls.Length == 0 || ArrayContains(clause.Rolls, roll);

            if (clause.SoulCardOnly)
            {
                if (counted)
                    count++;
            }
            else
            {
                if (!editionInit)
                {
                    editionInit = true;
                    editionStream = ctx.CreateLegendaryJokerStream(
                        ante,
                        MotelyJokerFixedRarityStreamFlags.ExcludeJokerType
                    );
                }

                MotelyJoker face = ctx.GetNextLegendaryJoker(ref walk.Faces, clause.With.Jokers);
                MotelyItemEdition edition = ctx.GetNextJoker(ref editionStream).Edition;
                if (counted && LegendaryJokerMatches(clause, face, edition))
                    count++;
            }

            if (roll >= lastRoll)
            {
                walk.Done = true;
                return count;
            }
        }

        return count;
    }

    /// <summary>
    /// Split mode (non-empty arcana and/or Spectral slot lists): only those paths count.
    /// Otherwise <see cref="LegendaryJokerSourceConfig.BoosterPacks"/> — slot matches regardless of
    /// rolled pack type (arcana/Spectral branches still gate The Soul).
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
            return ArrayContains(src.BoosterPacks, p);

        return pack.GetPackType() switch
        {
            MotelyBoosterPackType.Arcana => ArrayContains(src.ArcanaPacks, p),
            MotelyBoosterPackType.Spectral => ArrayContains(src.SpectralPacks, p),
            _ => false,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool LegendaryJokerMatches(
        LegendaryJokerClause clause,
        MotelyJoker face,
        MotelyItemEdition edition
    )
    {
        if (clause.Edition.HasValue && edition != clause.Edition.Value)
            return false;
        return clause.Jokers.Length == 0 || ArrayContains(clause.Jokers, face);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ArrayContains<T>(T[] array, T value)
        where T : struct
    {
        for (int i = 0; i < array.Length; i++)
        {
            if (EqualityComparer<T>.Default.Equals(array[i], value))
                return true;
        }

        return false;
    }

    private static int MaxOf(int[] values)
    {
        int max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > max)
                max = values[i];
        }

        return max;
    }
}
