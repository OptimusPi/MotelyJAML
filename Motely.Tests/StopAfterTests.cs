using Xunit;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// <c>StopAfter(n)</c> ends a search once at least n seeds have matched. Proven against a
/// deliberately permissive filter over a fixed batch slice: the same slice unbounded matches
/// thousands of seeds, so a run that returns a handful can only be StopAfter doing its job —
/// a filter with one natural match would pass whether or not the feature worked at all.
/// The contract is "at least n", never exactly n: a batch scores all 8 SIMD lanes before anyone
/// polls cancellation, so the run delivers the lane that tripped the limit and its neighbours.
/// </summary>
public class StopAfterTests(ITestOutputHelper output)
{
    // Matches nearly every seed — any joker, anywhere in ante 1.
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

        // At least one seed actually reached the caller — stopping must not swallow the find.
        Assert.True(stoppedDelivered >= 1, "StopAfter(1) delivered no seed at all");
        Assert.Equal(stopped, stoppedDelivered);

        // And it stopped somewhere near the limit rather than running the slice out. One batch of
        // 8 lanes per thread is the overshoot the contract allows for.
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

        // Hitting the limit is the search succeeding. A caller awaiting completion must not have
        // to tell it apart from a user-cancelled run.
        Assert.True(search.IsCompleted);
        Assert.True(search.MatchingSeeds >= 1);
    }

    // A run that stops inside a batch must not report the whole batch. The failure scales with
    // batchCharCount, so 6 is included: one batch there is 35^6 seeds.
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
