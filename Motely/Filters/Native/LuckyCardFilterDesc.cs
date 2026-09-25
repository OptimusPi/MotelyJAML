using System.Runtime.Intrinsics;

namespace Motely.Filters.Native;

public struct LuckCardFilterDesc() : IMotelySeedFilterDesc<LuckCardFilterDesc.LuckyCardFilter>
{
    public readonly LuckyCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        ctx.CachePseudoHash("lucky_money");
        return new LuckyCardFilter();
    }

    public struct LuckyCardFilter() : IMotelySeedFilter
    {
        public readonly VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            MotelyVectorPrngStream luckyMoney = searchContext.CreatePrngStream("lucky_money");

            VectorMask mask = VectorMask.AllBitsSet;
            Vector512<double> values;

            for (int i = 0; i < 15; i++)
            {
                values = searchContext.GetNextRandom(ref luckyMoney);

                mask &= Vector512.LessThan(values, Vector512.Create(1d / 4d));

                if (mask.IsAllFalse())
                {
                    break;
                }
            }

            return mask;
        }
    }
}
