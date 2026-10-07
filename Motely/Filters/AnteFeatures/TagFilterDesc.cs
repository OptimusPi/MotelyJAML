using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters;

public partial class TagClause : IMotelyClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyTag[] Tags { get; set; } = [];

    /// <summary>
    /// Tag-stream draw indices per ante: 0 = small-blind offer, 1 = big-blind offer,
    /// 2+ = further draws on the same ante stream (replay / double-tag extras).
    /// </summary>
    public int[] Rolls { get; set; } = [0, 1];

    public IMotelySeedFilterDesc CreateFilterDesc() => new TagFilterDesc(this);
}

/// <summary>The small blind's tag offer only.</summary>
public sealed class SmallBlindTagClause : TagClause
{
    public SmallBlindTagClause() => Rolls = [0];
}

/// <summary>The big blind's tag offer only.</summary>
public sealed class BigBlindTagClause : TagClause
{
    public BigBlindTagClause() => Rolls = [1];
}

public struct TagFilterDesc(TagClause clause)
    : IMotelySeedFilterDesc<TagFilterDesc.TagFilter>
{
    private readonly TagClause _clause = clause;

    public TagFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
        {
            // NOTE(audit): the tag clause only reads the tag stream below — this
            // booster-pack-stream cache looks unused/vestigial here. Left intact pending review;
            // remove if nothing downstream actually consumes a cached pack stream for tags.
            ctx.CacheBoosterPackStream(ante);
            ctx.CacheTagStream(ante);
        }
        return new TagFilter(_clause);
    }

    public struct TagFilter(TagClause clause) : IMotelySeedFilter
    {
        private readonly TagClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            Debug.Assert(_clause.Tags.Length > 0);
            var clause = _clause;
            int maxDraw = (clause.Rolls.Length == 0 ? -1 : clause.Rolls.Max());
            Span<VectorEnum256<MotelyTag>> draws = stackalloc VectorEnum256<MotelyTag>[maxDraw + 1];

            Vector256<int> matchCounts = Vector256<int>.Zero;

            foreach (var ante in clause.Antes)
            {
                var tagStream = ctx.CreateTagStream(ante);
                for (int i = 0; i <= maxDraw; i++)
                    draws[i] = ctx.GetNextTag(ref tagStream);

                foreach (var drawIndex in clause.Rolls)
                {
                    var rolled = draws[drawIndex];
                    foreach (var t in clause.Tags)
                    {
                        var match = VectorEnum256.Equals(rolled, t);
                        matchCounts = Vector256.Add(
                            matchCounts,
                            Vector256.ConditionalSelect(
                                match,
                                Vector256.Create(1),
                                Vector256<int>.Zero
                            )
                        );
                    }
                }
            }

            return SimdPackSupport.MeetsMinMaxMask(matchCounts, clause.Min, clause.Max);
        }
    }
}
