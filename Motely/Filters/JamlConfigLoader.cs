using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VYaml.Parser;

namespace Motely.Filters;

/// <summary>
/// YAML or JSON text → <see cref="JamlConfig"/>. One door: YAML is streamed into JSON (JSON is
/// already YAML, so it streams through unchanged), then System.Text.Json's source-generated
/// readers fill the config. No reflection, so trimming and NativeAOT keep working.
/// <para>
/// The loader never invents a value. A key the document leaves out keeps the class's own
/// initializer; a key written as null is null.
/// </para>
/// </summary>
public static class JamlConfigLoader
{
    public static JamlConfig FromJaml(string text)
    {
        byte[] json = YamlToJson(text);
        try
        {
            return JsonSerializer.Deserialize(json, JamlJsonContext.Default.JamlConfig)
                ?? throw new InvalidOperationException("JAML: the document is empty.");
        }
        catch (JsonException ex)
        {
            // A value error names its key, so find that key's line; a root key's line is known;
            // anything else names its path (and its clause).
            for (Exception? e = ex; e is not null; e = e.InnerException)
                if (e is JamlValueException value)
                    throw new InvalidOperationException(
                        KeyLine(text, value.Key) is int keyLine
                            ? $"JAML line {keyLine}: {value.Message}"
                            : $"JAML: {value.Message}",
                        ex
                    );
            string where = RootKey(ex.Path) is { } key && RootKeyLine(text, key) is int line
                ? $"line {line}"
                : ex.Path ?? "$";
            throw new InvalidOperationException($"JAML {where}: {FirstSentence(ex.Message)}", ex);
        }
    }

