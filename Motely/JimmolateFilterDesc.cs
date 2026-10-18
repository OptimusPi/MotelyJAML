using System.Runtime.CompilerServices;

namespace Motely;

/// <summary>
/// Jimmolate: a per-seed predicate ("does this seed pass?"), the classic Immolate <c>.cl</c>
/// filter mental model. Wraps a <see cref="MotelyIndividualSeedSearcher"/> and runs it against
/// every lane's live single-seed context, so the predicate can read vouchers, bosses, shop items
/// or any other stream before returning keep/reject — what an Immolate kernel did, without OpenCL.
/// The predicate is compiled C#, which is the whole reason it is fast: an Immolate <c>.cl</c> filter
/// was compiled into the kernel too, never marshalled per seed.
/// </summary>
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
