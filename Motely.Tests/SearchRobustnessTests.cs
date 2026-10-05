using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// Settings a caller can reach (CLI flags, JS <c>SearchSettings</c>) either work or fail fast with
/// an ArgumentException. Before these guards: a negative start batch or a batch character count of
/// 0 crashed a worker (out-of-range digit, native buffer overrun), 8+ searched nothing and reported
/// "completed", a worker that threw left the others sweeping the whole space before the error
/// surfaced, and a Dispose from inside a result callback freed buffers a batch was still reading.
/// </summary>
public class SearchRobustnessTests(ITestOutputHelper output)
{
    private static MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter> Pass() =>
        new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(new PassthroughFilterDesc())
            .WithQuietMode(true);

    /// <summary>Awaits <paramref name="task"/> but fails the test, instead of hanging it, past <paramref name="ms"/>.</summary>
    private static async Task<Task> Within(Task task, int ms = 30_000)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        Assert.True(done == task, $"search did not finish within {ms} ms");
        return task;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(9)]
    public void BatchCharacterCountOutsideOneToSevenThrows(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Pass().WithBatchCharacterCount(count));

    [Fact]
    public void NegativeBatchIndexesThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pass().WithStartBatchIndex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pass().WithEndBatchIndex(-1));
    }

    [Fact]
    public void StartPastTheSpaceOrEndBeforeStartThrowsAtCreate()
    {
        // Batch character count 3 splits the space into 35^5 batches.
        long totalBatches = 35L * 35 * 35 * 35 * 35;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pass().WithSequentialSearch().WithBatchCharacterCount(3).WithStartBatchIndex(totalBatches + 1).CreateSearch()
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Pass().WithSequentialSearch().WithBatchCharacterCount(3).WithStartBatchIndex(10).WithEndBatchIndex(5).CreateSearch()
        );
    }

    [Fact]
    public void NegativeStopAfterAndRandomCountThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Pass().StopAfter(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pass().WithRandomSearch(-5));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task AThrowingCallbackFaultsTheSearchInsteadOfHangingIt(int threads)
    {
        int matches = 0;
        using var search = Pass()
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithThreadCount(threads)
            .WithSeedMatchCallback(_ =>
            {
                if (Interlocked.Increment(ref matches) == 5)
                    throw new InvalidOperationException("boom");
            })
            .CreateSearch();

        var run = await Within(search.RunSearchAsync());
        var ex = Assert.IsType<InvalidOperationException>(run.Exception?.InnerException);
        Assert.Equal("boom", ex.Message);
        output.WriteLine($"faulted after {search.TotalSeedsSearched} seeds");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task AThrowingJimmolateFaultsTheSearchInsteadOfHangingIt(int threads)
    {
        int calls = 0;
        using var search = Pass()
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithThreadCount(threads)
            .WithJimmolate(_ =>
            {
                if (Interlocked.Increment(ref calls) == 50)
                    throw new InvalidOperationException("boom");
                return 1;
            })
            .CreateSearch();

        var run = await Within(search.RunSearchAsync());
        Assert.IsType<InvalidOperationException>(run.Exception?.InnerException);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task DisposeFromInsideACallbackDoesNotCorruptTheHeap(int threads)
    {
        IMotelySearch? search = null;
        int matches = 0;
        search = Pass()
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithThreadCount(threads)
            .WithJimmolate(_ => 1)
            .WithSeedMatchCallback(_ =>
            {
                if (Interlocked.Increment(ref matches) == 3)
                    search!.Dispose();
            })
            .CreateSearch();

        await Within(search.RunSearchAsync());

        // The old double free surfaced as a crash on later allocations, not at the Dispose itself.
        for (int i = 0; i < 200; i++)
        {
            using var churn = Pass().WithRandomSearch(16).WithJimmolate(_ => 1).CreateSearch();
            await churn.RunSearchAsync();
        }
    }

    [Fact]
    public async Task ProgressCallbacksNeverOverlap()
    {
        int inside = 0,
            overlaps = 0,
            calls = 0;
        using var search = Pass()
            .WithSequentialSearch()
            .WithBatchCharacterCount(2)
            .WithThreadCount(4)
            .WithEndBatchIndex(35 * 35)
            .WithProgressReportIntervalMs(0)
            .WithProgressCallback(_ =>
            {
                Interlocked.Increment(ref calls);
                if (Interlocked.Increment(ref inside) > 1)
                    Interlocked.Increment(ref overlaps);
                Thread.SpinWait(2000);
                Interlocked.Decrement(ref inside);
            })
            .CreateSearch();

        await Within(search.RunSearchAsync(), 120_000);
        output.WriteLine($"{calls} progress calls");
        Assert.True(calls > 0);
        Assert.Equal(0, overlaps);
    }

    [Fact]
    public void RandomProviderNearIntMaxValueEnds()
    {
        // The counter was an int: at seedCount = int.MaxValue it wrapped negative on the next seed
        // and never read as finished again, so the search ran forever.
        var provider = new MotelyRandomSeedProvider(int.MaxValue);
        typeof(MotelyRandomSeedProvider)
            .GetField("_seedsGenerated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(provider, (long)int.MaxValue - 1);
        var buffer = new string[4];
        Assert.Equal(1, provider.NextSeeds(buffer));
        Assert.Equal(0, provider.NextSeeds(buffer));
        Assert.Equal(0, provider.NextSeeds(buffer));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOOLONGKEYWORD")]
    [InlineData("c@tz")]
    [InlineData("CAT0")]
    public void InvalidKeywordsThrow(string keyword) =>
        Assert.Throws<ArgumentException>(() => Pass().WithKeywordSearch([keyword], ['1']));

    [Fact]
    public void EmptyKeywordListAndBadPaddingThrow()
    {
        Assert.Throws<ArgumentException>(() => Pass().WithKeywordSearch([], ['1']));
        Assert.Throws<ArgumentException>(() => Pass().WithKeywordSearch(["CATS"], ['a', '!']));
    }

    [Fact]
    public async Task LowercaseKeywordFindsUppercaseSeeds()
    {
        var found = new ConcurrentBag<string>();
        using var search = Pass()
            .WithKeywordSearch(["cats"], ['1'])
            .WithJimmolate(_ => 1)
            .WithSeedMatchCallback(found.Add)
            .CreateSearch();

        await Within(search.RunSearchAsync());
        Assert.NotEmpty(found);
        Assert.All(found, seed => Assert.Contains("CATS", seed));
    }

    [Fact]
    public async Task LowercaseSeedInAListIsSearchedAsTheUppercaseSeed()
    {
        var found = new ConcurrentBag<string>();
        using var search = Pass()
            .WithSeedList(["abcdefgh"])
            .WithJimmolate(_ => 1)
            .WithSeedMatchCallback(found.Add)
            .CreateSearch();

        await Within(search.RunSearchAsync());
        Assert.Equal(["ABCDEFGH"], found.ToArray());
    }
}
