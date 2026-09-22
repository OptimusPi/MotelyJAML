using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("boss", "bosses", ValueEnum = typeof(MotelyBossBlind))]
public sealed class BossClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [];
    public MotelyBossBlind[] Bosses { get; set; } = [];
    // No Rolls — for now. Boss re-rolls ARE a real source, but the re-roll read isn't
    // implemented in MotelySearchContext.Boss.cs yet (state-heavy, same blocker as joker
    // re-rolls). Antes select the WHERE; re-add Rolls here when that source lands.
}

public readonly struct BossFilterDesc(BossClause clause)
    : IMotelySeedFilterDesc<BossFilterDesc.BossFilter>,
      IJamlClauseDesc<BossClause>
{
    private readonly BossClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["boss", "bosses"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes"];

    /// <summary>Boss clauses carry no keys beyond the common set, so nothing is claimed here.</summary>
    public static bool Set(BossClause clause, string key, IJamlValueReader value) => false;

    /// <inheritdoc/>
    public static bool SetDiscriminatorValue(BossClause clause, IJamlValueReader value)
    {
        if (!value.TryEnumArray<MotelyBossBlind>(out var bosses))
            return false;
        clause.Bosses = bosses;
        return true;
    }

    /// <summary>
    /// One boss per ante, uniform over the pool still in play — <c>GetBossForAnte</c>: at antes
    /// divisible by eight the five finishers, otherwise the normal bosses whose minimum ante has
    /// arrived, less every boss already seen, and the pool refills only when it runs dry. So a
    /// boss's chance at ante <c>A</c> is one over that ante's pool, times the chance it was not
    /// drawn at an earlier ante where it was eligible. A finisher comes out to exactly 1/5.
    /// </summary>
    public static double EstimateRarity(BossClause clause, in JamlRarityContext ctx)
    {
        HashSet<MotelyBossBlind> wanted = [.. clause.Bosses];

        double[] pmf = JamlCountDistribution.Zero;
        foreach (int ante in clause.Antes)
        {
            double share = 0.0;
            foreach (var boss in wanted)
                share += ShareAt(boss, ante);
            pmf = JamlCountDistribution.Convolve(pmf, JamlCountDistribution.Bernoulli(share));
        }

        return JamlCountDistribution.Window(pmf, clause.Min, clause.Max);
    }

    private static bool IsFinisherAnte(int ante) => ante % 8 == 0;

    /// <summary>How many normal bosses may appear at <paramref name="ante"/> at all.</summary>
    private static int EligibleNormal(int ante)
    {
        int count = 0;
        foreach (var boss in MotelyBossBlindExt.NormalBossBlinds)
            if (boss.GetBossMinAnte() <= ante)
                count++;
        return count;
    }

    /// <summary>How many normal-boss antes precede <paramref name="ante"/>.</summary>
    private static int NormalAntesBefore(int ante) => (ante - 1) - (ante - 1) / 8;

    /// <summary>
    /// The normal pool at <paramref name="ante"/>: the eligible bosses less the ones seen since
    /// the last refill. Early antes cannot run dry — eligibility grows faster than the seen
    /// count — so the refill cycle is the full normal roster, which is the pool from ante 6 on.
    /// </summary>
    private static int NormalPool(int ante)
    {
        int roster = MotelyBossBlindExt.NormalBossBlinds.Length;
        int seen = NormalAntesBefore(ante) % roster;
        return Math.Max(1, EligibleNormal(ante) - seen);
    }

    private static double ShareAt(MotelyBossBlind boss, int ante)
    {
        if (ante < 1)
            return 0.0;

        if (boss.GetBossType() == MotelyBossBlindType.Finisher)
        {
            // A uniform permutation of five: whichever finisher ante it is, each is 1/5.
            return IsFinisherAnte(ante) ? 1.0 / MotelyBossBlindExt.FinisherBossBlinds.Length : 0.0;
        }

        if (IsFinisherAnte(ante) || ante < boss.GetBossMinAnte())
            return 0.0;

        // Not drawn at any earlier eligible normal ante in the current refill cycle …
        int roster = MotelyBossBlindExt.NormalBossBlinds.Length;
        int cycleStartIndex = NormalAntesBefore(ante) / roster * roster;
        double notSeen = 1.0;
        for (int earlier = 1; earlier < ante; earlier++)
        {
            if (IsFinisherAnte(earlier) || earlier < boss.GetBossMinAnte())
                continue;
            if (NormalAntesBefore(earlier) < cycleStartIndex)
                continue;
            notSeen *= 1.0 - 1.0 / NormalPool(earlier);
        }

        // … then drawn from this ante's pool.
        return notSeen / NormalPool(ante);
    }

    public BossFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
        new BossFilter(_clause);

    public struct BossFilter(BossClause clause) : IMotelySeedFilter
    {
        private readonly BossClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            Debug.Assert(_clause.Bosses.Length > 0);

            // Single match core: same PrepareRunState + CountBossOccurrences as should-scoring.
            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}
