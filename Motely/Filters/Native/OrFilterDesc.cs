using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Motely.Filters.Jaml;
using static Motely.MotelyVectorUtils;

namespace Motely.Filters;

[JamlDiscriminator("or")]
[YamlObject]
public sealed partial class OrClause : LogicClause { }

public static class MotelySeedFilterDescExtensions
{
    public static IMotelySeedFilterDesc Or(
        this IMotelySeedFilterDesc first,
        IMotelySeedFilterDesc second
    )
    {
        return new OrFilterDesc([first, second]);
    }

    public static IMotelySeedFilterDesc And(
        this IMotelySeedFilterDesc first,
        IMotelySeedFilterDesc second
    )
    {
        return new AndFilterDesc([first, second]);
    }
}

public struct OrFilterDesc(IMotelySeedFilterDesc[] filters, int min = 1)
    : IMotelySeedFilterDesc<OrFilterDesc.OrFilter>
{
    private readonly IMotelySeedFilterDesc[] _filters = filters;
    private readonly int _min = min;

    public OrFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var childFilters = new IMotelySeedFilter[_filters.Length];
        for (int i = 0; i < _filters.Length; i++)
            childFilters[i] = _filters[i].CreateFilter(ref ctx);
        return new OrFilter(childFilters, _min);
    }

    public struct OrFilter(IMotelySeedFilter[] filters, int min) : IMotelySeedFilter
    {
        private readonly IMotelySeedFilter[] _filters = filters;
        private readonly int _min = min;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            if (_min == 1) // Optimization for standard OR
            {
                var mask = VectorMask.NoBitsSet;
                var len = _filters.Length;
                for (int i = 0; i < len; i++)
                    mask |= _filters[i].Filter(ref ctx);
                return mask;
            }

            var count = Vector256<int>.Zero;
            var filters = _filters;
            var len2 = filters.Length;
            for (int i = 0; i < len2; i++)
            {
                var mask = filters[i].Filter(ref ctx);
                count = Vector256.Add(
                    count,
                    Vector256.ConditionalSelect(
                        VectorMaskToConditionalSelectMask(mask),
                        Vector256<int>.One,
                        Vector256<int>.Zero
                    )
                );
            }
            return Vector256.GreaterThanOrEqual(count, Vector256.Create(_min));
        }
    }
}
