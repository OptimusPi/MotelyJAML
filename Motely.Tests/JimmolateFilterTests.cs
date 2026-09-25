namespace Motely.Tests;

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

        Assert.Equal("ALEEB", Assert.Single(matched));
        Assert.Equal(1L, matching);
    }


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

        Assert.Equal(Seeds.Length, seen.Count);
        Assert.Equal((long)Seeds.Length, matching);
        Assert.Equal(Seeds.Length, matched.Count);
    }
}
