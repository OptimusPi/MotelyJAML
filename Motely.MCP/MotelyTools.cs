using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using Motely.Analysis;

namespace Motely.MCP;

/// <summary>
/// The four tools and the MCP App view, registered through the SDK's raw handlers: no
/// reflection-built tool schemas, so the NativeAOT publish stays warning-free.
/// </summary>
public sealed class MotelyTools(Searches searches, bool allowPaths)
{
    public ListToolsResult ListTools() =>
        new()
        {
            Tools =
            [
                new()
                {
                    Name = "motely_search",
                    Title = "Start a Balatro seed search",
                    Description =
                        "Start a Motely search for a JAML filter and return its id at once; the search keeps running. "
                        + "Poll motely_status for progress and the best seeds, motely_stop to end it. "
                        + "Modes: sequential (default; the whole keyspace, runs until stopped or endBatch), "
                        + "random (count seeds), keyword (seeds containing the keywords), seeds (exactly these).",
                    InputSchema = Schema(
                        $$"""
                        {
                          "type": "object",
                          "properties": {
                            "jaml": { "type": "string", "description": "The filter as YAML/JAML text (deck, stake, must, should, mustNot)." },
                            {{(allowPaths ? "\"path\": { \"type\": \"string\", \"description\": \"Path to a .jaml/.yaml filter file instead of jaml text.\" }," : "")}}
                            "mode": { "type": "string", "enum": ["sequential", "random", "keyword", "seeds"], "default": "sequential" },
                            "count": { "type": "integer", "minimum": 1, "description": "random: how many seeds (default 1000000)." },
                            "keywords": { "type": "array", "items": { "type": "string" }, "description": "keyword: words the seed must contain, e.g. [\"PIFREAK\"]." },
                            "seeds": { "type": "array", "items": { "type": "string" }, "description": "seeds: the exact seeds to test." },
                            "deck": { "type": "string", "description": "Override the filter's deck, e.g. Ghost." },
                            "stake": { "type": "string", "description": "Override the filter's stake, e.g. Gold." },
                            "cutoff": { "type": "string", "description": "'auto' (default: keep seeds that tie or beat the best so far), 'off', or a minimum score." },
                            "stopAfter": { "type": "integer", "minimum": 1, "description": "Stop after this many matches." },
                            "threads": { "type": "integer", "minimum": 1, "description": "Worker threads (default: every core)." },
                            "startBatch": { "type": "integer", "minimum": 0, "description": "sequential: first batch (resume point)." },
                            "endBatch": { "type": "integer", "minimum": 1, "description": "sequential: end batch, exclusive." }
                          }
                        }
                        """
                    ),
                    Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = false, OpenWorldHint = false },
                },
                new()
                {
                    Name = "motely_status",
                    Title = "Search progress and best seeds",
                    Description = "Progress, speed and the best seeds of a search from motely_search. Omit id to list every search.",
                    InputSchema = Schema(
                        """
                        {
                          "type": "object",
                          "properties": {
                            "id": { "type": "string", "description": "Search id, e.g. s1." },
                            "top": { "type": "integer", "minimum": 1, "maximum": 100, "default": 20, "description": "How many of the best seeds to return." }
                          }
                        }
                        """
                    ),
                    Annotations = new() { ReadOnlyHint = true, OpenWorldHint = false },
                },
                new()
                {
                    Name = "motely_stop",
                    Title = "Stop a search",
                    Description = "Stop a running search. Its best seeds so far stay readable through motely_status.",
                    InputSchema = Schema(
                        """
                        {
                          "type": "object",
                          "properties": { "id": { "type": "string", "description": "Search id, e.g. s1." } },
                          "required": ["id"]
                        }
                        """
                    ),
                    Annotations = new() { ReadOnlyHint = false, DestructiveHint = false, IdempotentHint = true, OpenWorldHint = false },
                },
                new()
                {
                    Name = "motely_analyze",
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
                          "required": ["seeds"]
                        }
                        """
                    ),
                    Annotations = new() { ReadOnlyHint = true, IdempotentHint = true, OpenWorldHint = false },
                },
            ],
        };

    public CallToolResult Call(CallToolRequestParams request)
    {
        var args = new Args(request.Arguments);
        try
        {
            return request.Name switch
            {
                "motely_search" => Search(args),
                "motely_status" => Status(args),
                "motely_stop" => Stop(args),
                "motely_analyze" => Analyze(args),
                _ => Error($"Unknown tool '{request.Name}'."),
            };
        }
        catch (ToolException ex)
        {
            return Error(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // YAML and plan errors carry line numbers; hand them back verbatim.
            return Error(ex.Message);
        }
    }

    private CallToolResult Search(Args args)
    {
        string jaml = args.String("jaml") ?? ReadPath(args.String("path"));
        var run = searches.Start(
            new SearchRequest(
                Jaml: jaml,
                Mode: args.String("mode") ?? "sequential",
                Deck: args.Enum<MotelyDeck>("deck"),
                Stake: args.Enum<MotelyStake>("stake"),
                Threads: args.Int("threads") ?? Environment.ProcessorCount,
                Cutoff: args.String("cutoff"),
                StopAfter: args.Long("stopAfter"),
                Count: args.Int("count"),
                Keywords: args.Strings("keywords"),
                Seeds: args.Strings("seeds"),
                StartBatch: args.Long("startBatch"),
                EndBatch: args.Long("endBatch")
            )
        );
        return Result(
            $"Started {run.Id}: {run.Name} | {run.Deck} {run.Stake} | {run.Mode}. Poll motely_status with id {run.Id}.",
            Snapshot(run, 20)
        );
    }

    private string ReadPath(string? path)
    {
        if (path is null)
            throw new ToolException("Give the filter as jaml text" + (allowPaths ? " or a path." : "."));
        if (!allowPaths)
            throw new ToolException("path is off over HTTP; send the filter as jaml text.");
        var full = Path.GetFullPath(path);
        return File.Exists(full) ? File.ReadAllText(full) : throw new ToolException($"No filter file at {full}.");
    }

    private CallToolResult Status(Args args)
    {
        int top = Math.Clamp(args.Int("top") ?? 20, 1, 100);
        if (args.String("id") is not { } id)
        {
            var all = searches.All.Select(r => Snapshot(r, 0)).ToArray();
            var text = all.Length == 0
                ? "No searches yet. Start one with motely_search."
                : string.Join('\n', all.Select(static s => $"{s.Id} {s.State} | {s.Name} | {s.SeedsSearched:N0} searched, {s.Matching:N0} matched"));
            return new() { Content = [new TextContentBlock { Text = text }] };
        }

        var snapshot = Snapshot(searches.Get(id), top);
        var lines = new StringBuilder();
        lines.AppendLine(
            $"{snapshot.Id} {snapshot.State} | {snapshot.Name} | {snapshot.Deck} {snapshot.Stake} | "
                + $"{snapshot.Percent:F2}% | {snapshot.SeedsSearched:N0} searched | {snapshot.Matching:N0} matched | "
                + $"{snapshot.SeedsPerSecond:N0} seeds/s"
        );
        if (snapshot.TallyLabels.Count > 0)
            lines.AppendLine($"seed,score,{string.Join(',', snapshot.TallyLabels)}");
        foreach (var find in snapshot.Top)
            lines.AppendLine($"{find.Seed},{find.Score}{string.Concat(find.Tallies.Select(static t => $",{t}"))}");
        if (snapshot.ResumeBatch is { } resume)
            lines.AppendLine($"Resume with startBatch {resume}.");
        return Result(lines.ToString().TrimEnd(), snapshot);
    }

    private CallToolResult Stop(Args args)
    {
        var run = searches.Get(args.String("id") ?? throw new ToolException("id is required."));
        if (run.State == "running")
        {
            run.Stopped = true;
            run.Cancel.Cancel();
        }
        var snapshot = Snapshot(run, 20);
        return Result($"{run.Id} {snapshot.State} with {snapshot.Matching:N0} matches.", snapshot);
    }

    private static CallToolResult Analyze(Args args)
    {
        var seeds = Seeds.Normalize(args.Strings("seeds"), "seeds");
        var deck = args.Enum<MotelyDeck>("deck") ?? MotelyDeck.Red;
        var stake = args.Enum<MotelyStake>("stake") ?? MotelyStake.White;
        var text = new StringBuilder();
        foreach (var seed in seeds)
        {
            var analysis = MotelyUnitTestAnalyzer.Analyze(new(seed, deck, stake));
            text.AppendLine($"=== {seed} | {deck} {stake} ===");
            text.AppendLine(string.IsNullOrEmpty(analysis.Error) ? analysis.ToString() : $"Error: {analysis.Error}");
        }
        return new() { Content = [new TextContentBlock { Text = text.ToString().TrimEnd() }] };
    }

    private static SearchSnapshot Snapshot(SearchRun run, int top)
    {
        var search = run.Search;
        var progress = run.Progress;
        long? resume = search is { IsSequentialBatchSearch: true, ResumeBatchIndex: >= 0 } && run.State != "finished"
            ? search.ResumeBatchIndex
            : null;
        return new(
            Id: run.Id,
            State: run.State,
            Name: run.Name,
            Deck: run.Deck.ToString(),
            Stake: run.Stake.ToString(),
            Mode: run.Mode,
            // Progress ticks once a second, so the last tick usually lands short of the finish line.
            Percent: run.State == "finished" ? 100 : progress?.PercentComplete ?? 0,
            SeedsSearched: search?.TotalSeedsSearched ?? 0,
            Matching: search?.MatchingSeeds ?? 0,
            SeedsPerSecond: search?.SeedsPerSecond ?? 0,
            ElapsedMs: search?.ElapsedMs ?? 0,
            ResumeBatch: resume,
            TallyLabels: run.TallyLabels,
            Top: [.. run.Top(top).Select(static f => new SeedRow(f.Seed, f.Score, f.Tallies))]
        );
    }

    private static CallToolResult Result(string text, SearchSnapshot snapshot) =>
        new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(snapshot, McpJson.Default.SearchSnapshot),
        };

    private static CallToolResult Error(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Typed reads over the raw argument dictionary, each failing with a usable message.</summary>
    private readonly struct Args(IDictionary<string, JsonElement>? values)
    {
        private JsonElement? Get(string name) =>
            values is not null && values.TryGetValue(name, out var v) && v.ValueKind is not JsonValueKind.Null ? v : null;

        public string? String(string name) =>
            Get(name) is { } v
                ? v.ValueKind == JsonValueKind.String ? v.GetString() : throw new ToolException($"{name} must be a string.")
                : null;

        public long? Long(string name) =>
            Get(name) is { } v
                ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : throw new ToolException($"{name} must be a whole number.")
                : null;

        public int? Int(string name) =>
            Long(name) is { } n ? n is >= int.MinValue and <= int.MaxValue ? (int)n : throw new ToolException($"{name} is out of range.") : null;

        public string[]? Strings(string name) =>
            Get(name) is { } v
                ? v.ValueKind == JsonValueKind.Array
                    ? [.. v.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ToolException($"{name} must hold strings."))]
                    : throw new ToolException($"{name} must be an array of strings.")
                : null;

        public T? Enum<T>(string name)
            where T : struct, Enum =>
            String(name) is { } text
                ? System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
                    ? value
                    : throw new ToolException($"{name} '{text}' is not one of: {string.Join(", ", System.Enum.GetNames<T>())}.")
                : null;
    }
}

public sealed record SeedRow(string Seed, int Score, int[] Tallies);

public sealed record SearchSnapshot(
    string Id,
    string State,
    string Name,
    string Deck,
    string Stake,
    string Mode,
    double Percent,
    long SeedsSearched,
    long Matching,
    double SeedsPerSecond,
    long ElapsedMs,
    long? ResumeBatch,
    IReadOnlyList<string> TallyLabels,
    SeedRow[] Top
);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(SearchSnapshot))]
internal sealed partial class McpJson : JsonSerializerContext;
