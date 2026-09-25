using Motely.Analysis;
using Motely.Enums;
using Xunit;

namespace Motely.Tests;

public sealed class SeedRouterTests
{
    [Fact]
    public void TestSeedRouter_CapturesSingleSearchContext()
    {
        using var router = new MotelySeedRouterDesc("1AAAAAAA", MotelyDeck.Red, MotelyStake.White);

        var ctx = router.Instance();

        Assert.Equal("1AAAAAAA", ctx.GetSeed());
        var bossStream = ctx.CreateBossStream();
        var runState = new MotelyRunState();
        var boss = ctx.GetBossForAnte(ref bossStream, 1, runState);
        Assert.NotEqual(default, boss);
    }

    [Fact]
    public void GetBossForAnte_MutationPersists_AcrossCallsOnSameRunStateInstance_WithoutRef()
    {
        using var router = new MotelySeedRouterDesc("1AAAAAAA", MotelyDeck.Red, MotelyStake.White);
        var ctx = router.Instance();
        var bossStream = ctx.CreateBossStream();
        var runState = new MotelyRunState();

        var first = ctx.GetBossForAnte(ref bossStream, 1, runState);
        Assert.True(runState.HasSeenBoss(first), "SeeBoss's mutation did not persist on the shared runState instance.");

        var second = ctx.GetBossForAnte(ref bossStream, 1, runState);
        Assert.NotEqual(first, second);
        Assert.True(runState.HasSeenBoss(second));
    }
}