    /// <summary>The first line that writes <c>key:</c>, block or flow style.</summary>
    private static int? KeyLine(string text, string key)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Contains(key + ":", StringComparison.OrdinalIgnoreCase))
                return i + 1;
        return null;
    }

    /// <summary>
    /// The line a block-style root key is written on: root keys start at column 0, so it is the
    /// first line that begins with <c>key:</c>. Null for flow style (JSON), which has no such line.
    /// </summary>
    private static int? RootKeyLine(string text, string key)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith(key, StringComparison.Ordinal)
                && lines[i].AsSpan(key.Length).TrimStart(' ').StartsWith(":"))
                return i + 1;
        return null;
    }

    /// <summary>`$.shuold` or `$.must[2].joker` → the root key, `shuold` / `must`.</summary>
    private static string? RootKey(string? path)
    {
        if (path is null || !path.StartsWith("$.", StringComparison.Ordinal))
            return null;
        int end = path.IndexOfAny(['.', '['], 2);
        return end < 0 ? path[2..] : path[2..end];
    }

    public static JamlConfig FromFile(string path) => FromJaml(File.ReadAllText(path));

    public static bool TryLoad(string text, [NotNullWhen(true)] out JamlConfig? config, out string? error)
    {
        try
        {
            config = FromJaml(text);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            config = null;
            error = ex.Message;
            return false;
        }
    }

    // STJ appends " Path: $.x | LineNumber: n | BytePositionInLine: m." — the line is in the
    // generated JSON, not the author's file, so keep only the sentence and report the path.
    private static string FirstSentence(string message)
    {
        int cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        return cut >= 0 ? message[..cut] : message;
    }

    /// <summary>
    /// YAML events straight into a JSON writer. Plain null is null and true/false are booleans;
    /// every other scalar is a string, and numbers are read from strings by the context's number
    /// handling — so a seed like <c>1234ABCD</c> or <c>11111111</c> never turns into a number.
    /// </summary>
    internal static byte[] YamlToJson(string yaml)
    {
        var parser = new YamlParser(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(yaml)));
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
        {
            try
            {
                parser.SkipHeader();
                if (parser.End || parser.CurrentEventType is ParseEventType.DocumentEnd or ParseEventType.StreamEnd)
                    throw new InvalidOperationException("JAML: the document is empty.");
                WriteNode(ref parser, writer);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // VYaml's tokenizer and parser exceptions: one type, with the line named and quoted.
                throw new InvalidOperationException(DescribeYamlError(yaml, ex.Message), ex);
            }
        }
        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// VYaml ends its messages with "at Line: N, Col: C, Idx: I" (line 1-based, column 0-based)
    /// and never shows the text. Say "JAML line N:" and quote the line instead.
    /// </summary>
    private static string DescribeYamlError(string yaml, string message)
    {
        const string marker = " at Line: ";
        int at = message.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return $"JAML: {message}";
        var parts = message[(at + marker.Length)..].Split(", ");
        if (parts.Length < 2 || !int.TryParse(parts[0], out int line) || !int.TryParse(parts[1].Replace("Col: ", ""), out int col))
            return $"JAML: {message}";

        string what = message[..at];
        var lines = yaml.Split('\n');
        string text = line >= 1 && line <= lines.Length ? lines[line - 1].Trim() : "";
        return text.Length == 0
            ? $"JAML line {line}: {what} (column {col + 1})"
            : $"JAML line {line}: {what} (column {col + 1}): `{text}`";
    }

    private static void WriteNode(ref YamlParser parser, Utf8JsonWriter writer)
    {
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
                if (parser.IsNullScalar())
                {
                    writer.WriteNullValue();
                    parser.Read();
                    break;
                }
                var text = parser.ReadScalarAsString()!;
                if (text is "true" or "True" or "TRUE")
                    writer.WriteBooleanValue(true);
                else if (text is "false" or "False" or "FALSE")
                    writer.WriteBooleanValue(false);
                else
                    writer.WriteStringValue(text);
                break;

            case ParseEventType.SequenceStart:
                writer.WriteStartArray();
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.SequenceEnd)
                    WriteNode(ref parser, writer);
                parser.Read();
                writer.WriteEndArray();
                break;

            case ParseEventType.MappingStart:
                writer.WriteStartObject();
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.MappingEnd)
                {
                    writer.WritePropertyName(parser.ReadScalarAsString() ?? "");
                    WriteNode(ref parser, writer);
                }
                parser.Read();
                writer.WriteEndObject();
                break;

            case ParseEventType.Alias:
                throw new InvalidOperationException("JAML: anchors and aliases (&name / *name) are not supported.");

            default:
                throw new InvalidOperationException($"JAML: unexpected YAML event {parser.CurrentEventType}.");
        }
    }
}

/// <summary>
/// <c>- uncommonJoker: Blueprint</c> → <see cref="UncommonJokerClause"/>. The clause key picks the
/// type from <see cref="Keys"/>; its value moves to the property it names (<c>jokers</c>, the
/// event clauses' <c>rolls</c>, a logic clause's <c>clauses</c>), wrapped into a list when the
/// author wrote a single value. Everything else in the mapping is that type's own keys.
/// </summary>
public sealed class JamlClauseConverter : JsonConverter<IMotelyClause>
{
    private readonly record struct Key(JsonTypeInfo TypeInfo, string? ValueProperty, bool ValueIsList);

    private static JamlJsonContext Ctx => JamlJsonContext.Default;

