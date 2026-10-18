using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters;

/// <summary>
/// Handles the two "special" spectral cards — <see cref="MotelySpectralCard.TheSoul"/> and
/// <see cref="MotelySpectralCard.BlackHole"/> — that the normal <see cref="SpectralCardFilterDesc"/>
/// cannot see correctly. Both are emitted by a dedicated soul/black-hole roll (random &gt; 0.997) in
/// pack types the spectral content stream never reads:
/// <list type="bullet">
///   <item>TheSoul → <b>Arcana</b> packs (tarot stream) AND Spectral packs.</item>
///   <item>BlackHole → <b>Celestial</b> packs (planet stream) AND Spectral packs.</item>
/// </list>
/// The vector spectral-content path also resamples Soul/BlackHole away, so the only faithful read is
/// the scalar generators. This filter therefore does a cheap, over-permissive SIMD narrow (a pack type
/// the card can spawn in is present) and then confirms the exact count per surviving seed via
/// <see cref="MotelyVectorSearchContext.SearchIndividualSeeds"/> + the scalar spectral counter in
/// <see cref="ClauseScoring"/> (which walks Arcana/Celestial packs too). Same shape as
/// <see cref="Motely.Filters.Native.TwoBlackHoleFilterDesc"/>. A clause with no <c>sources:</c>
/// resolves to <see cref="SpectralCardFilterDesc.DefaultSpecialSources"/> — pack slots only, see
/// <see cref="SpectralCardFilterDesc.ResolveSources"/> — on both the narrow and the confirm.
///
/// <b>KEEP — real SIMD, not dead code.</b> Reuses <see cref="SpectralCardClause"/> (no separate
/// JAML keyword). Live route: <c>spectralCard:</c> → <see cref="Handles"/> true →
/// <see cref="SpectralCardClause.CreateFilterDesc"/> installs this filter instead of
/// <see cref="SpectralCardFilterDesc"/>. Gate is <see cref="Handles"/> /
/// <see cref="ClauseScoring.TargetsSpecialSpectral"/> — leave this type on the tree.
/// </summary>
public struct SpecialSpectralCardFilterDesc(SpectralCardClause clause)
    : IMotelySeedFilterDesc<SpecialSpectralCardFilterDesc.SpecialSpectralCardFilter>
{
    private readonly SpectralCardClause _clause = clause;

    /// <summary>Whether this clause should be routed to the special path (targets TheSoul or BlackHole).</summary>
    public static bool Handles(SpectralCardClause clause) =>
        ClauseScoring.TargetsSpecialSpectral(clause);

    public SpecialSpectralCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
            ctx.CacheBoosterPackStream(ante);
        return new SpecialSpectralCardFilter(_clause);
    }

    public struct SpecialSpectralCardFilter(SpectralCardClause clause) : IMotelySeedFilter
    {
        private readonly SpectralCardClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;

            // The narrow below only sees packs. A clause that also reads the shop, Sixth Sense or
            // Séance can match on a seed with no Arcana/Spectral/Celestial pack at all (a
            // `spectralCard: [TheSoul, Incantation]` hit from a Séance roll), so it goes straight
            // to the exact per-seed count.
            var sources = SpectralCardFilterDesc.ResolveSources(clause);
            if (sources.ShopItems.Length > 0 || sources.SixthSense.Length > 0 || sources.Seance.Length > 0)
            {
                return ctx.SearchIndividualSeeds(
                    (MotelySingleSearchContext single) =>
                        ClauseScoring.ClauseMeetsMinForFilter(ref single, clause) ? 1 : 0
                );
            }

            // Cheap SIMD narrow: keep only lanes that have at least one pack a special card can spawn
            // in (Arcana / Spectral / Celestial) somewhere in the requested antes. Deliberately
            // over-permissive — iterate the full pack range (ante 1 up to its Hieroglyph-extended
            // slots) and ignore the per-lane reachability clamp here, because a too-tight narrow
            // would drop real matches before the exact scalar count runs.
            VectorMask relevant = VectorMask.NoBitsSet;
            foreach (var ante in clause.Antes)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                int maxPackSlot =
                    ante == 1
                        ? SimdPackSupport.Ante1ExtendedMaxPackSlot
                        : MotelyGlobals.LateAntesMaxPackSlot;
                for (int p = 0; p <= maxPackSlot; p++)
                {
                    var packType = ctx.GetNextBoosterPack(ref packStream).GetPackType();
                    relevant |=
                        VectorEnum256.Equals(packType, MotelyBoosterPackType.Arcana)
                        | VectorEnum256.Equals(packType, MotelyBoosterPackType.Spectral)
                        | VectorEnum256.Equals(packType, MotelyBoosterPackType.Celestial);
                }
            }

            if (relevant.IsAllFalse())
                return VectorMask.NoBitsSet;

            return ctx.SearchIndividualSeeds(
                relevant,
                (MotelySingleSearchContext single) =>
                    ClauseScoring.ClauseMeetsMinForFilter(ref single, clause) ? 1 : 0
            );
        }
    }
}
