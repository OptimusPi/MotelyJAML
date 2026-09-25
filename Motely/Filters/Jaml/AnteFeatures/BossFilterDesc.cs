using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("boss", "bosses", ValueEnum = typeof(MotelyBossBlind))]
[YamlObject]
public sealed partial class BossClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyBossBlind[] Bosses { get; set; } = [];
}

public readonly struct BossFilterDesc(BossClause clause)
    : IMotelySeedFilterDesc<BossFilterDesc.BossFilter>
{
    private readonly BossClause _clause = clause;

    public static string[] Discriminators => ["boss", "bosses"];

    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes"];

    private static bool IsFinisherAnte(int ante) => ante % 8 == 0;

    private static int EligibleNormal(int ante)
    {
        int count = 0;
        foreach (var boss in MotelyBossBlindExt.NormalBossBlinds)
            if (boss.GetBossMinAnte() <= ante)
                count++;
        return count;
    }

    private static int NormalAntesBefore(int ante) => (ante - 1) - (ante - 1) / 8;

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
            return IsFinisherAnte(ante) ? 1.0 / MotelyBossBlindExt.FinisherBossBlinds.Length : 0.0;
        }

        if (IsFinisherAnte(ante) || ante < boss.GetBossMinAnte())
            return 0.0;

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

            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}
