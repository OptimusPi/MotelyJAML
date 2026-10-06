namespace Motely.Filters;

/// <summary>Accepts every lane: the base of a search whose clauses do all the filtering.</summary>
public struct PassthroughFilterDesc()
    : IMotelySeedFilterDesc<PassthroughFilterDesc.PassthroughFilter>
{
    public readonly PassthroughFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        return new PassthroughFilter();
    }

    public struct PassthroughFilter() : IMotelySeedFilter
    {
        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            return VectorMask.AllBitsSet;
        }
    }
}
