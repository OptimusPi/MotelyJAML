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
/// document as written — keys, order, values — and neither interprets JAML: what the loader
/// makes of the result is the loader's business. The walk is over YamlDotNet's representation
/// model, which uses no reflection, so it runs unchanged under NativeAOT.
/// </summary>
public static partial class JamlConfigLoader
{
    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The filter's YAML as JSON. Unquoted scalars are typed the way YAML 1.2 reads them — null,
    /// true/false, integers, floats — and everything else, including every quoted scalar, is a
    /// string. Comments do not survive: JSON has none. Text that is not YAML throws a
    /// <see cref="JamlFilterException"/> with the line, as <see cref="FromJaml"/> does.
    /// </summary>
    /// <param name="yaml">The filter as YAML (or JSON, which is YAML).</param>
    /// <returns>The same document as indented JSON.</returns>
    public static string ToJson(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw new JamlFilterException(
                $"JAML line {ex.Start.Line}: {ex.Message}", (int)ex.Start.Line, (int)ex.Start.Column, ex);
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, JsonOptions))
        {
            if (stream.Documents.Count == 0)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
                WriteJson(writer, stream.Documents[0].RootNode);
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    /// <summary>
    /// The filter's JSON as block YAML: mappings and clause lists as blocks, and a list of plain
    /// values on one line (<c>antes: [1, 2]</c>). Strings stay plain when YAML would read them back
    /// as the same string, and are double-quoted otherwise.
    /// </summary>
    /// <param name="json">The filter as JSON.</param>
    /// <returns>The same document as YAML.</returns>
    public static string ToYaml(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            var line = (int)(ex.LineNumber ?? 0) + 1;
            throw new JamlFilterException($"JSON line {line}: {ex.Message}", line, (int)(ex.BytePositionInLine ?? 0), ex);
        }

        var sb = new StringBuilder();
        if (root is JsonObject map)
            WriteMapping(sb, map, indent: 0, continuesLine: false);
        else
            sb.Append(Flow(root)).Append('\n');
        return sb.ToString();
    }

    // ── YAML → JSON ──────────────────────────────────────────────────────────────────

    private static void WriteJson(Utf8JsonWriter writer, YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode map:
                writer.WriteStartObject();
                foreach (var (key, value) in map.Children)
                {
                    writer.WritePropertyName(key is YamlScalarNode k ? k.Value ?? "" : key.ToString());
                    WriteJson(writer, value);
                }
                writer.WriteEndObject();
                break;
            case YamlSequenceNode sequence:
                writer.WriteStartArray();
                foreach (var item in sequence.Children)
                    WriteJson(writer, item);
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

    private static void WriteScalar(Utf8JsonWriter writer, YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        bool typed = (scalar.Style is ScalarStyle.Plain or ScalarStyle.Any) && !IsStringTag(scalar);
        if (!typed)
        {
            writer.WriteStringValue(value);
            return;
        }
        if (value is "" or "~" or "null" or "Null" or "NULL")
            writer.WriteNullValue();
        else if (value is "true" or "True" or "TRUE")
            writer.WriteBooleanValue(true);
        else if (value is "false" or "False" or "FALSE")
            writer.WriteBooleanValue(false);
        else if (DecimalInt().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            writer.WriteNumberValue(number);
        else if (value.StartsWith("0x", StringComparison.Ordinal) && long.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
            writer.WriteNumberValue(hex);
        else if (DecimalFloat().IsMatch(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real))
            writer.WriteNumberValue(real);
        else
            writer.WriteStringValue(value);
    }

    private static bool IsStringTag(YamlScalarNode scalar) =>
        !scalar.Tag.IsEmpty && scalar.Tag.Value is "tag:yaml.org,2002:str" or "!!str";

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
                < ' ' => sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)),
                _ => sb.Append(c),
            };
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Plain words YAML would read as something other than a string.</summary>
    private static bool LooksTyped(string value) =>
        value.ToLowerInvariant() is "null" or "true" or "false" or "yes" or "no" or "on" or "off" or "y" or "n" or "~";

    [GeneratedRegex(@"^[+-]?[0-9]+\z")]
    private static partial Regex DecimalInt();

    [GeneratedRegex(@"^[+-]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][+-]?[0-9]+)?\z")]
    private static partial Regex DecimalFloat();

    /// <summary>Starts with a letter or underscore; words, digits, spaces, dots, dashes, apostrophes
    /// and slashes after it; never ends in a space.</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_ .'/-]*(?<! )\z")]
    private static partial Regex SafePlain();
}
