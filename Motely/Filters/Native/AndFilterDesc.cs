using System;
using System.Runtime.CompilerServices;
using Motely.Filters.Jaml;

namespace Motely.Filters;

[JamlDiscriminator("and")]
[YamlObject]
public sealed partial class AndClause : LogicClause { }

public struct AndFilterDesc(IMotelySeedFilterDesc[] filters)
    : IMotelySeedFilterDesc<AndFilterDesc.AndFilter>
{
    private readonly IMotelySeedFilterDesc[] _filters = filters;

    public AndFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var childFilters = new IMotelySeedFilter[_filters.Length];
        for (int i = 0; i < _filters.Length; i++)
            childFilters[i] = _filters[i].CreateFilter(ref ctx);
        return new AndFilter(childFilters);
    }

    public struct AndFilter(IMotelySeedFilter[] filters) : IMotelySeedFilter
    {
        private readonly IMotelySeedFilter[] _filters = filters;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var mask = VectorMask.AllBitsSet;
            var filters = _filters;
            for (int i = 0; i < filters.Length; i++)
            {
                mask &= filters[i].Filter(ref ctx);
            }
            return mask;
        }
    }
}
