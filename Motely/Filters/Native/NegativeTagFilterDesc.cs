using System.Runtime.CompilerServices;

namespace Motely.Filters.Native;

public struct NegativeTagFilterDesc()
    : IMotelySeedFilterDesc<NegativeTagFilterDesc.NegativeTagFilter>
{
    public readonly NegativeTagFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        for (int ante = 2; ante <= 4; ante++)
            ctx.CacheTagStream(ante);

        var filter = new NegativeTagFilter();
        return filter;
    }

    public struct NegativeTagFilter() : IMotelySeedFilter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            MotelyVectorTagStream tagStream;
            VectorMask mask = VectorMask.AllBitsSet;

            for (int ante = 2; ante <= 8; ante++)
            {
                tagStream = searchContext.CreateTagStream(ante, true);

                mask &= VectorEnum256.Equals(
                    searchContext.GetNextTag(ref tagStream),
                    MotelyTag.NegativeTag
                );

                if (mask.IsAllFalse())
                    break;

                mask &= VectorEnum256.Equals(
                    searchContext.GetNextTag(ref tagStream),
                    MotelyTag.NegativeTag
                );

                if (mask.IsAllFalse())
                    break;
            }

            return mask;
        }
    }
}
