using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

/// <summary>
/// Wraps an inner filter and inverts its result (mustNot semantics).
/// Seeds matching any inner filter are REJECTED. Only sound over an inner filter whose SIMD
/// pass is exact (<see cref="JamlScoring.IsExactFilterConfirm"/>); a coarse prefilter negated
/// here would reject every seed that merely might hold the item, so the builder keeps those
/// clauses out of the chain and rejects them in <see cref="JamlShouldScoreDesc"/> instead.
/// </summary>
public struct NegationFilterDesc(IMotelySeedFilterDesc inner)
    : IMotelySeedFilterDesc<NegationFilterDesc.NegationFilter>
{
    private readonly IMotelySeedFilterDesc _inner = inner;

    public NegationFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        return new NegationFilter(_inner.CreateFilter(ref ctx));
    }

    public struct NegationFilter(IMotelySeedFilter inner) : IMotelySeedFilter
    {
        private readonly IMotelySeedFilter _inner = inner;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // Invert: seeds that match the inner filter should be REJECTED
            return ~_inner.Filter(ref ctx);
        }
    }
}
