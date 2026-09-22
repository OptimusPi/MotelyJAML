using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

// Three attributes: tag/tags default both blind offers; the blind-specific wires pin one roll.
[JamlDiscriminator("tag", "tags",
    ValueEnum = typeof(MotelyTag), RollsDefault = new[] { 0, 1 })]
[JamlDiscriminator("smallBlindTag",
    ValueEnum = typeof(MotelyTag), RollsDefault = new[] { 0 })]
[JamlDiscriminator("bigBlindTag",
    ValueEnum = typeof(MotelyTag), RollsDefault = new[] { 1 })]
public sealed class TagClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyTag[] Tags { get; set; } = [];

    /// <summary>
    /// Tag-stream draw indices per ante: 0 = small-blind offer, 1 = big-blind offer,
    /// 2+ = further draws on the same ante stream (replay / double-tag extras).
    /// </summary>
    public int[] Rolls { get; set; } = [];
}

public struct TagFilterDesc(TagClause clause)
    : IMotelySeedFilterDesc<TagFilterDesc.TagFilter>,
      IJamlClauseDesc<TagClause>
{
    private readonly TagClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["tag", "tags", "smallBlindTag", "bigBlindTag"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "rolls"];

    /// <summary>Tag clauses carry no keys beyond the common set.</summary>
    public static bool Set(TagClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(TagClause clause, IJamlValueReader value)
    {
        if (!value.TryEnumArray<MotelyTag>(out var tags))
            return false;
        clause.Tags = tags;
        return true;
    }

    /// <summary>
    /// Each roll is one uniform draw of the tag pool — except in ante 1, where
    /// <c>GetNextTag</c> re-rolls nine tags away and the pool is the fifteen that remain, so a
    /// disallowed tag is impossible there rather than merely rare. Rolls on one ante's stream are
    /// distinct draws; antes are independent streams; the clause's window is read off the total.
    /// </summary>
    public static double EstimateRarity(TagClause clause, in JamlRarityContext ctx)
    {
        int pool = MotelyEnum<MotelyTag>.ValueCount;
        int trials = JamlPoolRarity.Distinct(clause.Rolls);
        HashSet<MotelyTag> wanted = [.. clause.Tags];

        double[] pmf = JamlCountDistribution.Zero;
        foreach (int ante in clause.Antes)
        {
            int hits = wanted.Count;
            int poolHere = pool;
            if (ante == 1)
            {
                var disallowed = MotelySingleSearchContext.DisallowedAnteOneTags;
                poolHere -= disallowed.Length;
                foreach (var tag in disallowed)
                    if (wanted.Contains(tag))
                        hits--;
            }

            pmf = JamlCountDistribution.Convolve(
                pmf,
                JamlCountDistribution.Binomial(trials, hits / (double)poolHere)
            );
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

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
