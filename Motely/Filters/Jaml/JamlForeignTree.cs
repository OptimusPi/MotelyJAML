using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VYaml.Parser;

namespace Motely.Filters.Jaml;

/// <summary>
/// JSON / YAML → the existing <see cref="JMap"/> tree. One ParseConfig after that.
/// No <c>dynamic</c>, no Activator. JAML terse lines stay on <see cref="JamlDocumentParser"/>.
/// </summary>
internal static class JamlForeignTree
{
    public static JMap ParseJson(string text)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"JSON parse error: {ex.Message}", ex);
        }

        return AsMap(FromJson(node), "JSON");
    }

    public static JMap ParseYaml(string text)
    {
        try
        {
            // VYaml 1.1.1 tokenizes a bare "joker:" / "tarotCard:" (implicit null) into
            // an unbounded token queue. Quote the empty value before the tokenizer.
            text = QuoteEmptyMappingValues(text);
            var parser = YamlParser.FromBytes(Encoding.UTF8.GetBytes(text));
            parser.SkipAfter(ParseEventType.DocumentStart);
            if (parser.End
                || parser.CurrentEventType is ParseEventType.DocumentEnd or ParseEventType.StreamEnd)
                return new JMap();
            return AsMap(ReadYaml(ref parser), "YAML");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"YAML parse error: {ex.Message}", ex);
        }
    }

    private static JMap AsMap(JNode node, string kind) =>
        node as JMap ?? throw new InvalidOperationException($"{kind} root must be a mapping.");

    private static JNode FromJson(JsonNode? node) =>
        node switch
        {
            null => new JScalar(""),
            JsonObject obj => MapFrom(obj.Select(kv => (kv.Key, FromJson(kv.Value)))),
            JsonArray arr => SeqFrom(arr.Select(FromJson)),
            JsonValue value => FromJsonValue(value),
            _ => new JScalar(node.ToJsonString()),
        };

    private static JScalar FromJsonValue(JsonValue value) =>
        value.GetValueKind() switch
        {
            JsonValueKind.True => JScalar.Of(true),
            JsonValueKind.False => JScalar.Of(false),
            JsonValueKind.Number when value.TryGetValue<int>(out var i) => JScalar.Of(i),
            JsonValueKind.Number => new JScalar(value.ToJsonString(), JScalarKind.Bare),
            JsonValueKind.String => new JScalar(value.GetValue<string>() ?? "", JScalarKind.Quoted),
            _ => new JScalar(""),
        };

    private static string QuoteEmptyMappingValues(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd();
            var colon = trimmed.LastIndexOf(':');
            if (colon < 0)
                continue;
            if (trimmed.AsSpan(colon + 1).Trim().Length != 0)
                continue;
            // "must:" then an indented child is a nested map/seq, not an empty value.
            var indent = IndentWidth(trimmed);
            var j = i + 1;
            while (j < lines.Length && IndentWidth(lines[j]) == int.MaxValue)
                j++;
            if (j < lines.Length && IndentWidth(lines[j]) > indent)
                continue;
            lines[i] = trimmed + " \"\"";
        }
        return string.Join("\n", lines);
    }

    private static int IndentWidth(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] is ' ' or '\t')
            n++;
        return n == line.Length ? int.MaxValue : n;
    }

    // Each ReadYaml call consumes exactly one node (same contract as VYaml's
    // PrimitiveObjectFormatter). Extra Read() after a child was the desync:
    // empty "joker:" is EmptyScalar, not MappingEnd.
    private const int YamlPairCap = 65_536;

    private static JNode ReadYaml(ref YamlParser parser)
    {
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
            {
                var text = parser.GetScalarAsString() ?? "";
                var kind = parser.TryGetScalarAsInt32(out _)
                    ? JScalarKind.Integer
                    : JScalarKind.Bare;
                parser.Read();
                return new JScalar(text, kind);
            }
            case ParseEventType.MappingStart:
                return ReadYamlMap(ref parser);
            case ParseEventType.SequenceStart:
                return ReadYamlSeq(ref parser);
            case ParseEventType.Alias:
                throw new InvalidOperationException("YAML aliases are not supported.");
            default:
                throw new InvalidOperationException(
                    $"Unexpected YAML event {parser.CurrentEventType}."
                );
        }
    }

    private static JMap ReadYamlMap(ref YamlParser parser)
    {
        var map = new JMap();
        parser.Read();
        var n = 0;
        while (!parser.End && parser.CurrentEventType != ParseEventType.MappingEnd)
        {
            if (++n > YamlPairCap)
                throw new InvalidOperationException("YAML mapping did not terminate.");
            if (parser.CurrentEventType != ParseEventType.Scalar)
                throw new InvalidOperationException("YAML mapping key must be a scalar.");
            string key = parser.GetScalarAsString() ?? "";
            if (!parser.Read())
                throw new InvalidOperationException($"YAML mapping '{key}' is missing a value.");
            map.Set(key, ReadYaml(ref parser), default);
        }
        if (parser.CurrentEventType == ParseEventType.MappingEnd)
            parser.Read();
        return map;
    }

    private static JSeq ReadYamlSeq(ref YamlParser parser)
    {
        var seq = new JSeq();
        parser.Read();
        var n = 0;
        while (!parser.End && parser.CurrentEventType != ParseEventType.SequenceEnd)
        {
            if (++n > YamlPairCap)
                throw new InvalidOperationException("YAML sequence did not terminate.");
            seq.Items.Add(ReadYaml(ref parser));
        }
        if (parser.CurrentEventType == ParseEventType.SequenceEnd)
            parser.Read();
        return seq;
    }

    private static JMap MapFrom(IEnumerable<(string Key, JNode Value)> pairs)
    {
        var map = new JMap();
        foreach (var (key, val) in pairs)
            map.Set(key, val, default);
        return map;
    }

    private static JSeq SeqFrom(IEnumerable<JNode> items)
    {
        var seq = new JSeq();
        seq.Items.AddRange(items);
        return seq;
    }
}
