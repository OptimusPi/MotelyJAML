using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("tag", "tags",
    ValueEnum = typeof(MotelyTag), RollsDefault = new[] { 0, 1 })]
[JamlDiscriminator("smallBlindTag",
    ValueEnum = typeof(MotelyTag), RollsDefault = new[] { 0 })]
[JamlDiscriminator("bigBlindTag",
    ValueEnum = typeof(MotelyTag), RollsDefault = new[] { 1 })]
[YamlObject]
public sealed partial class TagClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyTag[] Tags { get; set; } = [];

    public int[] Rolls { get; set; } = [];
}

public struct TagFilterDesc(TagClause clause)
    : IMotelySeedFilterDesc<TagFilterDesc.TagFilter>
{
    private readonly TagClause _clause = clause;

    public static string[] Discriminators => ["tag", "tags", "smallBlindTag", "bigBlindTag"];

    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "rolls"];

    public TagFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
        {
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
            int maxDraw = MapFeatureRolls.MaxRollIndex(clause.Rolls);
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

            return JamlSimdPackSupport.MeetsMinMaxMask(matchCounts, clause.Min, clause.Max);
        }
    }
}
