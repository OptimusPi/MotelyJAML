using System;
using Motely.Filters;

namespace Motely.Filters
{
    /// <summary>
    /// Simple wrapper that inverts the result of an inner filter descriptor.
    /// Used to implement MustNot by reusing existing verified specialized filters.
    /// </summary>
    public struct MotelyJsonInvertFilterDesc(IMotelySeedFilterDesc inner)
        : IMotelySeedFilterDesc<MotelyJsonInvertFilterDesc.MotelyJsonInvertFilter>
    {
        private readonly IMotelySeedFilterDesc _inner = inner;

        public MotelyJsonInvertFilter CreateFilter(ref MotelyFilterCreationContext ctx)
        {
            // Ensure the inner specialized filter registers any required pseudo-hash
            // key lengths by creating it in a non-additional-filter context.
            bool prevFlag = ctx.IsAdditionalFilter;
            ctx.IsAdditionalFilter = false;
            var innerFilter = _inner.CreateFilter(ref ctx);
            ctx.IsAdditionalFilter = prevFlag;

            DebugLogger.Log(
                $"[INVERT DESC] Created inner filter of type={innerFilter.GetType().Name}"
            );
            return new MotelyJsonInvertFilter(innerFilter);
        }

        public readonly struct MotelyJsonInvertFilter : IMotelySeedFilter
        {
            private readonly IMotelySeedFilter _innerFilter;

            public MotelyJsonInvertFilter(IMotelySeedFilter innerFilter)
            {
                _innerFilter = innerFilter ?? throw new ArgumentNullException(nameof(innerFilter));
            }

            public VectorMask Filter(ref MotelyVectorSearchContext ctx)
            {
                var m = _innerFilter.Filter(ref ctx);
                DebugLogger.Log($"[INVERT FILTER] inner mask=0x{m.Value:X2}");

                // Only invert bits for VALID lanes!
                // Invalid lanes must remain 0 (rejected) regardless of inner filter result.
                // Build valid lane mask by checking each lane
                uint validLaneMask = 0;
                for (int lane = 0; lane < 8; lane++)
                {
                    if (ctx.IsLaneValid(lane))
                    {
                        validLaneMask |= (1u << lane);
                    }
                }

                // Invert only the valid lanes: valid lanes with 0 become 1, valid lanes with 1 become 0
                // Invalid lanes stay 0 (validLaneMask will mask them out)
                uint inverted = (~m.Value) & validLaneMask;

                DebugLogger.Log(
                    $"[INVERT FILTER] valid lanes=0x{validLaneMask:X2}, inverted mask=0x{inverted:X2}"
                );
                return new VectorMask(inverted);
            }
        }
    }
}
