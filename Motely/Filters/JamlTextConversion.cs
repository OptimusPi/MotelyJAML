using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Motely.Filters;

/// <summary>
/// A filter moves between YAML and JSON through the engine, so a JavaScript app holds every
/// filter as one typed JSON object and never parses YAML itself. Both directions keep the
/// document as written — keys, order, values — so the loader reads the result as it reads the
/// original, and neither knows a JAML key. The walk is over YamlDotNet's representation model,
/// which uses no reflection, so it runs unchanged under NativeAOT.
/// </summary>
public static partial class JamlConfigLoader
{
    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>JSON's own duplicate-key check, so a key written twice is refused with its line, as YAML refuses it.</summary>
    private static readonly JsonDocumentOptions JsonInput = new() { AllowDuplicateProperties = false };

    /// <summary>How many values aliases may add as <see cref="ToJson"/> expands them: plenty for reuse, nothing like a bomb.</summary>
    private const int MaxAliasValues = 100_000;

    /// <summary>
    /// The filter's YAML as JSON, which the loader reads as it reads the YAML. An unquoted scalar is
    /// null or a boolean where YAML 1.2 says so, and a number where its text is already a JSON
    /// number, kept digit for digit however long. Every other scalar is a string, quoted or not,
    /// YAML's other number spellings included (<c>+5</c>, <c>010</c>, <c>0x1F</c>, <c>.5</c>,
    /// <c>.inf</c>): the loader reads those its own way (<c>010</c> is 8), and reads them the same
    /// way as strings. Aliases are expanded, as JSON has none; comments do not survive. Two things
    /// JSON cannot say read differently: <c>True</c> and <c>TRUE</c> become <c>true</c>, so a
    /// string field such as <c>name:</c> reads <c>"true"</c>; and <c>!!str</c> or <c>!!int</c>
    /// becomes the JSON type, so a tag on the wrong field (<c>cutoff: !!str 5</c>), which the
    /// loader refuses, loads from the JSON.
    /// Text that is not one YAML document, nests deeper than 64 levels, writes a key twice, carries a
    /// tag other than <c>!!str</c> or <c>!!int</c>, has a key that is not a plain value, or whose
    /// aliases would add more than 100,000 values, throws a <see cref="JamlFilterException"/> with
    /// the line, as <see cref="FromJaml"/> does.
    /// </summary>
    /// <param name="yaml">The filter as YAML (or JSON, which is YAML).</param>
    /// <returns>The same document as indented JSON.</returns>
    public static string ToJson(string yaml)
    {
        try
        {
            var parser = new JamlParser(yaml);
            var stream = new YamlStream();
            stream.Load(parser);

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, JsonOptions))
            {
                if (stream.Documents.Count == 0)
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }
                else
                {
                    int budget = parser.Nodes + MaxAliasValues;
                    WriteJson(writer, stream.Documents[0].RootNode, depth: 1, ref budget);
                }
            }
            return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        }
        catch (YamlException ex)
        {
            throw new JamlFilterException(
                $"JAML line {ex.Start.Line}: {ex.Message}", (int)ex.Start.Line, (int)ex.Start.Column, ex);
        }
    }

    /// <summary>
    /// The filter's JSON as block YAML: mappings and clause lists as blocks, and a list of plain
    /// values on one line (<c>antes: [1, 2]</c>). Strings stay plain when YAML would read them back
    /// as the same string, and are double-quoted otherwise; numbers keep their JSON text. JSON that
    /// does not parse, writes a key twice, or holds a string YAML cannot (an unpaired surrogate,
    /// <c>"\ud800"</c>) throws a <see cref="JamlFilterException"/> with the line.
    /// </summary>
    /// <param name="json">The filter as JSON.</param>
    /// <returns>The same document as YAML.</returns>
    public static string ToYaml(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: JsonInput);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } at ? (int)at + 1 : LineOfRefusedToken(json);
            throw new JamlFilterException($"JSON line {line}: {ex.Message}", line, (int)(ex.BytePositionInLine ?? 0), ex);
        }

        var sb = new StringBuilder();
        try
        {
            if (root is JsonObject map)
                WriteMapping(sb, map, indent: 0, continuesLine: false);
            else
                sb.Append(Flow(root)).Append('\n');
        }
        catch (InvalidOperationException ex)
        {
            // Raised as a string is read, which parsing does not do: an unpaired surrogate escape.
            var line = LineOfRefusedToken(json);
            throw new JamlFilterException($"JSON line {line}: {ex.Message}", line, 0, ex);
        }
        return sb.ToString();
    }

    // ── YAML → JSON ──────────────────────────────────────────────────────────────────

    /// <param name="depth">The collection depth <paramref name="node"/> opens, aliases expanded.</param>
    /// <param name="budget">Values left to write: the document's own, plus what aliases may add.</param>
    private static void WriteJson(Utf8JsonWriter writer, YamlNode node, int depth, ref int budget)
    {
        if (--budget < 0)
            throw new YamlException(node.Start, node.End, $"aliases expand the document by more than {MaxAliasValues} values.");
        RefuseTag(node);
        switch (node)
        {
            case YamlMappingNode map:
                RefuseDepth(node, depth);
                writer.WriteStartObject();
                foreach (var (key, value) in map.Children)
                {
                    if (key is not YamlScalarNode name)
                        throw new YamlException(key.Start, key.End, "a key is a plain value, not a list or a mapping.");
                    RefuseTag(name);
                    writer.WritePropertyName(name.Value ?? "");
                    WriteJson(writer, value, depth + 1, ref budget);
                }
                writer.WriteEndObject();
                break;
            case YamlSequenceNode sequence:
                RefuseDepth(node, depth);
                writer.WriteStartArray();
                foreach (var item in sequence.Children)
                    WriteJson(writer, item, depth + 1, ref budget);
                writer.WriteEndArray();
                break;
            case YamlScalarNode scalar:
                WriteScalar(writer, scalar);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>The parser caps nesting as written; this caps it as aliases expand it, a cycle included.</summary>
    private static void RefuseDepth(YamlNode node, int depth)
    {
        if (depth > JamlParser.MaxDepth)
            throw new YamlException(node.Start, node.End, JamlParser.DepthMessage);
    }

    /// <summary>
    /// The loader reads <c>!!str</c> and <c>!!int</c> where they name the field's own type and
    /// refuses every other tag. JSON would drop a tag silently and the loader would then accept
    /// the value, so a tag the loader refuses is refused here.
    /// </summary>
    private static void RefuseTag(YamlNode node)
    {
        if (!node.Tag.IsEmpty && !(node is YamlScalarNode && (IsStringTag(node.Tag) || node.Tag.Value is "tag:yaml.org,2002:int" or "!!int")))
            throw new YamlException(node.Start, node.End, $"a filter has no tag '{node.Tag.Value}'; write the value without it.");
    }

    private static void WriteScalar(Utf8JsonWriter writer, YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        bool typed = (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any) && !IsStringTag(scalar.Tag);
        if (!typed)
            writer.WriteStringValue(value);
        else if (value is "" or "~" or "null" or "Null" or "NULL")
            writer.WriteNullValue();
        else if (value is "true" or "True" or "TRUE")
            writer.WriteBooleanValue(true);
        else if (value is "false" or "False" or "FALSE")
            writer.WriteBooleanValue(false);
        else if (JsonNumber().IsMatch(value))
            JsonElement.Parse(value).WriteTo(writer); // its text, indented as a value (WriteRawValue is not)
        else
            writer.WriteStringValue(value);
    }

    private static bool IsStringTag(TagName tag) =>
        !tag.IsEmpty && tag.Value is "tag:yaml.org,2002:str" or "!!str";

    /// <summary>
    /// The line of the first token parsing lets through and the conversion cannot take, since
    /// neither error carries a position: a key written twice in one object, or a string holding an
    /// unpaired surrogate escape.
    /// </summary>
    private static int LineOfRefusedToken(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        var keys = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                keys.Push([]);
            else if (reader.TokenType == JsonTokenType.EndObject)
                keys.Pop();
            else if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
            {
                string text;
                try
                {
                    text = reader.GetString()!;
                }
                catch (InvalidOperationException)
                {
                    return LineAt(bytes, reader.TokenStartIndex);
                }
                if (reader.TokenType == JsonTokenType.PropertyName && !keys.Peek().Add(text))
                    return LineAt(bytes, reader.TokenStartIndex);
            }
        }
        return 1;
    }

    private static int LineAt(byte[] utf8, long offset) => 1 + utf8.AsSpan(0, (int)offset).Count((byte)'\n');

    // ── JSON → YAML ──────────────────────────────────────────────────────────────────

    /// <param name="continuesLine">The first key goes on the current line (after a list item's "- ").</param>
    private static void WriteMapping(StringBuilder sb, JsonObject map, int indent, bool continuesLine)
    {
        bool first = true;
        foreach (var (key, value) in map)
        {
            if (!(first && continuesLine))
                sb.Append(' ', indent);
            first = false;
            sb.Append(PlainOrQuoted(key)).Append(':');
            WriteValue(sb, value, indent);
        }
        if (first)
            sb.Append("{}\n");
    }

    /// <summary>Writes what follows <c>key:</c>: an inline value, or a nested block on the next lines.</summary>
    private static void WriteValue(StringBuilder sb, JsonNode? value, int indent)
    {
        switch (value)
        {
            case JsonObject { Count: > 0 } child:
                sb.Append('\n');
                WriteMapping(sb, child, indent + 2, continuesLine: false);
                break;
            case JsonArray array when array.Count > 0 && !array.All(IsInline):
                sb.Append('\n');
                WriteSequence(sb, array, indent + 2);
                break;
            default:
                sb.Append(' ').Append(Flow(value)).Append('\n');
                break;
        }
    }

    private static void WriteSequence(StringBuilder sb, JsonArray array, int indent)
    {
        foreach (var item in array)
        {
            sb.Append(' ', indent).Append("- ");
            switch (item)
            {
                case JsonObject { Count: > 0 } map:
                    WriteMapping(sb, map, indent + 2, continuesLine: true);
                    break;
                case JsonArray nested when nested.Count > 0 && !nested.All(IsInline):
                    sb.Append('\n');
                    WriteSequence(sb, nested, indent + 2);
                    break;
                default:
                    sb.Append(Flow(item)).Append('\n');
                    break;
            }
        }
    }

    private static bool IsInline(JsonNode? node) => node is null or JsonValue || node is JsonObject { Count: 0 } || node is JsonArray { Count: 0 };

    /// <summary>A value on one line: a scalar, or a flow list/map of scalars.</summary>
    private static string Flow(JsonNode? node) =>
        node switch
        {
            null => "null",
            JsonArray array => "[" + string.Join(", ", array.Select(Flow)) + "]",
            JsonObject map => "{" + string.Join(", ", map.Select(p => PlainOrQuoted(p.Key) + ": " + Flow(p.Value))) + "}",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => PlainOrQuoted(v.GetValue<string>()),
            _ => node.ToJsonString(),
        };

    /// <summary>
    /// A string YAML reads back as the same string when written plain, else double-quoted — and a
    /// JSON string literal is a valid YAML double-quoted scalar.
    /// </summary>
    private static string PlainOrQuoted(string value) =>
        SafePlain().IsMatch(value) && !LooksTyped(value) ? value : Quoted(value);

    private static string Quoted(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => sb.Append("\\\\"),
                '"' => sb.Append("\\\""),
                '\n' => sb.Append("\\n"),
                '\r' => sb.Append("\\r"),
                '\t' => sb.Append("\\t"),
                _ when MustEscape(c) => sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)),
                _ => sb.Append(c),
            };
        }
        return sb.Append('"').ToString();
    }

    /// <summary>
    /// What a quoted YAML scalar cannot hold raw: the C0 and C1 controls and DEL, which YAML does not
    /// print; U+0085, U+2028 and U+2029, which it reads as line breaks; the byte-order mark; and the
    /// non-characters U+FFFE and U+FFFF.
    /// </summary>
    private static bool MustEscape(char c) =>
        c < ' ' || c is (>= '\u007f' and <= '\u009f') or '\u2028' or '\u2029' or '\ufeff' or '\ufffe' or '\uffff';

    /// <summary>Plain words YAML would read as something other than a string.</summary>
    private static bool LooksTyped(string value) =>
        value.ToLowerInvariant() is "null" or "true" or "false" or "yes" or "no" or "on" or "off" or "y" or "n" or "~";

    /// <summary>A JSON number as JSON writes it: no sign but minus, no leading zero, digits both sides of the point.</summary>
    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?\z")]
    private static partial Regex JsonNumber();

    /// <summary>Starts with a letter or underscore; words, digits, spaces, dots, dashes, apostrophes
    /// and slashes after it; never ends in a space.</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_ .'/-]*(?<! )\z")]
    private static partial Regex SafePlain();
}
