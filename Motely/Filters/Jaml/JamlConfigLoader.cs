using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;


using VYaml.Parser;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

/// <summary>YAML text → <see cref="JamlConfig"/>. VYaml does the document; <see cref="JamlClauseFormatter"/> does the clauses.</summary>
public static partial class JamlConfigLoader
{
    /// <summary>One entry point for every filter file. JSON is a subset of YAML, so .json,
    /// .yaml, .yml and .jaml all go through the same parser.</summary>
    public static JamlConfig FromJaml(string yaml)
    {
        var bytes = Encoding.UTF8.GetBytes(yaml);
        JamlConfig? config;
        var lines = JamlScalarLines.TryBuild(bytes);
        var outer = JamlClauseFormatter.Source;
        JamlClauseFormatter.Source = lines;
        try
        {
            RejectUnknownRootKeys(bytes, lines);
            config = ReadDocument(bytes);
        }
        catch (InvalidOperationException)
        {
            throw; // JamlClauseFormatter already says "JAML line N: …"
        }
        catch (Exception ex)
        {
            // VYaml's own parser/serializer exceptions: one type for every host to catch.
            throw new InvalidOperationException(DescribeYamlError(yaml, ex.Message), ex);
        }
        finally
        {
            JamlClauseFormatter.Source = outer;
        }
        if (config is null)
            throw new InvalidOperationException("JAML: the document is empty.");


        // VYaml's generated deserializer assigns default(T) to every key the document leaves
        // out, which skips the property initializers. Put the empty lists back so a filter
        // with no mustNot (or no seeds) does not null-ref in the engine.
        config.Id ??= "";
        config.Seeds ??= [];
        config.Must ??= [];
        config.Should ??= [];
        config.MustNot ??= [];
        return config;
    }

    // ── The document, read with VYaml's parser only ──
    // VYaml's serializer layer cannot ship to NativeAOT / WASM: every YamlSerializerOptions starts
    // out with Resolver = StandardResolver.Instance, which finds formatters by reflection
    // (GetNestedType, MakeGenericType): the trimmer's IL2104 / IL3053. The parser has none of
    // that, so the loader reads JamlConfig with it directly, with the semantics VYaml's
    // YamlSerializer.Deserialize and JamlConfig's generated formatter had: the same keys (exact,
    // lowerCamelCase, others skipped), the same null and alias handling, the same enum names.

    private delegate T Reader<T>(ref YamlParser parser, Dictionary<Anchor, object?> anchors);

    private static JamlConfig? ReadDocument(byte[] bytes)
    {
        var parser = new YamlParser(new System.Buffers.ReadOnlySequence<byte>(bytes));
        parser.SkipHeader();
        if (parser.End)
            return null;
        return ReadWithAlias(ref parser, [], ReadConfig);
    }

    private static JamlConfig? ReadConfig(ref YamlParser parser, Dictionary<Anchor, object?> anchors)
    {
        if (parser.IsNullScalar())
        {
            parser.Read();
            return null;
        }
        parser.ReadWithVerify(ParseEventType.MappingStart);
        var config = new JamlConfig
        {
            // As the generated formatter did: a key the document leaves out is default(T).
            Id = null!,
            Seeds = null!,
            Must = null!,
            Should = null!,
            MustNot = null!,
        };
        while (!parser.End && parser.CurrentEventType != ParseEventType.MappingEnd)
        {
            if (parser.CurrentEventType != ParseEventType.Scalar || !parser.TryGetScalarAsString(out var key) || key is null)
                throw new YamlSerializerException(parser.CurrentMark, "Custom type deserialization supports only string key");
            parser.Read(); // the key
            switch (key)
            {
                case "id": config.Id = ReadWithAlias(ref parser, anchors, ReadString)!; break;
                case "name": config.Name = ReadWithAlias(ref parser, anchors, ReadString); break;
                case "description": config.Description = ReadWithAlias(ref parser, anchors, ReadString); break;
                case "author": config.Author = ReadWithAlias(ref parser, anchors, ReadString); break;
                case "filter": config.Filter = ReadWithAlias(ref parser, anchors, ReadString); break;
                case "deck": config.Deck = ReadWithAlias(ref parser, anchors, ReadEnum<MotelyDeck>); break;
                case "stake": config.Stake = ReadWithAlias(ref parser, anchors, ReadEnum<MotelyStake>); break;
                case "seeds": config.Seeds = ReadWithAlias(ref parser, anchors, ReadStringList)!; break;
                case "must": config.Must = ReadWithAlias(ref parser, anchors, JamlClauseFormatter.ReadClauseList)!; break;
                case "should": config.Should = ReadWithAlias(ref parser, anchors, JamlClauseFormatter.ReadClauseList)!; break;
                case "mustNot": config.MustNot = ReadWithAlias(ref parser, anchors, JamlClauseFormatter.ReadClauseList)!; break;
                default: parser.SkipCurrentNode(); break;
            }
        }
        parser.ReadWithVerify(ParseEventType.MappingEnd);
        return config;
    }

