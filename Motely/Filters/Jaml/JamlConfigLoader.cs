using System.Diagnostics.CodeAnalysis;
using System.Text;
using VYaml.Parser;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

/// <summary>YAML text → <see cref="JamlConfig"/>. VYaml does the document; <see cref="JamlClauseFormatter"/> does the clauses.</summary>
public static class JamlConfigLoader
{
    private static readonly YamlSerializerOptions Options = new()
    {
        Resolver = CompositeResolver.Create(
            // Every generic formatter JamlConfig needs, spelled out. VYaml's built-in resolver
            // makes these with MakeGenericType at runtime, which NativeAOT / WASM cannot do
            // ("EnumAsStringFormatter<MotelyDeck> is missing native code"). Naming them here
            // makes the compiler emit them.
            new IYamlFormatter[]
            {
                new JamlClauseFormatter(),
                new EnumAsStringFormatter<MotelyDeck>(),
                new EnumAsStringFormatter<MotelyStake>(),
                new ListFormatter<IJamlClause>(),
                new ListFormatter<string>(),
            },
            new IYamlFormatterResolver[] { StandardResolver.Instance }),
    };

    /// <summary>One entry point for every filter file. JSON is a subset of YAML, so .json,
    /// .yaml, .yml and .jaml all go through the same parser.</summary>
    public static JamlConfig FromJaml(string yaml)
    {
        var bytes = Encoding.UTF8.GetBytes(yaml);

        // Validate root-level keys before deserialization
        try
        {
            ValidateRootKeys(bytes);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            // Re-throw non-InvalidOperationException exceptions (e.g., VYaml parser exceptions)
            // as InvalidOperationException for consistent error handling
            throw new InvalidOperationException($"JAML: {ex.Message}", ex);
        }

        var config =
            YamlSerializer.Deserialize<JamlConfig>(bytes, Options)
            ?? throw new InvalidOperationException("JAML: the document is empty.");

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

    private static void ValidateRootKeys(byte[] yamlBytes)
    {
        var sequence = new System.Buffers.ReadOnlySequence<byte>(yamlBytes);
        var parser = new YamlParser(sequence);

        // Read the first event to get started
        if (parser.CurrentEventType is ParseEventType.Nothing)
            parser.Read();

        // Skip document markers if present
        if (parser.CurrentEventType is ParseEventType.StreamStart)
            parser.Read();
        if (parser.CurrentEventType is ParseEventType.DocumentStart)
            parser.Read();

        // The root must be a mapping
        if (parser.CurrentEventType != ParseEventType.MappingStart)
            return; // Not a mapping, will fail during deserialization anyway

        parser.Read();

        // Check all keys in the root mapping
        while (parser.CurrentEventType != ParseEventType.MappingEnd)
        {
            // Read the key - must be a scalar
            if (parser.CurrentEventType != ParseEventType.Scalar)
                throw new InvalidOperationException($"JAML line {parser.CurrentMark.Line}: expected a key");

            var keyLine = parser.CurrentMark.Line;
            var key = parser.ReadScalarAsString() ?? "";

            // Check if this key is allowed (case-insensitive)
            if (!JamlConfig.RootKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"JAML line {keyLine}: unknown key '{key}'");
            }

            // Skip the value - ReadNode reads the value and advances parser
            _ = ReadNode(ref parser);
        }
    }

    private sealed class Node
    {
        public int Line;
        public string? Scalar;
        public List<Node>? Items;
        public List<(string Key, Node Value)>? Map;
    }

    private static Node ReadNode(ref YamlParser parser)
    {
        var node = new Node { Line = parser.CurrentMark.Line };
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
                node.Scalar = parser.IsNullScalar() ? null : parser.ReadScalarAsString();
                if (node.Scalar is null) parser.Read();
                return node;

            case ParseEventType.SequenceStart:
                parser.Read();
                node.Items = [];
                while (parser.CurrentEventType != ParseEventType.SequenceEnd)
                    node.Items.Add(ReadNode(ref parser));
                parser.Read();
                return node;

            case ParseEventType.MappingStart:
                parser.Read();
                node.Map = [];
                while (parser.CurrentEventType != ParseEventType.MappingEnd)
                {
                    var key = parser.ReadScalarAsString() ?? "";
                    node.Map.Add((key, ReadNode(ref parser)));
                }
                parser.Read();
                return node;

            default:
                parser.Read();
                return node;
        }
    }


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
