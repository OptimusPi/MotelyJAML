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
/// A Charm Tag Mega Arcana (<c>sources.tagPacks</c>) opens at blind select, on the same
/// <c>soul_Tarot{ante}</c> stream: before every shop pack in ante 1, after the previous boss's shop
/// (slots 0..1) in antes 2+ (<see cref="MotelyGlobals.TagPackInsertSlot"/>).
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

        /// <summary>
        /// Legendaries already drawn in this walk (Balatro's <c>used_jokers</c>). The pool has five
        /// members; once all five are held a Soul draws <c>j_joker</c>, which <see cref="Own"/> drops, so
        /// five slots are enough. <see cref="HeldCount"/> is the live length —
        /// <see cref="GetNextLegendaryJoker"/> reads only that slice.
        /// </summary>
        public MotelyJoker[] Held = new MotelyJoker[5];

        /// <summary>Live length of <see cref="Held"/>.</summary>
        public int HeldCount;

        /// <summary>Record a legendary draw so later draws resample off it (when Showman is off).</summary>
        public void Own(MotelyJoker face)
        {
            if (HeldCount < Held.Length)
                Held[HeldCount++] = face;
        }
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
        ref SoulWalk walk,
        MotelyRunState runState
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

        // Charm Tag Mega Arcanas open at blind select (E1), on the same soul_Tarot<ante> stream the
        // shop packs use, so they sit between shop slots: before slot 0 in ante 1, after the
        // previous boss's shop (slots 0..1) in antes 2+. Those earlier slots are walked even when
        // the clause targets none of them, so the Charm reads the draws the game gives it.
        bool tagPending = src.TagPacks.Length > 0;
        int tagSlot = tagPending ? MotelyGlobals.TagPackInsertSlot(ante) : -1;
        int lastSlot = tagPending && tagSlot - 1 > maxBoosterPack ? tagSlot - 1 : maxBoosterPack;

        for (int p = 0; p <= lastSlot; p++)
        {
            if (tagPending && p == tagSlot)
            {
                tagPending = false;
                count += CountTagPacks(
                    ref ctx,
                    clause,
                    src,
                    ante,
                    lastRoll,
                    ref walk,
                    runState,
                    ref tarotStream,
                    ref tarotInit,
                    ref editionStream,
                    ref editionInit,
                    out bool tagDone
                );
                if (tagDone)
                    return count;
            }

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
                p <= maxBoosterPack
                && IsBoosterSlotTargetForLegendary(src, p, pack)
                && (!src.RequireMegaPack || pack.GetPackSize() == MotelyBoosterPackSize.Mega);
            if (!hasSoul || !isTarget)
                continue;

            // The player opens this Soul.
            count += OpenSoul(
                ref ctx,
                clause,
                ante,
                lastRoll,
                ref walk,
                runState,
                ref editionStream,
                ref editionInit,
                out bool done
            );
            if (done)
                return count;
        }

        if (tagPending)
        {
            count += CountTagPacks(
                ref ctx,
                clause,
                src,
                ante,
                lastRoll,
                ref walk,
                runState,
                ref tarotStream,
                ref tarotInit,
                ref editionStream,
                ref editionInit,
                out _
            );
        }

        return count;
    }

    /// <summary>
    /// Opens the ante's Charm Tag Mega Arcanas in order (<c>sources.tagPacks</c>: 0 = the ante's
    /// first Charm, 1 = the second; listing 1 assumes Charm 0 was opened too). Each takes the next
    /// five draws of <paramref name="tarotStream"/>; only listed Charms open their Soul.
    /// </summary>
    private static int CountTagPacks(
        ref MotelySingleSearchContext ctx,
        LegendaryJokerClause clause,
        LegendaryJokerSourceConfig src,
        int ante,
        int lastRoll,
        ref SoulWalk walk,
        MotelyRunState runState,
        ref MotelySingleTarotStream tarotStream,
        ref bool tarotInit,
        ref MotelySingleJokerFixedRarityStream editionStream,
        ref bool editionInit,
        out bool done
    )
    {
        done = false;
        int count = 0;
        int maxTagPack = MaxOf(src.TagPacks);
        for (int tagPack = 0; tagPack <= maxTagPack; tagPack++)
        {
            if (!tarotInit)
            {
                tarotInit = true;
                tarotStream = ctx.CreateArcanaPackTarotStream(ante, true);
            }

            bool hasTagSoul = ctx.GetNextArcanaPackHasTheSoul(
                ref tarotStream,
                MotelyBoosterPackSize.Mega
            );
            if (!hasTagSoul || !ArrayContains(src.TagPacks, tagPack))
                continue;

            count += OpenSoul(
                ref ctx,
                clause,
                ante,
                lastRoll,
                ref walk,
                runState,
                ref editionStream,
                ref editionInit,
                out done
            );
            if (done)
                return count;
        }

        return count;
    }

    /// <summary>
    /// Opens one targeted Soul: advances the run-wide <c>Joker4</c> walk, records the face as owned,
    /// and returns how many Souls the clause counts (0 or 1). <paramref name="done"/> is set once the
    /// walk has passed every listed roll, so the caller can stop.
    /// </summary>
    private static int OpenSoul(
        ref MotelySingleSearchContext ctx,
        LegendaryJokerClause clause,
        int ante,
        int lastRoll,
        ref SoulWalk walk,
        MotelyRunState runState,
        ref MotelySingleJokerFixedRarityStream editionStream,
        ref bool editionInit,
        out bool done
    )
    {
        int roll = walk.Opened++;
        bool counted = clause.Rolls.Length == 0 || ArrayContains(clause.Rolls, roll);
        int count = 0;

        if (clause.SoulCardOnly)
        {
            if (counted)
                count = 1;
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

            MotelyJoker face = ctx.GetNextLegendaryJoker(
                ref walk.Faces,
                walk.Held.AsSpan(0, walk.HeldCount),
                runState.ShowmanActive
            );
            walk.Own(face);
            MotelyItemEdition edition = ctx.GetNextJoker(ref editionStream).Edition;
            if (counted && LegendaryJokerMatches(clause, face, edition))
                count = 1;
        }

        done = roll >= lastRoll;
        if (done)
            walk.Done = true;
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
