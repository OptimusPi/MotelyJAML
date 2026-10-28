using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Motely.Party;
using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// The Search Party worker (<c>motely party</c>) against a fake seedfinder.app coordinator. The
/// expected seeds come from running the engine directly over the leased blocks with the same JAML.
/// </summary>
public sealed class PartyWorkerTests
{
    private const string PartyId = "party-test";

    // Moderately loose and scored: a must that holds for some seeds, a should that varies.
    private const string LooseJaml = """
        deck: Red
        stake: White
        must:
          - voucher: Overstock
            antes: [1]
        should:
          - joker: Any
            antes: [1]
        """;

    // Matches only where a spectral card is in the ante-1 shop: the Ghost deck's shop has them.
    private const string GhostShopJaml = """
        deck: Red
        stake: White
        must:
          - spectralCard: Any
            antes: [1]
            sources:
              shopItems: [0, 1, 2, 3, 4, 5, 6, 7]
        """;

    // Matches only where an Eternal joker shows in ante 1: Black stake and up.
    private const string EternalJaml = """
        deck: Red
        stake: White
        must:
          - joker: Any
            antes: [1]
            stickers: [Eternal]
        """;

    private const string NoClauseJaml = "deck: Red\nstake: White\n";

    // ---- the engine, run directly: what the worker must report ----

    private static Dictionary<string, int> EngineFinds(
        string jaml,
        int batchChars,
        long start,
        long end,
        MotelyDeck? deck = null,
        MotelyStake? stake = null
    )
    {
        var config = JamlConfigLoader.FromJaml(jaml);
        var settings = MotelySearchBuilder.CreatePlan(config, int.MinValue).Settings;
        if (deck is { } d)
            settings = settings.WithDeck(d);
        if (stake is { } s)
            settings = settings.WithStake(s);
        settings = settings
            .WithThreadCount(1)
            .WithBatchCharacterCount(batchChars)
            .WithSequentialSearch()
            .WithStartBatchIndex(start)
            .WithEndBatchIndex(end)
            .WithAutoScoreCutoff(false) // on by default: it would thin out a flood of finds
            .WithQuietMode(true);

        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        settings = settings.SeedScoreDesc is not null
            ? settings.WithScoredResultCallback(r =>
            {
                lock (found)
                    found[r.Seed] = r.Score;
            })
            : settings.WithSeedMatchCallback(line =>
            {
                lock (found)
                    found[line.Split(',')[0]] = 0;
            });
        using var search = settings.Start();
        search.AwaitCompletion();
        return found;
    }

    // ---- the fake coordinator ----

    private static string LeaseJson(
        string jaml,
        int batchChars,
        long startBlock,
        long blockCount,
        string token,
        string? deck = null,
        string? stake = null
    ) =>
        new JsonObject
        {
            ["lease"] = new JsonObject
            {
                ["partyId"] = PartyId,
                ["jaml"] = jaml,
                ["deck"] = deck,
                ["stake"] = stake,
                ["batchChars"] = batchChars,
                ["startBlock"] = startBlock,
                ["blockCount"] = blockCount,
                ["totalBlocks"] = MotelyGlobals.SequentialBatchCount(batchChars),
                ["workerToken"] = token,
                ["expiresAt"] = DateTime.UtcNow.AddSeconds(60).ToString("O"),
            },
        }.ToJsonString();

    private static string Done(string reason) => new JsonObject { ["done"] = true, ["reason"] = reason }.ToJsonString();

    private sealed record Report(string WorkerToken, long StartBlock, string[] Seeds);

    private sealed class FakeCoordinator : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _next = new();
        private readonly object _gate = new();

        /// <summary>The /next answer once the scripted ones run out.</summary>
        public string Fallback { get; set; } = Done("complete");

        /// <summary>Answers the real reports get, in order; past the end they are confirmed echoes.</summary>
        public Queue<(HttpStatusCode Status, string Body)> ReportAnswers { get; } = new();

        public int NextCalls { get; private set; }
        public List<Report> Reports { get; } = [];
        public List<Report> Heartbeats { get; } = [];

