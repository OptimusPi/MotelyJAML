using System.Net;
using System.Net.Http.Json;
using Motely.CLI.Party;
using Motely.MCP.Data;
using Motely.MCP.Party;
using Motely.MCP.Search;
using Motely.MCP.Tools;

namespace Motely.Tests;

public sealed class MotelyStoreAndPartyCoordinatorTests
{
    private static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "MotelyTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task FileMotelyStore_persists_filters_seeds_leaderboards_and_parties_across_restarts()
    {
        string tempDir = CreateTempDir();
        try
        {
            var store1 = new FileMotelyStore(tempDir);
            await store1.InitializeAsync();

            var now = DateTimeOffset.UtcNow;
            await store1.SaveFilterAsync(
                new SavedFilterRecord(
                    Slug: "custom-eye",
                    Name: "Custom Eye",
                    Author: "tester",
                    Description: "Eye in ante 3",
                    Deck: "Red",
                    Stake: "White",
                    JamlContent: "name: Custom Eye\nmust:\n  - boss: TheEye\n    antes: [3]\n",
                    CreatedAtUtc: now,
                    UpdatedAtUtc: now
                )
            );

            // Record seed and verify max-score upsert keeps higher score
            await store1.RecordSeedsAsync(
                [
                    new DiscoveredSeedRecord("SEED1111", "custom-eye", "Custom Eye", 10, [4, 6], "Red", "White", null, "w1", now),
                    new DiscoveredSeedRecord("SEED2222", "custom-eye", "Custom Eye", 25, [15, 10], "Red", "White", null, "w1", now),
                ]
            );
            await store1.RecordSeedsAsync(
                [
                    new DiscoveredSeedRecord("SEED1111", "custom-eye", "Custom Eye", 40, [20, 20], "Red", "White", null, "w2", now.AddSeconds(1)),
                    new DiscoveredSeedRecord("SEED1111", "custom-eye", "Custom Eye", 5, [5, 0], "Red", "White", null, "w3", now.AddSeconds(2)),
                ]
            );

            // Re-open from disk in a fresh instance
            var store2 = new FileMotelyStore(tempDir);
            await store2.InitializeAsync();

            var filter = await store2.GetFilterAsync("custom-eye");
            Assert.NotNull(filter);
            Assert.Equal("Custom Eye", filter.Name);

            var leaderboard = await store2.GetLeaderboardAsync("custom-eye");
            Assert.Equal(2, leaderboard.Count);
            Assert.Equal("SEED1111", leaderboard[0].Seed);
            Assert.Equal(40, leaderboard[0].Score);
            Assert.Equal("SEED2222", leaderboard[1].Seed);
            Assert.Equal(25, leaderboard[1].Score);

            var recent = await store2.GetRecentFindsAsync("custom-eye", limit: 10);
            Assert.Equal(2, recent.Count);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task PostgresMotelyStore_gracefully_falls_back_to_FileMotelyStore_when_unreachable()
    {
        string tempDir = CreateTempDir();
        try
        {
            await using var pgStore = new PostgresMotelyStore(
                "Host=127.0.0.1;Port=59999;Database=nonexistent;Username=none;Password=none;Timeout=1",
                tempDir
            );
            await pgStore.InitializeAsync();

            var now = DateTimeOffset.UtcNow;
            await pgStore.RecordSeedsAsync(
                [new DiscoveredSeedRecord("FALLBACK1", "test-slug", "Test", 99, [99], "Ghost", "White", null, "local", now)]
            );

            var finds = await pgStore.GetRecentFindsAsync("test-slug");
            var single = Assert.Single(finds);
            Assert.Equal("FALLBACK1", single.Seed);
            Assert.Equal(99, single.Score);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task PartyCoordinator_verifies_candidates_reclaims_expired_leases_and_runs_with_gpu_PartyWorker()
    {
        string tempDir = CreateTempDir();
        try
        {
            var store = new FileMotelyStore(tempDir);
            await store.InitializeAsync();
            var coordinator = new PartyCoordinator(store, "JamlFilters")
            {
                LeaseTtl = TimeSpan.FromMilliseconds(50)
            };

            string jaml = """
                name: PartyEyeTest
                deck: Red
                stake: White
                must:
                  - boss: TheEye
                    antes: [3, 4]
                should:
                  - voucher: Telescope
                    antes: [1, 2]
                    score: 10
                """;

            var party = await coordinator.CreatePartyAsync(
                new CreatePartyRequest(
                    FilterOrJaml: jaml,
                    Name: "PartyEyeTest",
                    BatchChars: 3,
                    BlockSize: 1,
                    TotalBlocks: 2
                )
            );

            // 1. Lease block 0 and verify active lease is persisted across coordinator restart
            var lease1 = await coordinator.NextLeaseAsync(party.PartyId);
            Assert.NotNull(lease1.Lease);
            Assert.Equal(0, lease1.Lease.StartBlock);

            var persistedParty = await store.GetPartyAsync(party.PartyId);
            Assert.NotNull(persistedParty?.ActiveLeases);
            Assert.Single(persistedParty!.ActiveLeases!);
            Assert.Equal(lease1.Lease.WorkerToken, persistedParty.ActiveLeases![0].WorkerToken);

            // Simulate server restart by creating a new PartyCoordinator over the same store
            var restartedCoordinator = new PartyCoordinator(store, "JamlFilters")
            {
                LeaseTtl = TimeSpan.FromSeconds(60)
            };
            await Task.Delay(100);

            // Expired heartbeat on the restarted coordinator must return 200 OK with Ok: false (not 400)
            var (hbCode, hbBody) = await restartedCoordinator.ReportAsync(
                new PartyReportRequestDto(
                    PartyId: party.PartyId,
                    WorkerToken: lease1.Lease.WorkerToken,
                    StartBlock: 0,
                    HeartbeatOnly: true
                )
            );
            Assert.Equal(200, hbCode);
            Assert.False(hbBody.Ok);

            // And block 0 must be reclaimed and re-issued by the restarted coordinator
            var reclaimedLease = await restartedCoordinator.NextLeaseAsync(party.PartyId);
            Assert.NotNull(reclaimedLease.Lease);
            Assert.Equal(0, reclaimedLease.Lease.StartBlock);

            // 2. Verify bogus seeds are rejected by server-side confirmation
            var (statusCode, fakeReport) = await restartedCoordinator.ReportAsync(
                new PartyReportRequestDto(
                    PartyId: party.PartyId,
                    WorkerToken: reclaimedLease.Lease.WorkerToken,
                    StartBlock: 0,
                    Seeds: ["NOTASEED!", "ZZZZZZZZ"],
                    HeartbeatOnly: false
                )
            );
            Assert.Equal(200, statusCode);
            Assert.True(fakeReport.Ok);
            Assert.Empty(fakeReport.Confirmed ?? []);
            Assert.Equal(2, (fakeReport.Rejected ?? []).Count);

            // 3. Connect PartyWorker with UseGpu = true over in-memory HttpClient to sweep remaining block 1
            using var httpClient = new HttpClient(new CoordinatorHttpHandler(restartedCoordinator))
            {
                BaseAddress = new Uri("http://localhost:5200")
            };

            var workerLog = new StringWriter();
            var partyClient = new PartyClient(httpClient, new Uri("http://localhost:5200"));
            var worker = new PartyWorker(
                partyClient,
                party.PartyId,
                new PartyWorkerOptions
                {
                    Threads = Environment.ProcessorCount,
                    UseGpu = true,
                },
                workerLog
            );

            var summary = await worker.RunAsync(CancellationToken.None);
            Assert.Equal(PartyWorkerOutcome.Stopped, summary.Outcome);
            Assert.Equal(0, PartyWorker.ExitCode(summary));

            var finalStatus = await restartedCoordinator.GetPartyStatusAsync(party.PartyId);
            Assert.NotNull(finalStatus);
            Assert.Equal("exhausted", finalStatus.Status);
            Assert.Equal(2, finalStatus.CompletedBlocks);
            Assert.True(finalStatus.MatchesFound > 0);
            Assert.NotEmpty(finalStatus.TopFinds);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task MotelyTools_gpu_party_and_database_tools_execute_end_to_end()
    {
        string tempDir = CreateTempDir();
        try
        {
            var store = new FileMotelyStore(tempDir);
            await store.InitializeAsync();
            var coordinator = new PartyCoordinator(store, "JamlFilters");
            var runner = new SearchRunner("JamlFilters", store);
            MotelyTools.Configure(store, coordinator, runner);

            var gpuStatus = MotelyTools.GpuStatus(batchChars: 3, batchCount: 4);
            Assert.True(gpuStatus.DeviceCount > 0);
            Assert.True(gpuStatus.Benchmark.GpuAvailable);

            var saved = await MotelyTools.SaveFilter(
                "mcp-gpu-test",
                "name: MCP GPU Test\ndeck: Red\nstake: White\nmust:\n  - boss: TheEye\n    antes: [3, 4]\nshould:\n  - voucher: Telescope\n    antes: [1, 2]\n    score: 5\n"
            );
            Assert.Equal("mcp-gpu-test", saved.Slug);

            var allSaved = await MotelyTools.SavedFilters();
            Assert.Contains(allSaved, f => f.Slug == "mcp-gpu-test");

            var searchRes = await MotelyTools.Search(
                filter: "mcp-gpu-test",
                mode: SeedSearchMode.Sequential,
                collect: 25,
                startBatch: 0,
                batchCount: 1,
                useGpu: true,
                saveToDb: true
            );
            Assert.True(searchRes.GpuUsed);
            Assert.True(searchRes.Matched > 0);

            var batchAnalysis = MotelyTools.AnalyzeBatch(
                string.Join(",", searchRes.Matches.Take(3)),
                filter: "mcp-gpu-test"
            );
            Assert.Equal(3, batchAnalysis.Count);
            Assert.Equal("MCP GPU Test", batchAnalysis.Filter);
            Assert.All(batchAnalysis.Seeds, s =>
            {
                Assert.Null(s.Error);
                Assert.True(s.Matched);
                Assert.NotNull(s.Score);
                Assert.NotNull(s.Tallies);
            });

            var party = await MotelyTools.PartyCreate("mcp-gpu-test", name: "MCP Party", batchChars: 3, blockSize: 1, totalBlocks: 2);
            var sweep = await MotelyTools.PartySweepGpu(party.PartyId, blocks: 1, useGpu: true);
            Assert.True(sweep.GpuUsed);
            Assert.Equal(42_875, sweep.SeedsSearched);
            Assert.True(sweep.Confirmed > 0);

            var recent = await MotelyTools.RecentFinds("mcp-gpu-test", limit: 10);
            Assert.NotEmpty(recent);
        }
        finally
        {
            MotelyTools.Configure(null!, null!, null!);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private sealed class CoordinatorHttpHandler(PartyCoordinator coordinator) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Get && path == "/api/party/next")
            {
                var query = request.RequestUri?.Query ?? "";
                string? partyId = null;
                foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && kv[0] == "partyId")
                        partyId = Uri.UnescapeDataString(kv[1]);
                }

                var next = await coordinator.NextLeaseAsync(partyId, cancellationToken: cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(next)
                };
            }

            if (request.Method == HttpMethod.Post && path == "/api/party/report")
            {
                var body = await request.Content!.ReadFromJsonAsync<PartyReportRequestDto>(cancellationToken: cancellationToken);
                var (code, res) = await coordinator.ReportAsync(body!, cancellationToken);
                return new HttpResponseMessage((HttpStatusCode)code)
                {
                    Content = JsonContent.Create(res)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
