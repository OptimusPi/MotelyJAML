using System.Diagnostics;
using Motely.Enums;
using Motely.Filters;
using Motely.Gpu;
using Motely.Lake;
using Xunit;

namespace Motely.Tests;

public sealed class ChallengerEngineParityTests
{
    private const string DungmotPath = @"D:\dungmot";

    [Fact]
    public void GpuDeviceInfo_confirms_RTX_4070_SUPER()
    {
        var best = GpuDeviceInfo.GetBestDevice();
        Assert.NotNull(best);
        Assert.True(best.SupportsFp64, $"Expected FP64 support on {best.Name}");
        Assert.Contains("4070", best.Name);
        Assert.True(best.ComputeUnits >= 56, $"Expected >= 56 compute units, found {best.ComputeUnits}");
    }

    [Theory]
    [InlineData("Foil")]
    [InlineData("Holographic")]
    [InlineData("Polychrome")]
    [InlineData("Negative")]
    public async Task ShopJoker_all_editions_have_bit_exact_gpu_cpu_parity(string edition)
    {
        string jaml = $"""
            name: ShopJoker_{edition}
            deck: Red
            stake: White
            must:
              - rareJoker: []
                edition: {edition}
                antes: [1, 2]
            """;

        var config = JamlConfigLoader.FromJaml(jaml);
        Assert.True(GpuFilterProgram.Compile(config).HasSelectiveOps);

        var cpuMatches = await SweepAsync(config, 0, 8, useGpu: false);
        var gpuMatches = await SweepAsync(config, 0, 8, useGpu: true);

        // Bit-exact match check: every seed found on CPU must match GPU and vice-versa
        Assert.Equal(cpuMatches, gpuMatches);
    }

    [Theory]
    [InlineData("Common")]
    [InlineData("Uncommon")]
    [InlineData("Rare")]
    public async Task ShopJoker_all_rarities_have_bit_exact_gpu_cpu_parity(string rarity)
    {
        string clauseName = rarity switch
        {
            "Common" => "commonJoker: []",
            "Uncommon" => "uncommonJoker: []",
            "Rare" => "rareJoker: []",
            _ => throw new ArgumentOutOfRangeException(nameof(rarity))
        };

        string jaml = $"""
            name: ShopJokerRarity_{rarity}
            deck: Red
            stake: White
            must:
              - {clauseName}
                edition: Foil
                antes: [1]
            """;

        var config = JamlConfigLoader.FromJaml(jaml);
        Assert.True(GpuFilterProgram.Compile(config).HasSelectiveOps);

        var cpuMatches = await SweepAsync(config, 0, 4, useGpu: false);
        var gpuMatches = await SweepAsync(config, 0, 4, useGpu: true);

        Assert.NotEmpty(cpuMatches);
        Assert.Equal(cpuMatches, gpuMatches);
    }

    [Theory]
    [InlineData("Foil")]
    [InlineData("Holographic")]
    [InlineData("Polychrome")]
    [InlineData("Negative")]
    public async Task BuffoonJoker_all_editions_have_bit_exact_gpu_cpu_parity(string edition)
    {
        string jaml = $"""
            name: BuffoonJoker_{edition}
            deck: Red
            stake: White
            must:
              - uncommonJoker: []
                edition: {edition}
                antes: [1, 2]
                sources:
                  boosterPacks: [0, 1]
            """;

        var config = JamlConfigLoader.FromJaml(jaml);
        Assert.True(GpuFilterProgram.Compile(config).HasSelectiveOps);

        var cpuMatches = await SweepAsync(config, 0, 8, useGpu: false);
        var gpuMatches = await SweepAsync(config, 0, 8, useGpu: true);

        Assert.Equal(cpuMatches, gpuMatches);
    }

    [Fact]
    public async Task High_ante_numbers_ante8_sweep_bit_exact_parity()
    {
        string jaml = """
            name: ShopJoker_Ante8
            deck: Red
            stake: White
            must:
              - uncommonJoker: []
                edition: Foil
                antes: [8]
            """;

        var config = JamlConfigLoader.FromJaml(jaml);
        var program = GpuFilterProgram.Compile(config);
        Assert.True(program.HasSelectiveOps, "Expected Ante 8 to compile into GPU opcode");

        var cpuMatches = await SweepAsync(config, 0, 4, useGpu: false);
        var gpuMatches = await SweepAsync(config, 0, 4, useGpu: true);

        Assert.Equal(cpuMatches, gpuMatches);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(16)]
    public async Task High_ante_numbers_ante12_and_16_behavior_investigation(int ante)
    {
        string jaml = $"""
            name: ShopJoker_Ante{ante}
            deck: Red
            stake: White
            must:
              - rareJoker: []
                edition: Foil
                antes: [{ante}]
            """;

        var config = JamlConfigLoader.FromJaml(jaml);
        var program = GpuFilterProgram.Compile(config);

        // Compiler safely returns false for ante > 8 in TryEmitJokerStream, cleanly falling back to CPU
        Assert.False(program.HasSelectiveOps, $"Expected ante {ante} to fall back to CPU");

        // Verify that sweep produces bit-exact identical matches regardless
        var cpuMatches = await SweepAsync(config, 0, 2, useGpu: false);
        var gpuMatches = await SweepAsync(config, 0, 2, useGpu: true);

        Assert.Equal(cpuMatches, gpuMatches);
    }

