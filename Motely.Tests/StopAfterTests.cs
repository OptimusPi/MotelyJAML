using Xunit;
using Xunit.Abstractions;

namespace Motely.Tests;

public class StopAfterTests(ITestOutputHelper output)
{
    private const string PermissiveJaml = """
        name: permissive
        deck: Red
        stake: White
        must:
          - joker: []
            antes: [1]
        """;

    private static (long Matching, int Delivered) RunSlice(long? stopAfter)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(PermissiveJaml, out var config, out var error),
            $"JAML parse failed: {error}"
        );

        int delivered = 0;
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(0)
            .WithEndBatchIndex(1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(_ => Interlocked.Increment(ref delivered));

        if (stopAfter.HasValue)
            settings = settings.StopAfter(stopAfter.Value);

        using var search = settings.Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, delivered);
    }

    [Fact]
    public void StopAfterEndsASearchThatWouldOtherwiseMatchThousands()
    {
        var (unbounded, unboundedDelivered) = RunSlice(stopAfter: null);
        output.WriteLine($"unbounded: {unbounded} matched, {unboundedDelivered} delivered");

        Assert.True(
            unbounded > 1000,
            $"control slice should match thousands so early-stop is visible; matched {unbounded}"
        );
        Assert.Equal(unbounded, unboundedDelivered);

        var (stopped, stoppedDelivered) = RunSlice(stopAfter: 1);
        output.WriteLine($"StopAfter(1): {stopped} matched, {stoppedDelivered} delivered");

        Assert.True(stoppedDelivered >= 1, "StopAfter(1) delivered no seed at all");
        Assert.Equal(stopped, stoppedDelivered);

        Assert.True(
            stopped < unbounded / 10,
            $"StopAfter(1) matched {stopped}, barely under the unbounded {unbounded} — it did not stop early"
        );
    }

    [Fact]
    public void StopAfterReportsTheSearchAsCompletedNotAborted()
    {
        Assert.True(JamlConfigLoader.TryLoad(PermissiveJaml, out var config, out var error), error);

        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(0)
            .WithEndBatchIndex(1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .StopAfter(1);

        using var search = settings.Start();
        search.AwaitCompletion();

        Assert.True(search.IsCompleted);
        Assert.True(search.MatchingSeeds >= 1);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    public void StopAfterDoesNotBookTheBatchItAbandoned(int batchCharCount)
    {
        Assert.True(JamlConfigLoader.TryLoad(PermissiveJaml, out var config, out var error), error);

        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSequentialSearch()
            .WithBatchCharacterCount(batchCharCount)
            .WithStartBatchIndex(0)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .StopAfter(1);

        using var search = settings.Start();
        search.AwaitCompletion();

        Assert.True(search.StoppedOnMatchLimit, "search did not stop on the match limit");
        Assert.True(search.MatchingSeeds >= 1);

        long seedsPerBatch = (long)Math.Pow(35, batchCharCount);
        Assert.True(
            search.TotalSeedsSearched < seedsPerBatch,
            $"reported {search.TotalSeedsSearched:N0} seeds searched, which is the abandoned "
                + $"batch ({seedsPerBatch:N0}) being billed in full"
        );
    }

    [Fact]
    public void StopAfterZeroSearchesTheWholeSlice()
    {
        var (unbounded, _) = RunSlice(stopAfter: null);
        var (explicitZero, _) = RunSlice(stopAfter: 0);
        Assert.Equal(unbounded, explicitZero);
    }
}
