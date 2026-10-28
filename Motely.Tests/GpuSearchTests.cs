using Motely.Filters;
using Motely.Gpu;

namespace Motely.Tests;

public sealed class GpuSearchTests
{
    [Fact]
    public void GpuDeviceInfo_discovers_host_opencl_device_with_fp64()
    {
        var devices = GpuDeviceInfo.DiscoverDevices();
        var best = GpuDeviceInfo.GetBestDevice();

        Assert.NotEmpty(devices);
        Assert.NotNull(best);
        Assert.True(best.SupportsFp64, $"Expected FP64 support on {best.Name}");
        Assert.True(best.ComputeUnits > 0);
        Assert.True(best.GlobalMemoryBytes > 0);
    }

    [Fact]
    public void GpuSeedSearcher_benchmark_executes_opencl_fp64_kernel()
    {
        var bench = GpuSeedSearcher.Benchmark(batchChars: 3, batchCount: 16);

        Assert.True(bench.GpuAvailable);
        Assert.False(string.IsNullOrWhiteSpace(bench.DeviceName));
        Assert.Equal(16L * 42_875L, bench.SeedsEvaluated);
        Assert.True(bench.ElapsedMilliseconds > 0);
        Assert.True(bench.MillionsOfSeedsPerSecond > 1.0, $"Expected >1M seeds/sec, got {bench.MillionsOfSeedsPerSecond:F2}M/s");
    }

    [Theory]
    [InlineData(
        """
        name: VoucherParity
        deck: Red
        stake: White
        must:
          - voucher: Telescope
            antes: [1, 2]
        """,
        0,
        2
    )]
    [InlineData(
        """
        name: TagParity
        deck: Ghost
        stake: White
        must:
          - tag: NegativeTag
            antes: [1, 2]
        """,
        0,
        2
    )]
    [InlineData(
        """
        name: BossParity
        deck: Red
        stake: White
        must:
          - boss: TheEye
            antes: [3, 4]
        mustNot:
          - boss: TheHook
            antes: [1]
        """,
        0,
        2
    )]
    [InlineData(
        """
        name: SoulEditionParity
        deck: Ghost
        stake: White
        must:
          - legendaryJoker: Perkeo
            edition: Negative
            antes: [1, 2, 3, 4]
        should:
          - voucher: CrystalBall
            antes: [1, 2, 3]
            score: 5
        """,
        0,
        4
    )]
    [InlineData(
        """
        name: SoulPackParity
        deck: Red
        stake: White
        must:
          - spectralCard: TheSoul
            antes: [1]
            sources:
              boosterPacks: [0, 1]
        """,
        0,
        3
    )]
    public async Task Gpu_and_Cpu_sequential_sweeps_produce_bit_exact_identical_matches_and_scores(
        string jaml,
        long startBatch,
        long endBatch
    )
    {
        var config = JamlConfigLoader.FromJaml(jaml);
        Assert.True(GpuFilterProgram.Compile(config).HasSelectiveOps);

        var cpuMatches = await SweepAsync(config, startBatch, endBatch, useGpu: false);
        var gpuMatches = await SweepAsync(config, startBatch, endBatch, useGpu: true);

        Assert.NotEmpty(cpuMatches);
        Assert.Equal(cpuMatches, gpuMatches);
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
