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

    private static JNode ReadYaml(ref YamlParser parser) =>
        parser.CurrentEventType switch
        {
            ParseEventType.Scalar => new JScalar(
                parser.GetScalarAsString() ?? "",
                parser.TryGetScalarAsInt32(out _) ? JScalarKind.Integer : JScalarKind.Bare
            ),
            ParseEventType.MappingStart => ReadYamlMap(ref parser),
            ParseEventType.SequenceStart => ReadYamlSeq(ref parser),
            ParseEventType.Alias => throw new InvalidOperationException(
                "YAML aliases are not supported."
            ),
            _ => throw new InvalidOperationException(
                $"Unexpected YAML event {parser.CurrentEventType}."
            ),
        };

    private static JMap ReadYamlMap(ref YamlParser parser)
    {
        var map = new JMap();
        parser.Read();
        while (!parser.End && parser.CurrentEventType != ParseEventType.MappingEnd)
        {
            if (parser.CurrentEventType != ParseEventType.Scalar)
                throw new InvalidOperationException("YAML mapping key must be a scalar.");
            string key = parser.GetScalarAsString() ?? "";
            if (!parser.Read())
                throw new InvalidOperationException($"YAML mapping '{key}' is missing a value.");
            map.Set(key, ReadYaml(ref parser), default);
            parser.Read();
        }
        return map;
    }

    private static JSeq ReadYamlSeq(ref YamlParser parser)
    {
        var seq = new JSeq();
        parser.Read();
        while (!parser.End && parser.CurrentEventType != ParseEventType.SequenceEnd)
        {
            seq.Items.Add(ReadYaml(ref parser));
            parser.Read();
        }
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
