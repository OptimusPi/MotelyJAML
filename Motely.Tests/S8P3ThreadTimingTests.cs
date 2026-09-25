using Motely.Filters.Jaml;

namespace Motely.Tests;

public sealed class S8P3ThreadTimingTests
{
    private const string PermissiveJaml = """
        name: s8p3-permissive
        deck: Red
        stake: White
        must:
          - joker: []
            antes: [1]
        """;

    private static JamlConfig Permissive() => ProofSearch.LoadOrThrow(PermissiveJaml);

    private const long SeedsPerBatch3 = 35L * 35 * 35;

    [Fact]
    public async Task StaticStride_FourThreads_CoverEveryBatchExactlyOnce()
    {
        const long start = 0,
            end = 40;
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(start)
            .WithEndBatchIndex(end)
            .WithThreadCount(4)
            .WithQuietMode(true)
            .CreateSearch();

        var task = search.RunSearchAsync();
        await search.WaitForCompletionAsync();
        await task;

        Assert.Equal((end - start) * SeedsPerBatch3, search.TotalSeedsSearched);
        Assert.Equal(end, search.CompletedBatchCount);
        Assert.Equal(end, search.ResumeBatchIndex);
        Assert.Equal(35L * 35 * 35 * 35 * 35, search.TotalBatchCount);

        Assert.True(search.SeedsPerSecond > 0, "per-thread rates should sum to a positive rate");
        Assert.True(search.ElapsedMs >= 0);
    }

    [Fact]
    public async Task StaticStride_SliceNarrowerThanThreadCount_ResumeIsFirstUnclaimedBatch()
    {
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(10)
            .WithEndBatchIndex(12)
            .WithThreadCount(4)
            .WithQuietMode(true)
            .CreateSearch();

        var task = search.RunSearchAsync();
        await search.WaitForCompletionAsync();
        await task;

        Assert.Equal(2 * SeedsPerBatch3, search.TotalSeedsSearched);
        Assert.Equal(12L, search.CompletedBatchCount);
        Assert.Equal(12L, search.ResumeBatchIndex);
    }

    [Fact]
    public void ProviderList_RateIsPositive_AndHasNoResumeCursor()
    {
        string[] seeds =
        [
            "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
            "99", "CC", "F", "Q", "R", "VV", "H", "I",
            "Z", "88", "AAAAAAAA", "MOTELY", "474", "3X3", "GHG", "4C4",
        ];
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(2)
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();

        Assert.False(search.IsSequentialBatchSearch);
        Assert.Equal(seeds.Length, (int)search.TotalSeedsSearched);
        Assert.True(search.SeedsPerSecond > 0, "the plan that chewed the list has a positive rate");
        Assert.Equal(-1L, search.ResumeBatchIndex);
    }
}
