using System.Text;
using System.Text.Json.Nodes;
using Motely.Lsp;

namespace Motely.Tests;

/// <summary>
/// Drives the real <see cref="LspServer"/> through a complete framed session: the client's
/// messages are written into the input stream up front, the server runs to completion on the
/// exit notification, and every frame it produced is parsed back out of the output stream.
/// No threads, no timing — the protocol either round-trips or it fails loudly.
/// </summary>
public class LspServerProtocolTests
{
    private const string DocumentUri = "file:///fixture/filter.jaml";

    [Fact]
    public void Initialize_AnnouncesCapabilitiesAndCleanShutdown()
    {
        var (exitCode, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            Notification("initialized"),
            Request(2, "shutdown"),
            Notification("exit")
        );

        Assert.Equal(0, exitCode);
        var initialize = ResponseTo(messages, 1);
        var capabilities = initialize["result"]!["capabilities"]!;
        Assert.Equal(1, capabilities["textDocumentSync"]!.GetValue<int>());
        Assert.True(capabilities["hoverProvider"]!.GetValue<bool>());
        Assert.NotNull(capabilities["completionProvider"]);
        Assert.Equal("Motely.Lsp", initialize["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(ResponseTo(messages, 2));
    }

    [Fact]
    public void SemanticTokens_ClassifyFromTheEngineGrammarInPositionOrder()
    {
        const string doc = """
            name: probe
            deck: Red
            must:
              - legendaryJoker: Perkeo   # legendary
                antes: [1, 2]
            """;

        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            Notification("initialized"),
            DidOpen(doc),
            Request(2, "textDocument/semanticTokens/full", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = DocumentUri },
            }),
            Request(3, "shutdown"),
            Notification("exit")
        );

        var legend = ResponseTo(messages, 1)["result"]!["capabilities"]!["semanticTokensProvider"]!
            ["legend"]!["tokenTypes"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .ToArray();
        Assert.Equal(JamlLanguageService.SemanticTokenTypes, legend);

        var data = ResponseTo(messages, 2)["result"]!["data"]!.AsArray()
            .Select(n => n!.GetValue<int>())
            .ToArray();
        Assert.Equal(0, data.Length % 5);

        // Decode the relative encoding back to absolute positions. deltaStartChar is never
        // negative in a well-formed stream — it was, before the tokens were sorted, because a
        // line's trailing comment is discovered before the key and value that precede it.
        var decoded = new List<(int Line, int Start, int Length, string Type)>();
        int line = 0,
            start = 0;
        for (var i = 0; i < data.Length; i += 5)
        {
            Assert.True(data[i + 1] >= 0, $"negative deltaStartChar at token {i / 5}");
            line += data[i];
            start = data[i] == 0 ? start + data[i + 1] : data[i + 1];
            decoded.Add((line, start, data[i + 2], JamlLanguageService.SemanticTokenTypes[data[i + 3]]));
        }

        Assert.Equal(
            [
                (0, 0, 4, "keyword"), // name
                (1, 0, 4, "keyword"), // deck
                (1, 6, 3, "enumMember"), // Red
                (2, 0, 4, "keyword"), // must
                (3, 4, 14, "type"), // legendaryJoker — a discriminator, not a root key
                (3, 20, 6, "enumMember"), // Perkeo
                (3, 29, 11, "comment"), // # legendary
                (4, 4, 5, "property"), // antes — a clause key
                (4, 12, 1, "number"),
                (4, 15, 1, "number"),
            ],
            decoded
        );
    }

    [Fact]
    public void DidOpen_PublishesDiagnosticsForATypo()
    {
        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            DidOpen("name: oops\nboses:\n  - joker: Blueprint\n"),
            Request(2, "shutdown"),
            Notification("exit")
        );

