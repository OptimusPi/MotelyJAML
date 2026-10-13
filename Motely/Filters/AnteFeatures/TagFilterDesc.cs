using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters;

[YamlDotNet.Serialization.YamlSerializable]
public partial class TagClause: IMotelyClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>The tags that count; empty (<c>any</c>) counts every offer.</summary>
    public MotelyTag[] Tags { get; set; } = [];

    /// <summary>
    /// Tag-stream draw indices per ante: 0 = small-blind offer, 1 = big-blind offer,
    /// 2+ = further draws on the same ante stream (replay / double-tag extras).
    /// </summary>
    public int[] Rolls { get; set; } = [0, 1];

    public IMotelySeedFilterDesc CreateFilterDesc() => new TagFilterDesc(this);
}

/// <summary>The small blind's tag offer only.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed class SmallBlindTagClause: TagClause
{
    public SmallBlindTagClause() => Rolls = [0];
}

/// <summary>The big blind's tag offer only.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed class BigBlindTagClause: TagClause
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
            ctx.CacheTagStream(ante);
        return new TagFilter(_clause);
    }

    public struct TagFilter(TagClause clause) : IMotelySeedFilter
    {
        private readonly TagClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;
            int maxDraw = clause.Rolls.Max();
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
                    Vector256<int> match = clause.Tags.Length == 0 ? Vector256<int>.AllBitsSet : Vector256<int>.Zero;
                    foreach (var tag in clause.Tags)
                        match |= VectorEnum256.Equals(rolled, tag);
                    matchCounts = Vector256.Add(
                        matchCounts,
                        Vector256.ConditionalSelect(match, Vector256<int>.One, Vector256<int>.Zero)
                    );
                }
            }

            return SimdPackSupport.MeetsMinMaxMask(matchCounts, clause.Min, clause.Max);
        }
    }
}
