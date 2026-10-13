using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters;
using Motely.Shared;
using Npgsql;

namespace Motely.MCP;

/// <summary>
/// The engine as an MCP server for a coding agent on this machine, on stdin/stdout or, with a port,
/// over Streamable HTTP (<see cref="McpHttp"/>). Tools are declared by hand (schemas as JSON text,
/// raw list/call handlers): nothing is built by reflection. On stdio, stdout is the protocol
/// channel, so the process's own Console.Out points at stderr for the whole session; the engine's
/// stray prints land there instead of corrupting the stream. With a database (<see cref="MotelyDb"/>)
/// every search's best seeds are kept and can be read back.
/// </summary>
internal static class MotelyMcp
{
    private const string Instructions =
        "Motely searches Balatro seeds with JAML filters: YAML with name, deck, stake and must/should/mustNot clause lists. "
        + "Call validate_filter after every edit to a filter, list_names for the exact names the engine accepts, "
        + "search_seeds to run a bounded search and get the best seeds back, analyze_seeds to see seeds ante by ante. "
        + "With the seed database on, every search_seeds result is saved: list_filters shows what has been run and "
        + "saved_seeds returns a filter's best seeds so far.";

    private const int DefaultMaxSeconds = 60;
    private const int DefaultTop = 20;

    /// <summary>The seed database, or null when none is configured.</summary>
    private static MotelyDb? Db;