        var published = PublishedDiagnostics(messages);
        var diagnostic = Assert.Single(published);
        Assert.Contains("boses", diagnostic["message"]!.GetValue<string>());
        Assert.Equal(1, diagnostic["range"]!["start"]!["line"]!.GetValue<int>());
        Assert.Equal("motely", diagnostic["source"]!.GetValue<string>());
    }

    [Fact]
    public void DidChange_ReplacesTheDocumentAndClearsDiagnostics()
    {
        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            DidOpen("boses: []\n"),
            Notification("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = DocumentUri },
                ["contentChanges"] = new JsonArray(new JsonObject
                {
                    ["text"] = "name: fixed\nmust:\n  - joker: Blueprint\n",
                }),
            }),
            Request(2, "shutdown"),
            Notification("exit")
        );

        var publishes = messages
            .Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
            .ToList();
        Assert.Equal(2, publishes.Count);
        Assert.NotEmpty((JsonArray)publishes[0]["params"]!["diagnostics"]!);
        Assert.Empty((JsonArray)publishes[1]["params"]!["diagnostics"]!);
    }

    [Fact]
    public void Hover_AnswersWithMarkdownAndRange()
    {
        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            DidOpen("must:\n  - joker: Blueprint\n"),
            Request(2, "textDocument/hover", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = DocumentUri },
                ["position"] = new JsonObject { ["line"] = 1, ["character"] = 5 },
            }),
            Request(3, "shutdown"),
            Notification("exit")
        );

        var hover = ResponseTo(messages, 2)["result"]!;
        Assert.Equal("markdown", hover["contents"]!["kind"]!.GetValue<string>());
        Assert.Contains("joker", hover["contents"]!["value"]!.GetValue<string>());
        Assert.Equal(1, hover["range"]!["start"]!["line"]!.GetValue<int>());
    }

    [Fact]
    public void Completion_ServesEngineVocabulary()
    {
        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            DidOpen("must:\n  - joker: Lu"),
            Request(2, "textDocument/completion", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = DocumentUri },
                ["position"] = new JsonObject { ["line"] = 1, ["character"] = 13 },
            }),
            Request(3, "shutdown"),
            Notification("exit")
        );

        var items = (JsonArray)ResponseTo(messages, 2)["result"]!;
        Assert.Contains(items, i => i!["label"]!.GetValue<string>() == "LuckyCat");
        // 20 = EnumMember: these are vocabulary values, not keys.
        Assert.All(items, i => Assert.Equal(20, i!["kind"]!.GetValue<int>()));
    }

    [Fact]
    public void UnknownRequest_GetsMethodNotFound()
    {
        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            Request(2, "workspace/executeCommand", new JsonObject()),
            Request(3, "shutdown"),
            Notification("exit")
        );

        var error = ResponseTo(messages, 2)["error"]!;
        Assert.Equal(-32601, error["code"]!.GetValue<int>());
    }

    [Fact]
    public void Diagnose_UnderlinesUnknownKeyAtItsSpan()
    {
        // Key also appears as a value earlier — span must land on the key line, not first occurrence.
        var text = "name: boses\nboses:\n";
        var diags = JamlLanguageService.Diagnose(text);
        var d = Assert.Single(diags);
        Assert.Contains("boses", d.Message);
        Assert.Equal(1, d.Span.StartLine);
    }

    [Fact]
    public void DidClose_ClearsPublishedDiagnostics()
    {
        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            DidOpen("boses: []\n"),
            Notification("textDocument/didClose", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = DocumentUri },
            }),
            Request(2, "shutdown"),
            Notification("exit")
        );

        var publishes = messages
            .Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
            .ToList();
        Assert.Equal(2, publishes.Count);
        Assert.NotEmpty((JsonArray)publishes[0]["params"]!["diagnostics"]!);
        Assert.Empty((JsonArray)publishes[1]["params"]!["diagnostics"]!);
        Assert.Equal(DocumentUri, publishes[1]["params"]!["uri"]!.GetValue<string>());
    }

    [Fact]
    public void ExitWithoutShutdown_ReturnsNonZero()
    {
        var (exitCode, _) = RunSession(
            Request(1, "initialize", new JsonObject()),
            Notification("exit")
        );
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Completion_IncludesTextEditForTypedPrefix()
    {
        var (_, messages) = RunSession(
            Request(1, "initialize", new JsonObject()),
            DidOpen("must:\n  - joker: Lu"),
            Request(2, "textDocument/completion", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = DocumentUri },
                ["position"] = new JsonObject { ["line"] = 1, ["character"] = 13 },
            }),
            Request(3, "shutdown"),
            Notification("exit")
        );

        var items = (JsonArray)ResponseTo(messages, 2)["result"]!;
        var lucky = Assert.Single(items, i => i!["label"]!.GetValue<string>() == "LuckyCat");
        var edit = lucky!["textEdit"]!;
        Assert.Equal("LuckyCat", edit["newText"]!.GetValue<string>());
        Assert.Equal(1, edit["range"]!["start"]!["line"]!.GetValue<int>());
        // "Lu" is the typed prefix — start column lands on L of Lu.
        Assert.Equal(11, edit["range"]!["start"]!["character"]!.GetValue<int>());
        Assert.Equal(13, edit["range"]!["end"]!["character"]!.GetValue<int>());
    }

    // ── Session plumbing ────────────────────────────────────────────────────────────────

    private static (int ExitCode, List<JsonNode> Messages) RunSession(params JsonObject[] client)
    {
        var input = new MemoryStream();
        foreach (var message in client)
        {
            var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
            var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
            input.Write(header);
            input.Write(payload);
        }
        input.Position = 0;

        var output = new MemoryStream();
        var exitCode = new LspServer(input, output, TextWriter.Null).Run();

        output.Position = 0;
        var channel = new JsonRpcChannel(output, Stream.Null);
        var messages = new List<JsonNode>();
        while (channel.Read() is { } message)
            messages.Add(message);
        return (exitCode, messages);
    }

    private static JsonObject Request(int id, string method, JsonObject? @params = null) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["method"] = method,
        ["params"] = @params,
    };

    private static JsonObject Notification(string method, JsonObject? @params = null) => new()
    {
        ["jsonrpc"] = "2.0",
        ["method"] = method,
        ["params"] = @params,
    };

    private static JsonObject DidOpen(string text) =>
        Notification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = DocumentUri,
                ["languageId"] = "jaml",
                ["version"] = 1,
                ["text"] = text,
            },
        });

    private static JsonNode ResponseTo(List<JsonNode> messages, int id) =>
        messages.Single(m => m["id"]?.GetValue<int>() == id && m["method"] is null);

    private static List<JsonNode> PublishedDiagnostics(List<JsonNode> messages) =>
        [.. messages
            .Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
            .SelectMany(m => (JsonArray)m["params"]!["diagnostics"]!)
            .Cast<JsonNode>()];
}
