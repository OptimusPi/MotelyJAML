using System.Diagnostics;
using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters;

/// <summary>
/// Fast vector filter: legendary soul stream edition only (ExcludeJokerType | ExcludeStickers).
///
/// <b>KEEP — real SIMD, not dead code.</b> Live <c>legendaryJoker:</c> + <c>edition:</c> uses the same
/// work via <see cref="LegendarySoulEditionPrefilter"/> inside <see cref="LegendaryJokerFilterDesc"/>
/// as a pure vector mask (no <c>SearchIndividualSeeds</c>). Pack/Soul exact law is scoring must re-eval.
/// This desc is the standalone composition form of that edition slice for <c>WithAdditionalFilter</c>.
/// </summary>
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
