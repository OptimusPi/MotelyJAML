using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

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
            return ~_inner.Filter(ref ctx);
        }
    }
}
