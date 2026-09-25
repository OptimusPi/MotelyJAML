using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

public struct SpecialSpectralCardFilterDesc(SpectralCardClause clause)
    : IMotelySeedFilterDesc<SpecialSpectralCardFilterDesc.SpecialSpectralCardFilter>
{
    private readonly SpectralCardClause _clause = clause;

    public static bool Handles(SpectralCardClause clause) =>
        JamlScoring.TargetsSpecialSpectral(clause);

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

            VectorMask relevant = VectorMask.NoBitsSet;
            foreach (var ante in clause.Antes)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                for (int p = 0; p <= MotelyGlobals.LateAntesMaxPackSlot; p++)
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
                    JamlScoring.ClauseMeetsMinForFilter(ref single, clause) ? 1 : 0
            );
        }
    }
}