    private static readonly Dictionary<string, Key> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["joker"] = new(Ctx.JokerClause, "jokers", true),
        ["jokers"] = new(Ctx.JokerClause, "jokers", true),
        ["commonJoker"] = new(Ctx.CommonJokerClause, "jokers", true),
        ["commonJokers"] = new(Ctx.CommonJokerClause, "jokers", true),
        ["uncommonJoker"] = new(Ctx.UncommonJokerClause, "jokers", true),
        ["uncommonJokers"] = new(Ctx.UncommonJokerClause, "jokers", true),
        ["rareJoker"] = new(Ctx.RareJokerClause, "jokers", true),
        ["rareJokers"] = new(Ctx.RareJokerClause, "jokers", true),
        ["legendaryJoker"] = new(Ctx.LegendaryJokerClause, "jokers", true),
        ["legendaryJokers"] = new(Ctx.LegendaryJokerClause, "jokers", true),
        ["planetCard"] = new(Ctx.PlanetCardClause, "planets", true),
        ["planetCards"] = new(Ctx.PlanetCardClause, "planets", true),
        ["spectralCard"] = new(Ctx.SpectralCardClause, "spectrals", true),
        ["spectralCards"] = new(Ctx.SpectralCardClause, "spectrals", true),
        ["tarotCard"] = new(Ctx.TarotCardClause, "tarots", true),
        ["tarotCards"] = new(Ctx.TarotCardClause, "tarots", true),
        ["standardCard"] = new(Ctx.StandardCardClause, "label", false),
        ["standardCards"] = new(Ctx.StandardCardClause, "label", false),
        ["erraticRank"] = new(Ctx.ErraticRankClause, "rank", false),
        ["erraticRanks"] = new(Ctx.ErraticRankClause, "rank", false),
        ["erraticSuit"] = new(Ctx.ErraticSuitClause, "suit", false),
        ["erraticSuits"] = new(Ctx.ErraticSuitClause, "suit", false),
        ["voucher"] = new(Ctx.VoucherClause, "vouchers", true),
        ["vouchers"] = new(Ctx.VoucherClause, "vouchers", true),
        ["tag"] = new(Ctx.TagClause, "tags", true),
        ["tags"] = new(Ctx.TagClause, "tags", true),
        ["smallBlindTag"] = new(Ctx.SmallBlindTagClause, "tags", true),
        ["bigBlindTag"] = new(Ctx.BigBlindTagClause, "tags", true),
        ["boss"] = new(Ctx.BossClause, "bosses", true),
        ["bosses"] = new(Ctx.BossClause, "bosses", true),
        ["boosterPack"] = new(Ctx.BoosterPackClause, "packs", true),
        ["boosterPacks"] = new(Ctx.BoosterPackClause, "packs", true),
        ["pokerHand"] = new(Ctx.PokerHandClause, "pokerHands", true),
        ["pokerHands"] = new(Ctx.PokerHandClause, "pokerHands", true),
        ["startingDraw"] = new(Ctx.StartingDrawClause, "label", false),
        ["luckyMoney"] = new(Ctx.LuckyMoneyClause, "rolls", true),
        ["luckyMult"] = new(Ctx.LuckyMultClause, "rolls", true),
        ["misprintMult"] = new(Ctx.MisprintMultClause, "rolls", true),
        ["wheelOfFortune"] = new(Ctx.WheelOfFortuneClause, "rolls", true),
        ["wheelStaysFlipped"] = new(Ctx.WheelStaysFlippedClause, "rolls", true),
        ["glassDestroy"] = new(Ctx.GlassDestroyClause, "rolls", true),
        ["cavendishExtinct"] = new(Ctx.CavendishExtinctClause, "rolls", true),
        ["grosMichelExtinct"] = new(Ctx.GrosMichelExtinctClause, "rolls", true),
        ["spaceLevelup"] = new(Ctx.SpaceLevelupClause, "rolls", true),
        ["businessPayout"] = new(Ctx.BusinessPayoutClause, "rolls", true),
        ["parkingPayout"] = new(Ctx.ParkingPayoutClause, "rolls", true),
        ["bloodstoneTrigger"] = new(Ctx.BloodstoneTriggerClause, "rolls", true),
        ["and"] = new(Ctx.AndClause, "clauses", true),
        ["or"] = new(Ctx.OrClause, "clauses", true),
    };

    public override IMotelyClause? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject obj)
            throw new JsonException("a clause is a mapping like `- joker: Blueprint`.");

        string? wire = null;
        foreach (var (name, _) in obj)
        {
            if (Keys.ContainsKey(name))
            {
                wire = name;
                break;
            }
        }
        if (wire is null)
            throw new JsonException($"no clause key among: {string.Join(", ", obj.Select(p => p.Key))}.");

        var key = Keys[wire];
        var value = obj[wire];
        obj.Remove(wire);

        if (value is JsonObject block)
        {
            // `- or: { mode: max, clauses: [...] }`: the block is the clause's own keys.
            foreach (var (name, child) in block.ToArray())
            {
                block.Remove(name);
                AddOnce(obj, name, child, wire);
            }
        }
        else if (value is not null && key.ValueProperty is { } property)
        {
            // `joker: any` is the engine's own "whole category": an empty list.
            if (key.ValueIsList && value is JsonValue v && v.TryGetValue(out string? text)
                && string.Equals(text.Trim(), "any", StringComparison.OrdinalIgnoreCase))
                value = new JsonArray();
            AddOnce(obj, property, key.ValueIsList && value is not JsonArray ? new JsonArray(value) : value, wire);
        }

        ExpandIntLists(obj);

        try
        {
            return (IMotelyClause?)obj.Deserialize(key.TypeInfo);
        }
        catch (JsonException ex) when (ex is not JamlValueException)
        {
            // The path restarts inside this clause, so say which clause it is —
            // and name the offending value so the fix is visible without opening the file.
            var raw = value is JsonValue v && v.TryGetValue(out string? text)
                ? text.Trim()
                : value?.ToJsonString();
            throw new JamlValueException(wire,
                $"in `{wire}` clause at {ex.Path ?? "$"}: {ex.Message.Split(" Path:")[0]} (value `{raw}`)");
        }
    }

    // ── Integer lists: ranges and ante bounds (ADR-001 revisit) ──
    //
    // Every int list takes range shorthand: `1-8`, `1..8`, `1 to 8`, alone or as list items
    // (`[1..3, 7]`). No enum value looks like that, so a scalar shaped as a range is expanded
    // wherever it appears in the clause, sources: included. `antes` is always an int list: a
    // bare `antes: 4` is [4], and every ante must be 0-39.

    internal const int MaxExpandedLength = 1024;
    internal const int MinAnte = 0;
    internal const int MaxAnte = 39;

    private static void ExpandIntLists(JsonObject obj)
    {
        foreach (var (name, node) in obj.ToArray())
        {
            if (node is JsonObject child)
            {
                ExpandIntLists(child);
                continue;
            }
            bool isAntes = name.Equals("antes", StringComparison.OrdinalIgnoreCase);
            JsonNode?[] items = node switch
            {
                JsonArray array => [.. array],
                JsonValue => [node],
                _ => [],
            };
            if (items.Length == 0 || (!isAntes && !items.Any(i => TryParseRange(Text(i), out _, out _))))
                continue;
            obj[name] = IntList(items, name, isAntes);
        }
    }

    private static JsonArray IntList(JsonNode?[] items, string key, bool isAntes)
    {
        var values = new List<int>();
        foreach (var item in items)
        {
            string text = Text(item);
            if (TryParseRange(text, out int from, out int to))
            {
                if (from > to)
                    throw new JamlValueException(key, $"`{text.Trim()}` is a descending range; write `{to}-{from}` (key `{key}`)");
                // long: 0-2147483647 is 2^31 values. The cap is on the whole list once expanded.
                long span = (long)to - from + 1;
                if (span > MaxExpandedLength - values.Count)
                    throw new JamlValueException(key, values.Count == 0
                        ? $"`{text.Trim()}` spans {span} values; ranges cover at most {MaxExpandedLength} values per key (key `{key}`)"
                        : $"`{text.Trim()}` brings the list to {values.Count + span} values; ranges cover at most {MaxExpandedLength} values per key (key `{key}`)");
                // Count, not `v <= to; v++`: with `to == int.MaxValue` that counter wraps.
                for (int k = 0; k < (int)span; k++)
                    values.Add(from + k);
            }
            else if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int single))
                values.Add(single);
            else
                throw new JamlValueException(key, $"`{text}` is not an integer (key `{key}`)");
        }
        // Bounds after the whole list is expanded, so an oversized list reports its size first.
        foreach (int value in values)
            CheckAnte(value, key, isAntes);
        return [.. values.Select(v => (JsonNode?)v)];
    }

    private static int CheckAnte(int value, string key, bool isAntes) =>
        isAntes && (value < MinAnte || value > MaxAnte)
            ? throw new JamlValueException(key, $"ante `{value}` is out of range; antes run {MinAnte}-{MaxAnte} (key `{key}`)")
            : value;

    private static string Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : node?.ToJsonString() ?? "";

    private static bool TryParseRange(string scalar, out int from, out int to)
    {
        from = to = 0;
        var s = scalar.AsSpan().Trim();
        int sep, sepLength;
        if ((sep = s.IndexOf("..", StringComparison.Ordinal)) >= 0) sepLength = 2;
        else if ((sep = s.IndexOf(" to ", StringComparison.OrdinalIgnoreCase)) >= 0) sepLength = 4;
        else if ((sep = s.IndexOf('-')) > 0) sepLength = 1; // > 0: a leading '-' is a sign
        else return false;
        // NumberStyles.None: digits only, so neither side can smuggle a sign or a second range.
        return int.TryParse(s[..sep].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out from)
            && int.TryParse(s[(sep + sepLength)..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out to);
    }

    private static void AddOnce(JsonObject obj, string name, JsonNode? value, string wire)
    {
        if (obj.ContainsKey(name))
            throw new JsonException($"`{wire}:` already sets `{name}`; it is also written as its own key.");
        obj[name] = value;
    }

    public override void Write(Utf8JsonWriter writer, IMotelyClause value, JsonSerializerOptions options) =>
        throw new NotSupportedException("JAML clauses are read, not written.");
}

