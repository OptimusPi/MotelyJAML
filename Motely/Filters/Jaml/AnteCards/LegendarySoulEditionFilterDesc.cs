using System.Diagnostics;
using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters.Jaml;

public struct LegendarySoulEditionFilterDesc(LegendaryJokerClause clause)
    : IMotelySeedFilterDesc<LegendarySoulEditionFilterDesc.LegendarySoulEditionFilter>
{
    private readonly LegendaryJokerClause _clause = clause;

    public readonly LegendarySoulEditionFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        Debug.Assert(
            _clause.Edition.HasValue,
            "Soul edition filter requires an edition on the clause."
        );

        foreach (var ante in _clause.Antes)
            ctx.CacheLegendaryJokerStream(
                ante,
                MotelyJokerFixedRarityStreamFlags.ExcludeJokerType
                    | MotelyJokerFixedRarityStreamFlags.ExcludeStickers,
                force: true
            );

        return new LegendarySoulEditionFilter(_clause);
    }

    public readonly struct LegendarySoulEditionFilter(LegendaryJokerClause clause)
        : IMotelySeedFilter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            uint laneMask = 0;
            for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
            {
                if (ctx.IsLaneValid(lane))
                    laneMask |= 1u << lane;
            }

            return LegendarySoulEditionPrefilter.Apply(ref ctx, clause, laneMask);
        }
    }
}
