using Motely.Enums;
using Motely.Filters;
using Motely.Lake;
using Motely.MCP.Tools;

namespace Motely.Tests;

public sealed class SeedLakeTests
{
    private const string DungmotPath = @"D:\dungmot";

    [Fact]
    public void SeedLakeService_GetAvailableLakes_discovers_curated_csv_and_parquet_partitions()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var lakes = lakeService.GetAvailableLakes();

        Assert.NotEmpty(lakes);

        // Curated ranks (10_best.txt, etc.)
        var curatedRanks = lakes.Where(l => l.Category == LakeCategory.CuratedRanks).ToList();
        Assert.True(curatedRanks.Count >= 13, $"Expected >=13 curated ranks, found {curatedRanks.Count}");
        Assert.Contains(curatedRanks, l => l.Partition == "10");
        Assert.Contains(curatedRanks, l => l.Partition == "Ace");

        // Curated suits
        var curatedSuits = lakes.Where(l => l.Category == LakeCategory.CuratedSuits).ToList();
        Assert.True(curatedSuits.Count >= 4, $"Expected >=4 curated suits, found {curatedSuits.Count}");
        Assert.Contains(curatedSuits, l => l.Partition == "Club");

        // Negative uncommons
        var uncommon = lakes.FirstOrDefault(l => l.Category == LakeCategory.NegativeUncommon);
        Assert.NotNull(uncommon);
        Assert.Equal("uncommonss", uncommon.Partition);

        // Parquet lakes
        var pqRanks = lakes.Where(l => l.Category == LakeCategory.Ranks).ToList();
        Assert.Contains(pqRanks, l => l.Partition == "little_10s");
        Assert.Contains(pqRanks, l => l.Partition == "10s");

        var pqSuits = lakes.Where(l => l.Category == LakeCategory.Suits).ToList();
        Assert.Contains(pqSuits, l => l.Partition == "little_Clubs");
        Assert.Contains(pqSuits, l => l.Partition == "Clubs");
    }

    [Fact]
    public void SeedLakeService_ReadSeeds_curated_rank_returns_valid_seeds()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var seeds = lakeService.ReadSeeds(LakeCategory.CuratedRanks, "10", limit: 50).ToList();

        Assert.NotEmpty(seeds);
        Assert.Equal(50, seeds.Count);
        foreach (var s in seeds)
        {
            Assert.Equal(8, s.Length);
            Assert.True(s.All(c => char.IsAsciiLetterOrDigit(c) && !char.IsLower(c)));
        }
    }

    [Fact]
    public void SeedLakeService_ReadSeeds_negative_uncommon_csv_returns_parsed_seeds()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var seeds = lakeService.ReadSeeds(LakeCategory.NegativeUncommon, "uncommonss", limit: 20).ToList();

        Assert.NotEmpty(seeds);
        Assert.Equal(20, seeds.Count);
        Assert.Equal("111S8CHT", seeds[0]);
    }

    [Fact]
    public void SeedLakeService_ReadSeeds_parquet_rank_reads_streaming_rows()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var seeds = lakeService.ReadSeeds(LakeCategory.Ranks, "little_10s", limit: 25).ToList();

        Assert.NotEmpty(seeds);
        Assert.Equal(25, seeds.Count);
        foreach (var s in seeds)
        {
            Assert.True(s.Length is >= 1 and <= 8);
        }
    }

    [Fact]
    public async Task SeedLakeService_CreateSeedProvider_integrates_with_MotelySearch()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var provider = lakeService.CreateSeedProvider(LakeCategory.CuratedRanks, "10", limit: 30);

        string jaml = """
            name: LakeCuratedSearch
            deck: Red
            stake: White
            must:
              - erraticRank: Ten
                min: 4
            """;
        var config = JamlConfigLoader.FromJaml(jaml);
        var plan = MotelySearchBuilder.CreatePlan(config, int.MinValue);

        var matches = new List<string>();
        var settings = plan.Settings
            .WithSourceConfig(config)
            .WithProviderSearch(provider)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s =>
            {
                lock (matches) matches.Add(s);
            });

        using var search = settings.Start();
        await search.WaitForCompletionAsync();

        Assert.True(search.TotalSeedsSearched > 0);
    }

    [Fact]
    public void MotelyTools_LakeList_and_LakeQuery_return_structured_content()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var listResult = MotelyTools.LakeList();
        Assert.True(listResult.Count > 0);
        Assert.NotEmpty(listResult.Lakes);

        var queryResult = MotelyTools.LakeQuery(LakeCategory.CuratedRanks, "10", limit: 15);
        Assert.Equal(15, queryResult.Count);
        Assert.Equal("CuratedRanks", queryResult.Category);
        Assert.Equal("10", queryResult.Partition);
        Assert.Equal(15, queryResult.Seeds.Count);
    }

    [Fact]
    public async Task MotelyTools_Search_with_LakeMode_executes_filter_over_lake()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        string jaml = """
            name: LakeModeSearch
            deck: Red
            stake: White
            """;

        var result = await MotelyTools.Search(
            jaml: jaml,
            mode: SeedSearchMode.Lake,
            lakeCategory: LakeCategory.CuratedRanks,
            lakePartition: "10",
            collect: 10
        );

        Assert.Equal(10, result.Searched);
        Assert.Equal(10, result.Matched);
        Assert.Equal(10, result.Matches.Count);
    }
}
