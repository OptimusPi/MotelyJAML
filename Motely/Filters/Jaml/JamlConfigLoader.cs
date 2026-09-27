using System.Diagnostics.CodeAnalysis;
using System.Text;
using VYaml.Emitter;
using VYaml.Parser;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

/// <summary>YAML text → <see cref="JamlConfig"/>. VYaml does the document; <see cref="JamlClauseFormatter"/> does the clauses.</summary>
public static partial class JamlConfigLoader
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
        // Validate root-level keys before deserializing
        ValidateRootKeys(yaml);

        var config =
            YamlSerializer.Deserialize<JamlConfig>(Encoding.UTF8.GetBytes(yaml), Options)
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

    private static void ValidateRootKeys(string yaml)
    {
        var bytes = Encoding.UTF8.GetBytes(yaml);
        var sequence = new System.Buffers.ReadOnlySequence<byte>(bytes);
        var parser = new YamlParser(sequence);
        var keySet = new HashSet<string>(JamlConfig.RootKeys, StringComparer.OrdinalIgnoreCase);

        // Skip to the first document mapping
        while (parser.CurrentEventType is ParseEventType.StreamStart or ParseEventType.DocumentStart)
        {
            parser.Read();
        }

        if (parser.CurrentEventType == ParseEventType.MappingStart)
        {
            parser.Read();
            while (parser.CurrentEventType != ParseEventType.MappingEnd)
            {
                var keyLine = parser.CurrentMark.Line;
                var key = parser.ReadScalarAsString() ?? "";

                // Validate the key against allowed root keys
                if (!keySet.Contains(key))
                    throw new InvalidOperationException($"JAML line {keyLine}: unknown key '{key}' on JamlConfig");

                // Skip the value to move to the next key-value pair
                parser.Read();
                SkipValue(ref parser);
            }
        }
    }

    private static void SkipValue(ref YamlParser parser)
    {
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
                parser.Read();
                break;
            case ParseEventType.SequenceStart:
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.SequenceEnd)
                    SkipValue(ref parser);
                parser.Read();
                break;
            case ParseEventType.MappingStart:
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.MappingEnd)
                {
                    parser.Read(); // skip key
                    SkipValue(ref parser); // skip value
                }
                parser.Read();
                break;
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