    /// <summary>VYaml's DeserializeWithAlias: an alias returns the anchored value, an anchor files it.</summary>
    private static T ReadWithAlias<T>(ref YamlParser parser, Dictionary<Anchor, object?> anchors, Reader<T> read)
    {
        if (parser.CurrentEventType == ParseEventType.Alias)
            return ResolveAlias<T>(ref parser, anchors);
        var anchored = parser.TryGetCurrentAnchor(out var anchor);
        var value = read(ref parser, anchors);
        if (anchored)
            anchors[anchor] = value;
        return value;
    }

    /// <summary>The value an alias names, as VYaml's YamlDeserializationContext resolved it.</summary>
    internal static T ResolveAlias<T>(ref YamlParser parser, Dictionary<Anchor, object?> anchors)
    {
        if (!parser.TryGetCurrentAnchor(out var anchor))
            throw new YamlSerializerException(parser.CurrentMark, "An alias with no anchor.");
        parser.Read();
        if (!anchors.TryGetValue(anchor, out var value))
            throw new YamlSerializerException($"Could not found an alias value of anchor: {anchor}");
        return value switch
        {
            null => default!,
            T t => t,
            _ => throw new YamlSerializerException("The alias value is not a type of " + typeof(T).Name),
        };
    }

    /// <summary>VYaml's NullableStringFormatter.</summary>
    private static string? ReadString(ref YamlParser parser, Dictionary<Anchor, object?> anchors)
    {
        if (parser.IsNullScalar())
        {
            parser.Read();
            return null;
        }
        return parser.ReadScalarAsString();
    }

    /// <summary>VYaml's ListFormatter of string.</summary>
    private static List<string>? ReadStringList(ref YamlParser parser, Dictionary<Anchor, object?> anchors)
    {
        if (parser.IsNullScalar())
        {
            parser.Read();
            return null;
        }
        parser.ReadWithVerify(ParseEventType.SequenceStart);
        var list = new List<string>();
        while (!parser.End && parser.CurrentEventType != ParseEventType.SequenceEnd)
            list.Add(ReadWithAlias(ref parser, anchors, ReadString)!);
        parser.ReadWithVerify(ParseEventType.SequenceEnd);
        return list;
    }

    /// <summary>VYaml's EnumAsStringFormatter, without its reflection: the scalar as written or
    /// lowerCamelCased, against the lowerCamelCased member names (so `Red` and `red` both load).</summary>
    private static T ReadEnum<T>(ref YamlParser parser, Dictionary<Anchor, object?> anchors)
        where T : struct, Enum
    {
        var text = parser.ReadScalarAsString();
        if (text is null)
        {
            YamlSerializerException.ThrowInvalidType<T>("null");
            return default;
        }
        if (EnumNames<T>.Values.TryGetValue(text, out var value)
            || EnumNames<T>.Values.TryGetValue(LowerCamel(text), out value))
            return value;
        YamlSerializerException.ThrowInvalidType<T>(text);
        return default;
    }

    private static class EnumNames<T>
        where T : struct, Enum
    {
        public static readonly Dictionary<string, T> Values = Enum.GetValues<T>()
            .ToDictionary(v => LowerCamel(Enum.GetName(v)!));
    }

