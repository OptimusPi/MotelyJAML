using System.Runtime.CompilerServices;

namespace Motely.Filters.Native;

public readonly struct JimmolateFilterDesc(MotelyIndividualSeedSearcher searcher, int scoreCutoff = 1)
    : IMotelySeedFilterDesc<JimmolateFilterDesc.JimmolateFilter>
{
    private readonly MotelyIndividualSeedSearcher _searcher = searcher;
    private readonly int _scoreCutoff = scoreCutoff;

    public readonly JimmolateFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
        new(_searcher, _scoreCutoff);

    public readonly struct JimmolateFilter(MotelyIndividualSeedSearcher searcher, int scoreCutoff)
        : IMotelySeedFilter
    {
        private readonly MotelyIndividualSeedSearcher _searcher = searcher;
        private readonly int _scoreCutoff = scoreCutoff;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx) =>
            ctx.SearchIndividualSeeds(_searcher, _scoreCutoff);
    }
}