/// <summary>A bad value under a known key; the loader finds that key's line for the message.</summary>
internal sealed class JamlValueException(string key, string message) : JsonException(message)
{
    public string Key { get; } = key;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = [typeof(JamlClauseConverter)]
)]
[JsonSerializable(typeof(JamlConfig))]
[JsonSerializable(typeof(AndClause))]
[JsonSerializable(typeof(OrClause))]
[JsonSerializable(typeof(JokerClause))]
[JsonSerializable(typeof(CommonJokerClause))]
[JsonSerializable(typeof(UncommonJokerClause))]
[JsonSerializable(typeof(RareJokerClause))]
[JsonSerializable(typeof(LegendaryJokerClause))]
[JsonSerializable(typeof(PlanetCardClause))]
[JsonSerializable(typeof(SpectralCardClause))]
[JsonSerializable(typeof(TarotCardClause))]
[JsonSerializable(typeof(StandardCardClause))]
[JsonSerializable(typeof(ErraticRankClause))]
[JsonSerializable(typeof(ErraticSuitClause))]
[JsonSerializable(typeof(VoucherClause))]
[JsonSerializable(typeof(TagClause))]
[JsonSerializable(typeof(SmallBlindTagClause))]
[JsonSerializable(typeof(BigBlindTagClause))]
[JsonSerializable(typeof(BossClause))]
[JsonSerializable(typeof(BoosterPackClause))]
[JsonSerializable(typeof(PokerHandClause))]
[JsonSerializable(typeof(StartingDrawClause))]
[JsonSerializable(typeof(LuckyMoneyClause))]
[JsonSerializable(typeof(LuckyMultClause))]
[JsonSerializable(typeof(MisprintMultClause))]
[JsonSerializable(typeof(WheelOfFortuneClause))]
[JsonSerializable(typeof(WheelStaysFlippedClause))]
[JsonSerializable(typeof(GlassDestroyClause))]
[JsonSerializable(typeof(CavendishExtinctClause))]
[JsonSerializable(typeof(GrosMichelExtinctClause))]
[JsonSerializable(typeof(SpaceLevelupClause))]
[JsonSerializable(typeof(BusinessPayoutClause))]
[JsonSerializable(typeof(ParkingPayoutClause))]
[JsonSerializable(typeof(BloodstoneTriggerClause))]
internal partial class JamlJsonContext : JsonSerializerContext;