    private static string LowerCamel(string name)
    {
        var mutator = NamingConventionMutator.Of(VYaml.Annotations.NamingConvention.LowerCamelCase);
        var buffer = new char[name.Length * 2 + 8];
        int written;
        while (!mutator.TryMutate(name.AsSpan(), buffer, out written))
            buffer = new char[buffer.Length * 2];
        return new string(buffer, 0, written);
    }

    /// <summary>
    /// VYaml's generated deserializer skips keys JamlConfig doesn't have, so `boses:` would load
    /// as a filter with no bosses. Walk the root mapping's events first and name the typo and its line.
    /// </summary>
    private static void RejectUnknownRootKeys(byte[] bytes, JamlScalarLines? lines)
    {
        var parser = new YamlParser(new System.Buffers.ReadOnlySequence<byte>(bytes));
        while (parser.CurrentEventType is ParseEventType.Nothing or ParseEventType.StreamStart or ParseEventType.DocumentStart)
            if (!parser.Read()) return;
        if (parser.CurrentEventType != ParseEventType.MappingStart)
            return; // not a mapping: Deserialize reports that
        parser.Read();
        for (int n = 0; parser.CurrentEventType != ParseEventType.MappingEnd; n++)
        {
            int line = parser.CurrentMark.Line; // past the key; in flow style, past much more
            var key = parser.ReadScalarAsString() ?? "";
            if (lines is not null && n < lines.RootKeys.Count && lines.RootKeys[n].Key == key)
                line = lines.RootKeys[n].Line;
            if (Array.FindIndex(JamlConfig.RootKeys, k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) < 0)
                throw new InvalidOperationException(
                    $"JAML line {line}: unknown key '{key}' (root keys are {string.Join(", ", JamlConfig.RootKeys)})");
            Skip(ref parser);
        }
    }

    private static void Skip(ref YamlParser parser)
    {
        int depth = 0;
        do
        {
            switch (parser.CurrentEventType)
            {
                case ParseEventType.SequenceStart or ParseEventType.MappingStart: depth++; break;
                case ParseEventType.SequenceEnd or ParseEventType.MappingEnd: depth--; break;
            }
            parser.Read();
        } while (depth > 0);

    }

    /// <summary>
    /// VYaml's tokenizer and parser end their messages with "at Line: N, Col: C, Idx: I" (line
    /// 1-based, column 0-based) and never show the text. Rewrite that into the loader's own
    /// "JAML line N:" form and quote the offending line, so `joker: *any*` names `*any*`.
    /// Anything without a position (serializer errors) keeps the plain "JAML:" prefix.
    /// </summary>
    private static string DescribeYamlError(string yaml, string message)
    {
        var m = YamlPosition().Match(message);
        if (!m.Success || !int.TryParse(m.Groups["line"].ValueSpan, out var line)
            || !int.TryParse(m.Groups["col"].ValueSpan, out var col))
            return $"JAML: {message}";

        var what = message[..m.Index];
        var lines = yaml.Split('\n');
        var text = line >= 1 && line <= lines.Length ? lines[line - 1].TrimEnd('\r') : "";
        if (text.Trim().Length == 0)
            return $"JAML line {line}: {what} (column {col + 1})"; // past the end: nothing to quote

        const int Window = 80;
        var excerpt = text.Trim();
        if (text.Length > Window)
        {
            var start = Math.Clamp(col - Window / 2, 0, text.Length - Window);
            excerpt = (start > 0 ? "…" : "") + text.Substring(start, Window) + (start + Window < text.Length ? "…" : "");
        }
        return $"JAML line {line}: {what} (column {col + 1}): `{excerpt}`";
    }

    [GeneratedRegex(@" at Line: (?<line>\d+), Col: (?<col>\d+), Idx: \d+$")]
    private static partial Regex YamlPosition();

    public static JamlConfig FromFile(string path) => FromJaml(File.ReadAllText(path));

    public static bool TryLoad(
        string yaml,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    )
    {
        try
        {
            config = FromJaml(yaml);
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
}