        /// <summary>Every request in order: "next", "heartbeat" or "report".</summary>
        public List<string> Log { get; } = [];

        public FakeCoordinator Then(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _next.Enqueue((status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/party/next")
            {
                Assert.Equal($"?partyId={PartyId}", request.RequestUri.Query);
                lock (_gate)
                {
                    NextCalls++;
                    Log.Add("next");
                    var (status, body) = _next.Count > 0 ? _next.Dequeue() : (HttpStatusCode.OK, Fallback);
                    return Json(status, body);
                }
            }

            if (request.Method == HttpMethod.Post && path == "/api/party/report")
            {
                string text = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                Assert.Equal(PartyId, root.GetProperty("partyId").GetString());
                var report = new Report(
                    root.GetProperty("workerToken").GetString()!,
                    root.GetProperty("startBlock").GetInt64(),
                    root.TryGetProperty("seeds", out var seeds) ? [.. seeds.EnumerateArray().Select(e => e.GetString()!)] : []
                );
                lock (_gate)
                {
                    if (root.TryGetProperty("heartbeatOnly", out var hb) && hb.GetBoolean())
                    {
                        Heartbeats.Add(report);
                        Log.Add("heartbeat");
                        return Json(HttpStatusCode.OK, """{"ok":true}""");
                    }
                    Reports.Add(report);
                    Log.Add("report");
                    if (ReportAnswers.Count > 0)
                    {
                        var (status, body) = ReportAnswers.Dequeue();
                        return Json(status, body);
                    }
                    var answer = new JsonObject
                    {
                        ["confirmed"] = new JsonArray([.. report.Seeds.Select(s => (JsonNode)s)]),
                        ["rejected"] = new JsonArray(),
                        ["recorded"] = report.Seeds.Length,
                    };
                    return Json(HttpStatusCode.OK, answer.ToJsonString());
                }
            }

            return Json(HttpStatusCode.NotFound, """{"error":"not found"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static async Task<(PartyWorkerSummary Summary, string Log)> RunAsync(
        FakeCoordinator fake,
        PartyWorkerOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        using var http = new HttpClient(fake);
        var log = new StringWriter();
        var worker = new PartyWorker(
            new PartyClient(http, new Uri("http://coordinator.test/")),
            PartyId,
            options ?? FastOptions(),
            log
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60)); // a hung worker fails the test, not the run
        var summary = await worker.RunAsync(timeout.Token);
        return (summary, log.ToString());
    }

    private static PartyWorkerOptions FastOptions(int threads = 2) =>
        new()
        {
            Threads = threads,
            FullyLeasedDelay = TimeSpan.FromMilliseconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(10),
        };

    // ---- tests ----

    [Fact]
    public async Task TwoLeasesThenDone_ReportsExactlyTheEngineSeedsOfEachRange()
    {
        var fake = new FakeCoordinator()
            .Then(LeaseJson(LooseJaml, 1, 0, 20, "tok-a"))
            .Then(LeaseJson(LooseJaml, 1, 500, 30, "tok-b"))
            .Then(Done("complete"));

        var (summary, log) = await RunAsync(fake);

        var expectedA = EngineFinds(LooseJaml, 1, 0, 20);
        var expectedB = EngineFinds(LooseJaml, 1, 500, 530);
        Assert.NotEmpty(expectedA);
        Assert.NotEmpty(expectedB);

        Assert.Equal(PartyWorkerOutcome.Stopped, summary.Outcome);
        Assert.Equal("complete", summary.Reason);
        Assert.Equal(0, PartyWorker.ExitCode(summary));
        Assert.Equal(3, fake.NextCalls);
        Assert.Equal(2, fake.Reports.Count);

        Assert.Equal(("tok-a", 0L), (fake.Reports[0].WorkerToken, fake.Reports[0].StartBlock));
        Assert.Equal(expectedA.Keys.Order(StringComparer.Ordinal), fake.Reports[0].Seeds.Order(StringComparer.Ordinal));
        Assert.Equal(("tok-b", 500L), (fake.Reports[1].WorkerToken, fake.Reports[1].StartBlock));
        Assert.Equal(expectedB.Keys.Order(StringComparer.Ordinal), fake.Reports[1].Seeds.Order(StringComparer.Ordinal));

        Assert.Equal(2, summary.Leases);
        Assert.Equal(50, summary.Blocks);
        Assert.Equal(50 * 35, summary.SeedsSearched);
        Assert.Equal(expectedA.Count + expectedB.Count, summary.Matches);
        Assert.Equal(expectedA.Count + expectedB.Count, summary.Confirmed);
        Assert.Contains(
            $"lease blocks 0-19 (20 × 35 seeds) … {expectedA.Count} matches, reported: {expectedA.Count} confirmed",
            log
        );
        Assert.Contains($"lease blocks 500-529 (30 × 35 seeds) …", log);
    }

    [Fact]
    public async Task ClauseLessFilter_ReportsEverySeedOfTheBlock_InTheProtocolsNumbering()
    {
        // PROTOCOL.md: at batchChars n, block b is every seed whose first n characters vary and whose
        // last 8 - n are b's base-35 digits, least significant first. 37 = 2 + 1·35 → "?3211111".
        var fake = new FakeCoordinator().Then(LeaseJson(NoClauseJaml, 1, 37, 1, "tok"));

        await RunAsync(fake);

        var seeds = Assert.Single(fake.Reports).Seeds;
        Assert.Equal(35, seeds.Length);
        Assert.All(seeds, s => Assert.Matches("^[1-9A-Z]3211111$", s));
        Assert.Equal(35, seeds.Distinct().Count());
        Assert.Equal(EngineFinds(NoClauseJaml, 1, 37, 38).Keys.Order(StringComparer.Ordinal), seeds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MoreThan1000Matches_SendsThe1000BestByScore()
    {
        const string jaml = """
            deck: Red
            stake: White
            should:
              - joker: Any
                antes: [1, 2]
              - tarotCard: Any
                antes: [1]
            """;
        var fake = new FakeCoordinator().Then(LeaseJson(jaml, 2, 0, 3, "tok"));

        // One thread over three blocks: the engine's default auto cutoff would thin this flood of
        // finds after the first block, so this also pins that the worker turns it off.
        var (summary, log) = await RunAsync(fake, FastOptions(threads: 1));

        var expected = EngineFinds(jaml, 2, 0, 3);
        Assert.True(expected.Count > 1000, $"the filter should match more than 1000 of 3,675 seeds, got {expected.Count}");
        Assert.True(expected.Values.Distinct().Count() > 1, "scores should vary, or 'best' means nothing");

        var sent = Assert.Single(fake.Reports).Seeds;
        Assert.Equal(1000, sent.Length);
        Assert.Equal(PartySweep.Best(expected, 1000).Order(StringComparer.Ordinal), sent.Order(StringComparer.Ordinal));
        // Nothing left out scores above anything sent.
        int lowestSent = sent.Min(s => expected[s]);
        Assert.All(expected.Where(kv => !sent.Contains(kv.Key)), kv => Assert.True(kv.Value <= lowestSent));

        Assert.Equal(expected.Count, summary.Matches);
        Assert.Equal(1000, summary.Reported);
        Assert.Contains("(best 1,000 sent)", log);
    }

    [Fact]
    public async Task LeaseDeck_OverridesTheJamlDeck()
    {
        var fake = new FakeCoordinator()
            .Then(LeaseJson(GhostShopJaml, 1, 0, 40, "ghost", deck: "Ghost"))
            .Then(LeaseJson(GhostShopJaml, 1, 0, 40, "jaml-deck"));

        await RunAsync(fake);

        var ghost = EngineFinds(GhostShopJaml, 1, 0, 40, deck: MotelyDeck.Ghost);
        var red = EngineFinds(GhostShopJaml, 1, 0, 40);
        Assert.True(ghost.Count > red.Count, $"Ghost {ghost.Count} vs Red {red.Count}: the filter should tell the decks apart");

        Assert.Equal(2, fake.Reports.Count);
        Assert.Equal(ghost.Keys.Order(StringComparer.Ordinal), fake.Reports[0].Seeds.Order(StringComparer.Ordinal));
        // deck: null keeps the JAML's own deck.
        Assert.Equal(red.Keys.Order(StringComparer.Ordinal), fake.Reports[1].Seeds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LeaseStake_OverridesTheJamlStake()
    {
        var fake = new FakeCoordinator().Then(LeaseJson(EternalJaml, 1, 0, 20, "tok", stake: "black"));

        await RunAsync(fake);

        var black = EngineFinds(EternalJaml, 1, 0, 20, stake: MotelyStake.Black);
        var white = EngineFinds(EternalJaml, 1, 0, 20);
        Assert.True(black.Count > white.Count, $"Black {black.Count} vs White {white.Count}");
        Assert.Equal(black.Keys.Order(StringComparer.Ordinal), Assert.Single(fake.Reports).Seeds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task HeartbeatsWhileSweeping_ThenOneReport()
    {
        var fake = new FakeCoordinator().Then(LeaseJson(LooseJaml, 1, 7, 3, "tok-hb"));
        var options = new PartyWorkerOptions
        {
            Threads = 1,
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            // A sweep that takes a while, so the heartbeat has time to beat.
            Sweep = async (lease, threads, ct) =>
            {
                await Task.Delay(400, ct);
                return await PartySweep.RunAsync(lease, threads, ct);
            },
        };

        await RunAsync(fake, options);

        Assert.True(fake.Heartbeats.Count >= 3, $"{fake.Heartbeats.Count} heartbeat(s)");
        Assert.All(fake.Heartbeats, h => Assert.Equal(("tok-hb", 7L, 0), (h.WorkerToken, h.StartBlock, h.Seeds.Length)));
        Assert.Single(fake.Reports);
        // The report comes after the last heartbeat: none beats for a lease already reported.
        Assert.Equal("report", fake.Log.Last(e => e != "next"));
    }

    [Theory]
    [InlineData("complete", 0)]
    [InlineData("exhausted", 0)]
    [InlineData("cancelled", 0)]
    [InlineData("unknown party", 1)]
    public async Task TerminalReason_StopsAtOnce(string reason, int exitCode)
    {
        var fake = new FakeCoordinator { Fallback = Done(reason) };

        var (summary, log) = await RunAsync(fake);

        Assert.Equal(PartyWorkerOutcome.Stopped, summary.Outcome);
        Assert.Equal(reason, summary.Reason);
        Assert.Equal(exitCode, PartyWorker.ExitCode(summary));
        Assert.Equal(1, fake.NextCalls);
        Assert.Empty(fake.Reports);
        Assert.Equal("", log);
    }

    [Fact]
    public async Task FullyLeased_WaitsAndAsksAgain()
    {
        var fake = new FakeCoordinator()
            .Then(Done("fully leased"))
            .Then(Done("fully leased"))
            .Then(LeaseJson(LooseJaml, 1, 0, 2, "tok"))
            .Then(Done("fully leased"))
            .Then(Done("exhausted"));

        var (summary, log) = await RunAsync(fake);

        Assert.Equal(PartyWorkerOutcome.Stopped, summary.Outcome);
        Assert.Equal("exhausted", summary.Reason);
        Assert.Equal(5, fake.NextCalls);
        Assert.Single(fake.Reports);
        Assert.Contains("leased to other workers", log);
    }

    [Fact]
    public async Task FullyLeased_IsBoundedByMaxWaits()
    {
        var fake = new FakeCoordinator { Fallback = Done("fully leased") };
        var options = new PartyWorkerOptions
        {
            FullyLeasedDelay = TimeSpan.FromMilliseconds(5),
            MaxFullyLeasedWaits = 4,
        };

        var (summary, _) = await RunAsync(fake, options);

        Assert.Equal(PartyWorkerOutcome.WaitLimit, summary.Outcome);
        Assert.Equal(4, fake.NextCalls);
        Assert.Empty(fake.Reports);
    }

    [Fact]
    public async Task Once_StopsAfterOneLease()
    {
        var fake = new FakeCoordinator()
            .Then(LeaseJson(LooseJaml, 1, 0, 2, "first"))
            .Then(LeaseJson(LooseJaml, 1, 2, 2, "second"));
        var options = new PartyWorkerOptions { Threads = 2, Once = true };

        var (summary, _) = await RunAsync(fake, options);

        Assert.Equal(PartyWorkerOutcome.Once, summary.Outcome);
        Assert.Equal(1, fake.NextCalls);
        Assert.Equal("first", Assert.Single(fake.Reports).WorkerToken);
    }

    [Fact]
    public async Task Interrupted_SendsNoReportForTheHalfDoneLease()
    {
        // 64 blocks at batchChars 4 is 96 million seeds: far more than 300 ms of work.
        var fake = new FakeCoordinator().Then(LeaseJson(LooseJaml, 4, 0, 64, "tok"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var (summary, log) = await RunAsync(fake, FastOptions(threads: 2), cts.Token);

        Assert.Equal(PartyWorkerOutcome.Interrupted, summary.Outcome);
        Assert.Equal(130, PartyWorker.ExitCode(summary));
        Assert.Empty(fake.Reports);
        Assert.Equal(1, fake.NextCalls);
        Assert.Contains("no report sent", log);
    }

    [Fact]
    public async Task RefusedReport_IsLoggedAndTheWorkerGoesOn()
    {
        var fake = new FakeCoordinator()
            .Then(LeaseJson(LooseJaml, 1, 0, 2, "stale"))
            .Then(LeaseJson(LooseJaml, 1, 2, 2, "fresh"));
        fake.ReportAnswers.Enqueue((HttpStatusCode.BadRequest, """{"error":"no matching lease for this worker token and block"}"""));

        var (summary, log) = await RunAsync(fake);

        Assert.Equal(PartyWorkerOutcome.Stopped, summary.Outcome);
        Assert.Equal(2, fake.Reports.Count);
        Assert.Contains("report refused: HTTP 400: no matching lease", log);
    }

    [Fact]
    public async Task ServerErrorOnLease_IsRetried()
    {
        var fake = new FakeCoordinator()
            .Then("""{"error":"database is waking up"}""", HttpStatusCode.InternalServerError)
            .Then("<html>bad gateway</html>", HttpStatusCode.BadGateway)
            .Then(LeaseJson(LooseJaml, 1, 0, 2, "tok"));

        var (summary, log) = await RunAsync(fake);

        Assert.Equal(PartyWorkerOutcome.Stopped, summary.Outcome);
        Assert.Equal(4, fake.NextCalls);
        Assert.Single(fake.Reports);
        Assert.Contains("HTTP 500: database is waking up", log);
        Assert.Contains("HTTP 502: <html>bad gateway</html>", log);
    }

    [Fact]
    public async Task ServerThatKeepsFailing_GivesUp()
    {
        var fake = new FakeCoordinator { Fallback = "not json" };
        var options = new PartyWorkerOptions { RetryDelay = TimeSpan.FromMilliseconds(1), MaxConsecutiveErrors = 3 };

        var (summary, _) = await RunAsync(fake, options);

        Assert.Equal(PartyWorkerOutcome.Failed, summary.Outcome);
        Assert.Equal(1, PartyWorker.ExitCode(summary));
        Assert.Equal(3, fake.NextCalls);
    }

    [Fact]
    public async Task LeaseWithBrokenJaml_FailsWithoutReporting()
    {
        var fake = new FakeCoordinator().Then(LeaseJson("must:\n  - joker: NotAJoker\n", 1, 0, 1, "tok"));

        var (summary, _) = await RunAsync(fake);

        Assert.Equal(PartyWorkerOutcome.Failed, summary.Outcome);
        Assert.Contains("JAML", summary.Reason);
        Assert.Empty(fake.Reports);
    }

    [Fact]
    public void ClientUrls_KeepTheServersPath()
    {
        using var http = new HttpClient();
        Assert.Equal(
            "https://www.seedfinder.app/api/party/next?partyId=a%20b",
            new PartyClient(http, new Uri("https://www.seedfinder.app")).NextUri("a b").AbsoluteUri
        );
        Assert.Equal(
            "https://host/sub/api/party/report",
            new PartyClient(http, new Uri("https://host/sub/")).ReportUri.AbsoluteUri
        );
    }
}
