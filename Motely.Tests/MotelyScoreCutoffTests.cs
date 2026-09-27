namespace Motely.Tests;

public sealed class MotelyScoreCutoffTests
{
    [Fact]
    public void Auto_FirstSeedAlwaysPasses_ThenDropsBelowRunningMax()
    {
        var cutoff = MotelyScoreCutoff.Auto();

        Assert.True(cutoff.ShouldEmit(3));
        Assert.Equal(3, cutoff.CurrentHigh);
        Assert.True(cutoff.ShouldEmit(3));
        Assert.False(cutoff.ShouldEmit(2));
        Assert.True(cutoff.ShouldEmit(8));
        Assert.Equal(8, cutoff.CurrentHigh);
        Assert.False(cutoff.ShouldEmit(7));
        Assert.True(cutoff.ShouldEmit(8));
    }

    [Fact]
    public void Fixed_EmitsAtOrAboveFloor()
    {
        var cutoff = MotelyScoreCutoff.Fixed(4);

        Assert.False(cutoff.ShouldEmit(3));
        Assert.True(cutoff.ShouldEmit(4));
        Assert.True(cutoff.ShouldEmit(99));
        Assert.False(cutoff.ShouldEmit(0));
    }

    [Fact]
    public void Off_EmitsEverything()
    {
        var cutoff = MotelyScoreCutoff.Off();

        Assert.True(cutoff.ShouldEmit(int.MinValue));
        Assert.True(cutoff.ShouldEmit(0));
        Assert.True(cutoff.ShouldEmit(int.MaxValue));
    }

    [Fact]
    public void TryParse_EmptyAndAuto_AreRunningMax()
    {
        Assert.True(MotelyScoreCutoff.TryParse("", out var empty, out var emptyErr));
        Assert.Null(emptyErr);
        Assert.True(empty.IsAuto);

        Assert.True(MotelyScoreCutoff.TryParse("auto", out var auto, out var autoErr));
        Assert.Null(autoErr);
        Assert.True(auto.IsAuto);
    }
}