    /// <param name="port">Serve Streamable HTTP on this loopback port; null for stdin/stdout.</param>
    public static async Task<int> RunAsync(int? port, CancellationToken cancellationToken)
    {
        // Traces and metrics to the Aspire dashboard when it set an OTLP endpoint; nothing otherwise.
        using var telemetry = ServiceDefaults.Extensions.AddServiceDefaults(SearchTelemetry.Sources, SearchTelemetry.Sources);

        Db = MotelyDb.FromEnvironment();
        if (Db is { } db)
        {
            try
            {
                int applied = await db.MigrateAsync(cancellationToken);
                Console.Error.WriteLine($"motely mcp: database ready ({applied} migration(s) applied)");
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException or ArgumentException)
            {
                // Loud on purpose: a wrong Neon string should fail the resource, not run a server that silently keeps nothing.
                Console.Error.WriteLine($"motely mcp: the seed database is unreachable: {ex.Message}");
                return 1;
            }
        }
        else
            Console.Error.WriteLine("motely mcp: no ConnectionStrings__db; finds are not saved");

        await using var _ = Db;
        if (port is { } httpPort)
        {
            await McpHttp.RunAsync(httpPort, CreateOptions, cancellationToken);
            return 0;
        }

        // The transport writes to the raw stdout handle, not Console.Out.
        Console.SetOut(Console.Error);
        var options = CreateOptions();
        await using var transport = new StdioServerTransport(options);
        await using var server = McpServer.Create(transport, options);
        try
        {
            await server.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return 0;
    }

    private static McpServerOptions CreateOptions() =>
        new()
        {
            ServerInfo = new Implementation
            {
                Name = "motely",
                Title = "Motely seed search",
                Version = typeof(MotelyMcp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            },
            ServerInstructions = Instructions,
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [.. Tools] }),
                CallToolHandler = CallAsync,
            },
        };

    // Before Tools: list_names' schema enumerates these keys, and static initializers run in order.
    private static readonly Dictionary<string, Func<IReadOnlyCollection<string>>> Vocabulary = new()
    {
        ["clauses"] = static () => YamlConfigLoader.ClauseKeys,
        ["jokers"] = Enum.GetNames<MotelyJoker>,
        ["commonJokers"] = Enum.GetNames<MotelyJokerCommon>,
        ["uncommonJokers"] = Enum.GetNames<MotelyJokerUncommon>,
        ["rareJokers"] = Enum.GetNames<MotelyJokerRare>,
        ["legendaryJokers"] = Enum.GetNames<MotelyJokerLegendary>,
        ["vouchers"] = Enum.GetNames<MotelyVoucher>,
        ["tarotCards"] = Enum.GetNames<MotelyTarotCard>,
        ["spectralCards"] = Enum.GetNames<MotelySpectralCard>,
        ["planetCards"] = Enum.GetNames<MotelyPlanetCard>,
        ["tags"] = Enum.GetNames<MotelyTag>,
        ["bosses"] = Enum.GetNames<MotelyBossBlind>,
        ["decks"] = Enum.GetNames<MotelyDeck>,
        ["stakes"] = Enum.GetNames<MotelyStake>,
        ["editions"] = Enum.GetNames<MotelyItemEdition>,
        ["enhancements"] = Enum.GetNames<MotelyItemEnhancement>,
        ["seals"] = Enum.GetNames<MotelyItemSeal>,
        ["stickers"] = Enum.GetNames<MotelyJokerSticker>,
        ["ranks"] = Enum.GetNames<MotelyStandardcardRank>,
        ["suits"] = Enum.GetNames<MotelyStandardcardSuit>,
    };

    private static readonly Tool[] Tools =
    [
        new()
        {
            Name = "validate_filter",
            Title = "Validate a JAML filter",
            Description =
                "Load a filter the way the engine does and build its search plan. Returns the name, deck, stake and clause counts, "
                + "or the error with its line number. Same check as `motely check` and the editor hook.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "jaml": { "type": "string", "description": "The filter as YAML text." },
                    "path": { "type": "string", "description": "A filter file, or a bare name for YamlFilters/<name>.yaml." }
                  },
                  "additionalProperties": false
                }
                """
            ),
            Annotations = new() { ReadOnlyHint = true, IdempotentHint = true, OpenWorldHint = false },
        },
        new()
        {
            Name = "search_seeds",
            Title = "Search Balatro seeds",
            Description =
                "Run a filter over seeds and return the best ones as CSV (seed, score, one column per should clause). "
                + "Pick at most one input: seeds, replay, keywords, random or aesthetic; with none, it sweeps the whole "
                + "keyspace in order (startBatch/endBatch or startSeed/stopSeed narrow it). Stops at stopAfter matches, "
                + "after maxSeconds, or when the input runs out; a sequential sweep that stops early says where to resume. "
                + "With the seed database on, the returned seeds are saved under the filter's slug.",
            InputSchema = Schema(
                $$"""
                {
                  "type": "object",
                  "properties": {
                    "jaml": { "type": "string", "description": "The filter as YAML text." },
                    "path": { "type": "string", "description": "A filter file, or a bare name for YamlFilters/<name>.yaml." },
                    "seeds": { "type": "array", "items": { "type": "string" }, "description": "Search exactly these seeds." },
                    "replay": { "type": "boolean", "description": "Search the filter's own seeds: block." },
                    "keywords": { "type": "array", "items": { "type": "string" }, "description": "Search seeds containing any of these words." },
                    "padding": { "type": "string", "description": "Characters that fill the free slots of keyword and aesthetic seeds (default: 1-9 and A-Z)." },
                    "random": { "type": "integer", "minimum": 1, "description": "Search this many random seeds." },
                    "aesthetic": { "type": "string", "description": "One aesthetic seed family ({{MotelyAestheticParser.KnownJamlStringsDescription()}}); all runs every family in turn." },
                    "startBatch": { "type": "integer", "minimum": 0, "description": "Sequential: first batch." },
                    "endBatch": { "type": "integer", "minimum": 1, "description": "Sequential: end batch, exclusive." },
                    "startSeed": { "type": "string", "description": "Sequential: start at the batch holding this seed." },
                    "stopSeed": { "type": "string", "description": "Sequential: end after the batch holding this seed." },
                    "deck": { "type": "string", "description": "Replaces the filter's deck, e.g. Ghost." },
                    "stake": { "type": "string", "description": "Replaces the filter's stake, e.g. Gold." },
                    "cutoff": { "type": "string", "description": "A minimum score, 'auto' (keep seeds that tie or beat the best so far once finds flood in) or 'off'. Default: the filter's own cutoff:, else auto." },
                    "stopAfter": { "type": "integer", "minimum": 1, "description": "Stop after this many matches." },
                    "maxSeconds": { "type": "integer", "minimum": 1, "maximum": 3600, "default": {{DefaultMaxSeconds}}, "description": "Stop after this long and return what was found." },
                    "top": { "type": "integer", "minimum": 1, "maximum": 200, "default": {{DefaultTop}}, "description": "How many of the best seeds to return." },
                    "threads": { "type": "integer", "minimum": 1, "description": "Worker threads (default: every core)." }
                  },
                  "additionalProperties": false
                }
                """
            ),
            Annotations = new() { ReadOnlyHint = false, OpenWorldHint = false },
        },
        new()
        {
            Name = "saved_seeds",
            Title = "A filter's saved seeds",
            Description =
                "The best seeds the database holds for one filter, as CSV (seed, score, tallies as JSON, when it was found). "
                + "Filters are named by slug; list_filters shows them.",
            InputSchema = Schema(
                $$"""
                {
                  "type": "object",
                  "properties": {
                    "filter": { "type": "string", "description": "The filter's slug, as list_filters shows it." },
                    "top": { "type": "integer", "minimum": 1, "maximum": 200, "default": {{DefaultTop}}, "description": "How many seeds to return." }
                  },
                  "required": ["filter"],
                  "additionalProperties": false
                }
                """
            ),
            Annotations = new() { ReadOnlyHint = true, IdempotentHint = true, OpenWorldHint = false },
        },
        new()
        {
            Name = "list_filters",
            Title = "Filters with saved seeds",
            Description = "Every filter the database holds: slug, name, how many seeds, when it last ran.",
            InputSchema = Schema(
                """
                { "type": "object", "properties": {}, "additionalProperties": false }
                """
            ),
            Annotations = new() { ReadOnlyHint = true, IdempotentHint = true, OpenWorldHint = false },
        },
        new()
        {
            Name = "analyze_seeds",
            Title = "Analyze seeds ante by ante",
            Description = "For each seed: every ante's boss, voucher, tags, shop and packs.",
            InputSchema = Schema(
                """
                {
                  "type": "object",
                  "properties": {
                    "seeds": { "type": "array", "items": { "type": "string" }, "minItems": 1, "maxItems": 20 },
                    "deck": { "type": "string", "default": "Red" },
                    "stake": { "type": "string", "default": "White" }
                  },
                  "required": ["seeds"],
                  "additionalProperties": false
                }
                """
            ),
            Annotations = new() { ReadOnlyHint = true, IdempotentHint = true, OpenWorldHint = false },
        },
        new()
        {
            Name = "list_names",
            Title = "List the names JAML accepts",
            Description =
                "The engine's own spelling for one kind of name: clause keys, jokers, vouchers, tarot/spectral/planet cards, "
                + "tags, bosses, decks, stakes, editions, enhancements, seals, stickers, ranks or suits.",
            InputSchema = Schema(
                $$"""
                {
                  "type": "object",
                  "properties": {
                    "kind": { "type": "string", "enum": [{{string.Join(", ", Vocabulary.Keys.Select(static k => $"\"{k}\""))}}] }
                  },
                  "required": ["kind"],
                  "additionalProperties": false
                }
                """
            ),
            Annotations = new() { ReadOnlyHint = true, IdempotentHint = true, OpenWorldHint = false },
        },
    ];

    private static async ValueTask<CallToolResult> CallAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken
    )
    {
        var call = request.Params ?? throw new McpProtocolException("tools/call needs params.", McpErrorCode.InvalidParams);
        var tool = Array.Find(Tools, t => t.Name == call.Name)
            ?? throw new McpProtocolException($"Unknown tool '{call.Name}'.", McpErrorCode.InvalidParams);
        try
        {
            var args = new Args(call.Arguments, tool.InputSchema);
            return tool.Name switch
            {
                "validate_filter" => Validate(args),
                "search_seeds" => await SearchAsync(args, request, cancellationToken),
                "saved_seeds" => await SavedSeedsAsync(args, cancellationToken),
                "list_filters" => await ListFiltersAsync(cancellationToken),
                "analyze_seeds" => Analyze(args),
                _ => ListNames(args),
            };
        }
        catch (ToolError ex)
        {
            return Error(ex.Message);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return Error($"Database: {ex.Message}");
        }
    }

    private static CallToolResult Validate(Args args)
    {
        var (text, source) = args.Filter();
        if (!FilterCheck.TryCheck(text, out var config, out var plan, out var error))
            return Error($"{source}: {error}");

        var summary = new StringBuilder(
            $"{source}: ok | {config.Name ?? "unnamed"} | {config.Deck} {config.Stake} | "
                + $"must {config.Must.Count}, should {config.Should.Count}, mustNot {config.MustNot.Count}"
        );
        if (config.Cutoff is { } cutoff)
            summary.Append($" | cutoff {cutoff}");
        if (plan.TallyLabels.Count > 0)
            summary.Append($"\ncolumns: seed,score,{string.Join(',', plan.TallyLabels)}");
        return Text(summary.ToString());
    }

    private static async Task<CallToolResult> SearchAsync(
        Args args,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken
    )
    {
        var (text, source) = args.Filter();
        if (!YamlConfigLoader.TryLoad(text, out var config, out var loadError))
            return Error($"{source}: {loadError}");

        // Same precedence as the CLI: the cutoff argument, else the filter's cutoff:, else auto.
        if (!MotelyScoreCutoff.TryResolve(args.String("cutoff"), config.Cutoff, out var cutoff, out var cutoffText, out var cutoffError))
            return Error($"cutoff: {cutoffError}");

        JamlSearchPlan plan;
        try
        {
            plan = MotelySearchBuilder.CreatePlan(config, cutoff.EngineCutoff);
        }
        catch (InvalidOperationException ex)
        {
            return Error($"{source}: {ex.Message}");
        }

        var deck = args.Enum<MotelyDeck>("deck") ?? config.Deck;
        var stake = args.Enum<MotelyStake>("stake") ?? config.Stake;
        int threads = args.Int("threads") ?? Environment.ProcessorCount;
        int top = args.Int("top", max: 200) ?? DefaultTop;
        int maxSeconds = args.Int("maxSeconds", max: 3600) ?? DefaultMaxSeconds;
        long? stopAfter = args.Int("stopAfter");
        string[] keywords = args.Strings("keywords") ?? [];
        string[]? seeds = args.Strings("seeds");
        int? random = args.Int("random");
        string? aesthetic = args.String("aesthetic");
        string? startSeed = args.String("startSeed");
        string? stopSeed = args.String("stopSeed");
        bool replay = args.Bool("replay") ?? false;

        // The CLI's own mode rules and messages, so both front doors accept and refuse the same things.
        var notes = new List<string>();
        if (
            !CliSearchMode.TryApplySearchMode(
                plan.Settings.WithDeck(deck).WithStake(stake).WithThreadCount(threads),
                new CliSearchMode.Input(
                    SeedsArgument: seeds is null ? null : string.Join(',', seeds),
                    Replay: replay,
                    JamlPath: source,
                    FilterId: config.Id,
                    JamlSeeds: config.Seeds,
                    KeywordInputs: keywords,
                    PaddingCharsOption: args.String("padding") ?? new string(MotelyGlobals.SeedDigits),
                    RandomCount: random,
                    AestheticName: aesthetic,
                    StartBatch: args.Long("startBatch"),
                    EndBatch: args.Long("endBatch"),
                    StartPercent: null,
                    StartSeed: startSeed is null ? null : MotelyGlobals.NormalizeSeed(startSeed),
                    StopSeed: stopSeed is null ? null : MotelyGlobals.NormalizeSeed(stopSeed),
                    BatchCharacterCount: null
                ),
                notes.Add,
                out var modeError,
                out var settings
            )
        )
            return Error(modeError.StartsWith("Error: ", StringComparison.Ordinal) ? modeError["Error: ".Length..] : modeError);

        var run = new SearchRun(top);
        using var telemetry = SearchTelemetry.Start(
            config.Name ?? Path.GetFileNameWithoutExtension(source),
            deck,
            stake,
            replay ? "replay"
            : seeds is not null ? "seeds"
            : keywords.Length > 0 ? "keywords"
            : random is not null ? "random"
            : aesthetic is not null ? "aesthetic"
            : "sequential"
        );
        // A filter with no clauses has no score provider; then finds arrive only on the seed-match
        // channel. With one they arrive on both, so listen to exactly one (as the CLI does).
        settings = settings.SeedScoreDesc is not null
            ? settings
                .WithAutoScoreCutoff(cutoff.IsAuto)
                .WithScoredResultCallback(result => run.Add(result.Seed, result.Score, result.TallyValuesSpan.ToArray()))
            : settings.WithSeedMatchCallback(line =>
            {
                int comma = line.IndexOf(',');
                run.Add(comma < 0 ? line : line[..comma], 0, []);
            });
        settings = settings
            .WithProgressReportIntervalMs(1000)
            .WithProgressCallback(progress =>
            {
                run.Report(progress);
                telemetry.Report(progress);
            });
        if (stopAfter is { } limit)
            settings = settings.StopAfter(limit);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(maxSeconds));
        using var search = settings.Start(deadline.Token);

        using var reporting = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var reporter = request.Params?.ProgressToken is { } progressToken
            ? ReportProgressAsync(request.Server, progressToken, run, reporting.Token)
            : Task.CompletedTask;

        bool timedOut = false;
        try
        {
            await search.WaitForCompletionAsync(deadline.Token);
        }
        // The client's own cancel propagates (the SDK sends no reply); the deadline is a normal stop.
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
        }
        finally
        {
            reporting.Cancel();
            await reporter;
        }
        telemetry.Finish(search, timedOut || search.StoppedOnMatchLimit ? "stopped" : "finished");

        string input =
            replay ? $"replay {config.Seeds?.Count ?? 0:N0} saved seeds"
            : seeds is not null ? $"{seeds.Length:N0} listed seeds"
            : keywords.Length > 0 ? $"keywords {string.Join(",", keywords)}"
            : random is { } count ? $"{count:N0} random seeds"
            : aesthetic is not null ? $"aesthetic {aesthetic.Trim().ToLowerInvariant()}"
            : "sequential";
        string ending =
            timedOut ? $"Stopped at maxSeconds {maxSeconds}"
            : search.StoppedOnMatchLimit ? $"Stopped at stopAfter {stopAfter:N0}"
            : "Finished";

        var output = new StringBuilder();
        output.AppendLine($"{config.Name ?? source} | {deck} {stake} | {input} | cutoff {cutoffText}");
        foreach (var note in notes)
            output.AppendLine(note);
        output.AppendLine(
            $"{ending}: {search.TotalSeedsSearched:N0} searched, {search.MatchingSeeds:N0} matched, "
                + $"{search.SeedsPerSecond:N0} seeds/s, {TimeSpan.FromMilliseconds(search.ElapsedMs):hh\\:mm\\:ss\\.f}"
        );

        var best = run.Best();
        if (best.Length == 0)
            output.AppendLine("No seeds matched.");
        else
        {
            output.AppendLine(
                plan.TallyLabels.Count > 0 ? $"seed,score,{string.Join(',', plan.TallyLabels)}" : "seed,score"
            );
            foreach (var find in best)
                output.AppendLine($"{find.Seed},{find.Score}{string.Concat(find.Tallies.Select(static t => $",{t}"))}");
        }

        if (search.IsSequentialBatchSearch && search.ResumeBatchIndex is >= 0 and var resume && resume < search.TotalBatchCount)
            output.AppendLine($"Resume with startBatch {resume}.");

        // The seeds go to the database after the search, never on its hot path. A save that fails
        // is reported with the results, not instead of them.
        if (Db is { } db && best.Length > 0)
        {
            var slug = MotelyDb.Slug(config, source);
            try
            {
                await db.SaveFindsAsync(
                    slug,
                    config.Name,
                    text,
                    plan.TallyLabels,
                    best.Select(static f => (f.Seed, f.Score, f.Tallies)),
                    cancellationToken
                );
                output.AppendLine($"Saved {best.Length:N0} seed(s) to the database under filter '{slug}'.");
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
            {
                output.AppendLine($"Database save failed: {ex.Message}");
            }
        }
        return Text(output.ToString().TrimEnd());
    }

    /// <summary>
    /// Engine progress, once a second, for a client that sent a progress token. Sent only when it
    /// grows: the protocol requires each notification's progress to exceed the last.
    /// </summary>
    private static async Task ReportProgressAsync(McpServer server, ProgressToken token, SearchRun run, CancellationToken stop)
    {
        double sent = -1;
        try
        {
            while (true)
            {
                await Task.Delay(1000, stop);
                if (run.Progress is not { } progress || progress.PercentComplete <= sent)
                    continue;
                sent = progress.PercentComplete;
                await server.NotifyProgressAsync(
                    token,
                    new ProgressNotificationValue
                    {
                        Progress = (float)progress.PercentComplete,
                        Total = 100,
                        Message = $"{progress.SeedsSearched:N0} searched, {progress.MatchingSeeds:N0} matched",
                    },
                    cancellationToken: stop
                );
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private static async Task<CallToolResult> SavedSeedsAsync(Args args, CancellationToken cancellationToken)
    {
        if (Db is not { } db)
            return Error("No seed database is configured (ConnectionStrings__db).");
        var slug = args.String("filter") ?? throw new ToolError("filter is required.");
        int top = args.Int("top", max: 200) ?? DefaultTop;

        var rows = await db.BestSeedsAsync(slug, top, cancellationToken);
        if (rows.Count == 0)
            return Error($"No saved seeds for filter '{slug}'. list_filters shows the slugs the database holds.");
        var output = new StringBuilder("seed,score,tallies,found_at\n");
        foreach (var row in rows)
            output.AppendLine($"{row.Seed},{row.Score},{row.Tallies},{row.FoundAt:u}");
        return Text(output.ToString().TrimEnd());
    }

    private static async Task<CallToolResult> ListFiltersAsync(CancellationToken cancellationToken)
    {
        if (Db is not { } db)
            return Error("No seed database is configured (ConnectionStrings__db).");
        var rows = await db.FiltersAsync(cancellationToken);
        if (rows.Count == 0)
            return Text("The database holds no filters yet; search_seeds saves its finds.");
        var output = new StringBuilder("slug,name,seeds,updated_at\n");
        foreach (var row in rows)
            output.AppendLine($"{row.Slug},{row.Name ?? ""},{row.Seeds},{row.UpdatedAt:u}");
        return Text(output.ToString().TrimEnd());
    }

    private static CallToolResult Analyze(Args args)
    {
        var deck = args.Enum<MotelyDeck>("deck") ?? MotelyDeck.Red;
        var stake = args.Enum<MotelyStake>("stake") ?? MotelyStake.White;
        var output = new StringBuilder();
        foreach (var raw in args.Strings("seeds") ?? [])
        {
            if (!CliSearchMode.TryNormalizeSeed(raw, out var seed))
                throw new ToolError($"seeds: '{raw}' is not a seed ({CliSearchMode.SeedRule}).");
            var analysis = MotelyUnitTestAnalyzer.Analyze(new(seed, deck, stake));
            output.AppendLine($"=== {seed} | {deck} {stake} ===");
            output.AppendLine(string.IsNullOrEmpty(analysis.Error) ? analysis.ToString().TrimEnd() : $"Error: {analysis.Error}");
        }
        return output.Length == 0 ? Error("seeds: give at least one.") : Text(output.ToString().TrimEnd());
    }

    private static CallToolResult ListNames(Args args)
    {
        var kind = args.String("kind") ?? throw new ToolError("kind is required.");
        return Vocabulary.TryGetValue(kind, out var names)
            ? Text(string.Join(", ", names()))
            : Error($"kind '{kind}' is not one of: {string.Join(", ", Vocabulary.Keys)}.");
    }

    private static CallToolResult Text(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>A mistake in the call: reported to the model as an error result, not a protocol fault.</summary>
    private sealed class ToolError(string message) : Exception(message);

    /// <summary>Typed reads over the raw arguments, each failing with a message the model can act on.</summary>
    private readonly struct Args
    {
        private readonly IDictionary<string, JsonElement>? _values;

        public Args(IDictionary<string, JsonElement>? values, JsonElement schema)
        {
            _values = values;
            if (values is null)
                return;
            // The schemas say additionalProperties: false; a misspelled argument would otherwise
            // be dropped without a word and the search would run with the default instead.
            var known = schema.GetProperty("properties");
            foreach (var name in values.Keys)
            {
                if (!known.TryGetProperty(name, out _))
                    throw new ToolError(
                        $"Unknown argument '{name}'. Known: {string.Join(", ", known.EnumerateObject().Select(static p => p.Name))}."
                    );
            }
        }

        private JsonElement? Get(string name) =>
            _values is not null && _values.TryGetValue(name, out var v) && v.ValueKind is not JsonValueKind.Null ? v : null;

        /// <summary>The filter text from jaml or path, and a name for it in messages.</summary>
        public (string Text, string Source) Filter()
        {
            var jaml = String("jaml");
            var path = String("path");
            if (jaml is not null && path is not null)
                throw new ToolError("Give jaml or path, not both.");
            if (jaml is not null)
                return (jaml, "jaml");
            if (path is null)
                throw new ToolError("Give the filter as jaml text or a path.");
            var resolved = FilterCheck.ResolvePath(path);
            // Filters only: the loader's errors quote the text, so any other file stays unread.
            if (!resolved.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                && !resolved.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                throw new ToolError($"path must be a .yaml or .yml filter file, not {resolved}.");
            return File.Exists(resolved)
                ? (File.ReadAllText(resolved), resolved)
                : throw new ToolError($"No filter file at {Path.GetFullPath(resolved)}.");
        }

        public string? String(string name) =>
            Get(name) is { } v
                ? v.ValueKind == JsonValueKind.String ? v.GetString() : throw new ToolError($"{name} must be a string.")
                : null;

        public bool? Bool(string name) =>
            Get(name) is { } v
                ? v.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => throw new ToolError($"{name} must be true or false."),
                }
                : null;

        public long? Long(string name) =>
            Get(name) is { } v
                ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
                    ? n
                    : throw new ToolError($"{name} must be a whole number.")
                : null;

        public int? Int(string name, int max = int.MaxValue) =>
            Long(name) is { } n
                ? n >= 1 && n <= max ? (int)n : throw new ToolError($"{name} must be 1-{max}.")
                : null;

        public string[]? Strings(string name) =>
            Get(name) is { } v
                ? v.ValueKind == JsonValueKind.Array
                    ? [.. v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ToolError($"{name} must hold strings."))]
                    : throw new ToolError($"{name} must be an array of strings.")
                : null;

        public T? Enum<T>(string name)
            where T : struct, Enum =>
            String(name) is { } text
                ? System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
                    ? value
                    : throw new ToolError($"{name} '{text}' is not one of: {string.Join(", ", System.Enum.GetNames<T>())}.")
                : null;
    }

    /// <summary>The best seeds of one search and its latest progress. Finds land from every worker thread.</summary>
    private sealed class SearchRun(int keep)
    {
        private readonly List<SeedFind> _finds = [];
        private readonly Lock _gate = new();
        private MotelyProgress? _progress;

        public MotelyProgress? Progress => Volatile.Read(ref _progress);

        public void Report(MotelyProgress progress) => Volatile.Write(ref _progress, progress);

        public void Add(string seed, int score, int[] tallies)
        {
            lock (_gate)
            {
                _finds.Add(new(seed, score, tallies));
                // Trim only when the list doubles, so a flood of finds costs a sort per `keep` adds.
                if (_finds.Count < keep * 2)
                    return;
                Trim();
            }
        }

        public SeedFind[] Best()
        {
            lock (_gate)
            {
                Trim();
                return [.. _finds];
            }
        }

        // Stable: equal scores keep the order they were found in.
        private void Trim()
        {
            var kept = _finds.OrderByDescending(static f => f.Score).Take(keep).ToList();
            _finds.Clear();
            _finds.AddRange(kept);
        }
    }

    private sealed record SeedFind(string Seed, int Score, int[] Tallies);
}
