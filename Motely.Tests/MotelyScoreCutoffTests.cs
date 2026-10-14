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

    [Fact]
    public void TryResolve_YAMLCutoff_ReplacesAuto()
    {
        Assert.True(MotelyScoreCutoff.TryResolve(null, 12, out var cutoff, out var text, out var err));
        Assert.Null(err);
        Assert.False(cutoff.IsAuto);
        Assert.Equal(12, cutoff.EngineCutoff);
        Assert.Equal("12 (yaml)", text);
    }

    [Fact]
    public void TryResolve_HostText_WinsOverYAMLCutoff()
    {
        Assert.True(MotelyScoreCutoff.TryResolve("off", 12, out var off, out var offText, out _));
        Assert.Equal(int.MinValue, off.EngineCutoff);
        Assert.True(off.ShouldEmit(0));
        Assert.Equal("off", offText);

        Assert.True(MotelyScoreCutoff.TryResolve("auto", 12, out var auto, out _, out _));
        Assert.True(auto.IsAuto);

        Assert.False(MotelyScoreCutoff.TryResolve("nope", 12, out _, out _, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void TryResolve_NothingGiven_IsAuto()
    {
        Assert.True(MotelyScoreCutoff.TryResolve(null, null, out var cutoff, out var text, out _));
        Assert.True(cutoff.IsAuto);
        Assert.Equal("auto", text);
    }

    [Fact]
    public void YAMLCutoff_DropsSeedsBelowItInTheEngine()
    {
        const string yaml = """
            should:
              - joker: any
                score: 1
            """;
        var config = Motely.Filters.YamlConfigLoader.FromYaml(yaml);
        long Matches(int engineCutoff)
        {
            using var search = Motely.Filters.MotelySearchBuilder
                .CreateSettings(config, engineCutoff)
                .WithSeedGenerator(["MOTELY77"], 1)
                .WithThreadCount(1)
                .WithQuietMode(true)
                .WithAutoScoreCutoff(false)
                .Start();
            search.AwaitCompletion();
            return search.MatchingSeeds;
        }

        Assert.Equal(1, Matches(int.MinValue));
        Assert.Equal(0, Matches(int.MaxValue));
    }
}
