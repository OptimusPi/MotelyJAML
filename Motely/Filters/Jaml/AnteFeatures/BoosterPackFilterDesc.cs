using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

/// <summary>
/// Shop booster pack <em>offer</em> filter: which pack kind+size sits in which pack slot.
/// Does not open pack contents — that is tarot/joker/planet/standard sources.
/// Value enum is <see cref="MotelyBoosterPack"/> (already in Motely.Enums; no new enum).
/// </summary>
[JamlDiscriminator(
    "boosterPack",
    "boosterPacks",
    ValueEnum = typeof(MotelyBoosterPack),
    RollsDefault = new[] { 0, 1 }
)]
public sealed class BoosterPackClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];

    /// <summary>
    /// Pack identities (type + size). Empty = any pack in the targeted slots (category match).
    /// </summary>
    public MotelyBoosterPack[] Packs { get; set; } = [];

    /// <summary>
    /// Shop pack offer indices per ante (0 = first offer, 1 = second, …).
    /// Same index space as other filters' <c>boosterPacks:</c> source lists.
    /// </summary>
    public int[] Rolls { get; set; } = [0, 1];
}

public struct BoosterPackFilterDesc(BoosterPackClause clause)
    : IMotelySeedFilterDesc<BoosterPackFilterDesc.BoosterPackFilter>,
      IJamlClauseDesc<BoosterPackClause>
{
    private readonly BoosterPackClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["boosterPack", "boosterPacks"];

    /// <inheritdoc/>
    public static string[] ClauseKeys =>
        ["min", "max", "score", "label", "ante", "antes", "rolls"];

    /// <inheritdoc/>
    public static bool Set(BoosterPackClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(BoosterPackClause clause, IJamlValueReader value)
    {
        // Empty disc = any pack kind/size in the listed slots.
        if (string.IsNullOrWhiteSpace(value.Text))
            return true;
        if (!value.TryEnumArray<MotelyBoosterPack>(out var packs))
            return false;
        clause.Packs = packs;
        return true;
    }

    /// <summary>
    /// Each targeted slot is one weighted pack roll, so a slot matches with the summed weight of
    /// the packs named — or certainly, for a category-any clause. Ante 1 offers four slots, later
    /// antes six, and ante 1's first slot is not a roll at all: the stream hands out a plain
    /// Buffoon pack before touching the PRNG, so it matches iff Buffoon is named.
    /// </summary>
    public static double EstimateRarity(BoosterPackClause clause, in JamlRarityContext ctx)
    {
        bool any = JamlDisc.IsCategoryAny(clause.Packs);
        HashSet<MotelyBoosterPack> wanted = [.. JamlDisc.OrEmpty(clause.Packs)];

        double weighted = 1.0;
        if (!any)
        {
            weighted = 0.0;
            foreach (var pack in wanted)
                weighted += JamlPoolRarity.PackShare(pack);
        }
        double fixedBuffoon = any || wanted.Contains(MotelyBoosterPack.Buffoon) ? 1.0 : 0.0;

        double[] pmf = JamlCountDistribution.Zero;
        foreach (int ante in clause.Antes)
        {
            HashSet<int> slots = [];
            foreach (int slot in clause.Rolls)
            {
                if (!slots.Add(slot) || !JamlPoolRarity.SlotIsReachable(ante, slot))
                    continue;
                pmf = JamlCountDistribution.Convolve(
                    pmf,
                    JamlCountDistribution.Bernoulli(
                        JamlPoolRarity.SlotIsFixedBuffoon(ante, slot) ? fixedBuffoon : weighted
                    )
                );
            }
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    public BoosterPackFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        foreach (var ante in _clause.Antes)
            ctx.CacheBoosterPackStream(ante);
        return new BoosterPackFilter(_clause);
    }

    public struct BoosterPackFilter(BoosterPackClause clause) : IMotelySeedFilter
    {
        private readonly BoosterPackClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            var clause = _clause;
            int maxSlot = MapFeatureRolls.MaxRollIndex(clause.Rolls);
            Debug.Assert(maxSlot >= 0, "BoosterPack rolls empty after load — loader bug.");

            Vector256<int> matchCounts = Vector256<int>.Zero;

            VectorMask ante1Extended = VectorMask.NoBitsSet;
            if (JamlSimdPackSupport.NeedsAnte1Extension(maxSlot))
            {
                bool hasAnte1 = false;
                for (int i = 0; i < clause.Antes.Length; i++)
                {
                    if (clause.Antes[i] == 1)
                    {
                        hasAnte1 = true;
                        break;
                    }
                }
                if (hasAnte1)
                    ante1Extended = JamlSimdPackSupport.Ante1PackExtensionMask(ref ctx);
            }

            foreach (var ante in clause.Antes)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                for (int p = 0; p <= maxSlot; p++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);

                    bool isTarget = false;
                    for (int i = 0; i < clause.Rolls.Length; i++)
                    {
                        if (clause.Rolls[i] == p)
                        {
                            isTarget = true;
                            break;
                        }
                    }
                    if (!isTarget)
                        continue;

                    VectorMask reachable = JamlSimdPackSupport.SlotReachableMask(
                        ante,
                        p,
                        ante1Extended
                    );
                    if (reachable.IsAllFalse())
                        continue;

                    VectorMask hit;
                    if (JamlDisc.IsCategoryAny(clause.Packs))
                    {
                        hit = reachable;
                    }
                    else
                    {
                        hit = VectorMask.NoBitsSet;
                        for (int t = 0; t < clause.Packs.Length; t++)
                            hit |= VectorEnum256.Equals(pack, clause.Packs[t]);
                        hit &= reachable;
                    }

                    JamlSimdPackSupport.AddMatchCounts(hit, ref matchCounts);
                }
            }

            return JamlSimdPackSupport.MeetsMinMaxMask(matchCounts, clause.Min, clause.Max);
        }
    }
}