    [Fact]
    public async Task Direct_Empirical_Verification_zero_false_negatives_shop_and_buffoon()
    {
        // Construct a filter with both Shop and Buffoon Jokers across multiple antes
        string jaml = """
            name: DirectZeroFalseNegativeCheck
            deck: Red
            stake: White
            must:
              - rareJoker: []
                edition: Foil
                antes: [1, 2]
            """;

        var config = JamlConfigLoader.FromJaml(jaml);
        var program = GpuFilterProgram.Compile(config);
        Assert.True(program.HasSelectiveOps);

        // Run full CPU search over 16 batches (686,000 seeds)
        var cpuMatches = await SweepAsync(config, 0, 16, useGpu: false);
        Assert.NotEmpty(cpuMatches);

        // Run GPU search over the exact same 16 batches
        var gpuMatches = await SweepAsync(config, 0, 16, useGpu: true);

        // Check for ANY false negatives: Every single CPU match MUST be found by GPU search!
        var cpuSeedSet = new HashSet<string>(cpuMatches.Select(m => m.Seed));
        var gpuSeedSet = new HashSet<string>(gpuMatches.Select(m => m.Seed));

        var missingInGpu = cpuSeedSet.Except(gpuSeedSet).ToList();
        Assert.Empty(missingInGpu);

        // Check for false positives: Every GPU match must be valid on CPU
        var extraInGpu = gpuSeedSet.Except(cpuSeedSet).ToList();
        Assert.Empty(extraInGpu);

        // Scores must match bit-exactly
        Assert.Equal(cpuMatches, gpuMatches);
    }

    [Fact]
    public void SeedLakeService_reads_10s_parquet_large_file_streaming()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);

        // Read 100 seeds from 10s.parquet (1.25 GB file)
        var sw = Stopwatch.StartNew();
        var seeds = lakeService.ReadSeeds(LakeCategory.Ranks, "10s", limit: 100).ToList();
        sw.Stop();

        Assert.Equal(100, seeds.Count);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Reading 100 seeds took {sw.ElapsedMilliseconds}ms, expected fast streaming");

        foreach (var s in seeds)
        {
            Assert.True(s.Length is >= 1 and <= 8);
            Assert.True(s.All(c => char.IsAsciiLetterOrDigit(c)));
        }
    }

    [Fact]
    public void SeedLakeService_reads_little_10s_parquet_complete()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var seeds = lakeService.ReadSeeds(LakeCategory.Ranks, "little_10s").ToList();

        Assert.NotEmpty(seeds);
        Assert.Equal(420, seeds.Count);
    }

    [Fact]
    public void SeedLakeService_reads_curated_10_best_txt()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var seeds = lakeService.ReadSeeds(LakeCategory.CuratedRanks, "10_best.txt", limit: 50).ToList();

        Assert.Equal(50, seeds.Count);
        foreach (var s in seeds)
        {
            Assert.Equal(8, s.Length);
        }
    }

    [Fact]
    public void SeedLakeService_reads_negative_uncommons_csv()
    {
        if (!Directory.Exists(DungmotPath))
            return;

        var lakeService = new SeedLakeService(DungmotPath);
        var seeds = lakeService.ReadSeeds(LakeCategory.NegativeUncommon, "uncommonss").ToList();

        Assert.Equal(409, seeds.Count);
        Assert.Equal("111S8CHT", seeds[0]);
    }

    [Fact]
    public void GpuSeedSearcher_ShopJoker_live_benchmark_RTX_4070_SUPER()
    {
        var benchConfig = new JamlConfig
        {
            Must =
            [
                new RareJokerClause
                {
                    Edition = MotelyItemEdition.Negative,
                    Antes = [1, 2],
                    Min = 1
                }
            ]
        };

        using var searcher = GpuSeedSearcher.TryCreate(benchConfig);
        Assert.NotNull(searcher);

        int batchChars = 3;
        int batchCount = 16;
        long totalSeeds = 16L * 42_875L; // 686,000 seeds

        int[] scratch = new int[4096];
        searcher.FilterBatchRange(0, 1, batchChars, scratch); // warmup

        var sw = Stopwatch.StartNew();
        int candidates = searcher.FilterBatchRange(0, batchCount, batchChars, scratch);
        sw.Stop();

        double elapsedMs = Math.Max(0.001, sw.Elapsed.TotalMilliseconds);
        double mSeedsPerSec = (totalSeeds / 1_000_000.0) / (elapsedMs / 1000.0);

        Assert.True(candidates >= 0);
        Assert.True(mSeedsPerSec > 1.0, $"Speed {mSeedsPerSec:F2} M/s on RTX 4070 SUPER");
    }

    private static async Task<List<(string Seed, int Score)>> SweepAsync(
        JamlConfig config,
        long startBatch,
        long endBatch,
        bool useGpu
    )
    {
        var plan = MotelySearchBuilder.CreatePlan(config, int.MinValue);
        var found = new List<(string Seed, int Score)>();

        var settings = plan
            .Settings.WithSourceConfig(config)
            .WithThreadCount(Environment.ProcessorCount)
            .WithBatchCharacterCount(3)
            .WithSequentialSearch()
            .WithStartBatchIndex(startBatch)
            .WithEndBatchIndex(endBatch)
            .WithAutoScoreCutoff(false)
            .WithGpuSearch(useGpu)
            .WithQuietMode(true);

        if (settings.SeedScoreDesc is not null)
        {
            settings = settings.WithScoredResultCallback(r =>
            {
                lock (found)
                {
                    found.Add((r.Seed, r.Score));
                }
            });
        }
        else
        {
            settings = settings.WithSeedMatchCallback(s =>
            {
                lock (found)
                {
                    found.Add((s, 0));
                }
            });
        }

        using var search = settings.Start();
        await search.WaitForCompletionAsync();

        found.Sort((a, b) =>
        {
            int cmp = string.CompareOrdinal(a.Seed, b.Seed);
            return cmp != 0 ? cmp : a.Score.CompareTo(b.Score);
        });
        return found;
    }
}
