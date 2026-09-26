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

    // MotelyRunState became a plain class (record) instead of a ref struct this session — no
    // more `ref` needed at call sites, because mutating a shared object's fields through a method
    // call (SeeBoss) is visible to the caller automatically. This pins that claim with real
    // behavior: GetBossForAnte marks the boss it picks as seen (HasSeenBoss), so calling it twice
    // on the SAME runState instance, with no `ref`, must never repeat a boss until the pool is
    // exhausted. If mutation stopped propagating, this would eventually pick the same boss twice.
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
