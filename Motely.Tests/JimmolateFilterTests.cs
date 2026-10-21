namespace Motely.Tests;

// Jimmolate is a per-seed PREDICATE, chained on like a normal filter (the OG Immolate
// `filter(inst) => keep?` mental model, in C#). These tests prove the predicate's bool
// actually drives filtering and that it receives a live, drivable MotelySingleSearchContext
// — the same single-seed instance scoring/the analyzer/JAMLyzer run on, not a seed string.
// The instance is the unit of introspection; via Bootsharp the predicate can be authored
// host-side (incl. JS) and loaded into the compiled Motely core, where it runs against that
// live context — exactly as Immolate compiles a .cl filter into the kernel. The predicate
// runs IN the engine; JS does not receive a marshalled result.
public sealed class JimmolateFilterTests
{
    private static readonly string[] Seeds = ["12345678", "UNITTEST", "1AAAAAAA", "ALEEBOOO"];

    private static (long Matching, List<string> Matched) RunWithJimmolate(
        MotelyIndividualSeedSearcher predicate
    ) => RunWithJimmolate(Seeds, predicate);

    private static (long Matching, List<string> Matched) RunWithJimmolate(
        string[] seeds,
        MotelyIndividualSeedSearcher predicate
    )
    {
        var matched = new List<string>();
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(predicate)
            .WithSeedMatchCallback(matched.Add);

        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, matched);
    }

    [Fact]
    public void Jimmolate_AcceptAll_KeepsEverySeed()
    {
        var (matching, matched) = RunWithJimmolate(
            static (MotelySingleSearchContext _) => 1
        );

        Assert.Equal((long)Seeds.Length, matching);
        Assert.Equal(Seeds.Length, matched.Count);
    }

    [Fact]
    public void Jimmolate_RejectAll_KeepsNothing()
    {
        var (matching, matched) = RunWithJimmolate(
            static (MotelySingleSearchContext _) => 0
        );

        Assert.Equal(0L, matching);
        Assert.Empty(matched);
    }

    [Fact]
    public void Jimmolate_PredicateBoolDrivesFiltering_KeepsOnlyTargetSeed()
    {
        const string target = "UNITTEST";

        var (matching, matched) = RunWithJimmolate(
            static (MotelySingleSearchContext ctx) => ctx.GetSeed() == target ? 1 : 0
        );

        Assert.Equal(1L, matching);
        Assert.Equal(target, Assert.Single(matched));
    }

    // The real Immolate model: a finder that actually FINDS a seed by its derivation. ALEEB is
    // hidden in a list of decoys; the finder identifies it by its VERIFIED ante-1 fingerprint —
    // Magic Trick voucher AND The Window boss (see seeds/ALEEB.verified.txt). Two independent
    // derived facts, read live off the context, so no decoy passes by coincidence. It finds ALEEB.
    [Fact]
    public void Jimmolate_FindsAleebAmongDecoys_ByVerifiedAnteOneFingerprint()
    {
        string[] seeds = ["PIROCKS", "ALEEB", "LOVEYAHB"];

        var (matching, matched) = RunWithJimmolate(
            seeds,
            (MotelySingleSearchContext ctx) =>
            {
                if (ctx.GetAnteFirstVoucher(1) != MotelyVoucher.MagicTrick)
                    return 0;

                var bossStream = ctx.CreateBossStream();
                var runState = new MotelyRunState();
                return ctx.GetBossForAnte(ref bossStream, 1, runState)
                    == MotelyBossBlind.TheWindow
                        ? 1
                        : 0;
            }
        );

        Assert.Equal("ALEEB", Assert.Single(matched)); // pulled the needle out of the decoys
        Assert.Equal(1L, matching);
    }


    // The OG Immolate mental model: the predicate gets a live, drivable per-seed context,
    // not just a seed string. Every seed has an ante-1 boss, so reading it must succeed for
    // each seed and keep all of them.
    [Fact]
    public void Jimmolate_ReceivesLiveSearchContext_CanDriveStreams()
    {
        var seen = new List<string>();

        var (matching, matched) = RunWithJimmolate(
            (MotelySingleSearchContext ctx) =>
            {
                seen.Add(ctx.GetSeed());
                var bossStream = ctx.CreateBossStream();
                var runState = new MotelyRunState();
                var boss = ctx.GetBossForAnte(ref bossStream, 1, runState);
                return boss != default ? 1 : 0;
            }
        );

        Assert.Equal(Seeds.Length, seen.Count); // predicate invoked once per seed
        Assert.Equal((long)Seeds.Length, matching); // every seed has a valid ante-1 boss
        Assert.Equal(Seeds.Length, matched.Count);
    }
}
