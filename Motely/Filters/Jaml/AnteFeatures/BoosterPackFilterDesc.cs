using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely.Filters.Jaml;

[JamlDiscriminator(
    "boosterPack",
    "boosterPacks",
    ValueEnum = typeof(MotelyBoosterPack),
    RollsDefault = new[] { 0, 1 }
)]
[YamlObject]
public sealed partial class BoosterPackClause : IJamlClause, IAnteScopedClause, IRollScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];

    public MotelyBoosterPack[] Packs { get; set; } = [];

    public int[] Rolls { get; set; } = [0, 1];
}

public struct BoosterPackFilterDesc(BoosterPackClause clause)
    : IMotelySeedFilterDesc<BoosterPackFilterDesc.BoosterPackFilter>
{
    private readonly BoosterPackClause _clause = clause;

    public static string[] Discriminators => ["boosterPack", "boosterPacks"];

    public static string[] ClauseKeys =>
        ["min", "max", "score", "label", "ante", "antes", "rolls"];

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
