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
        var bytes = Encoding.UTF8.GetBytes(yaml);
        JamlConfig? config;
        try
        {
            RejectUnknownRootKeys(bytes);
            config = YamlSerializer.Deserialize<JamlConfig>(bytes, Options);
        }
        catch (InvalidOperationException)
        {
            throw; // JamlClauseFormatter already says "JAML line N: …"
        }
        catch (Exception ex)
        {
            // VYaml's own parser/serializer exceptions: one type for every host to catch.
            throw new InvalidOperationException($"JAML: {ex.Message}", ex);
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

    /// <summary>
    /// VYaml's generated deserializer skips keys JamlConfig doesn't have, so `boses:` would load
    /// as a filter with no bosses. Walk the root mapping's events first and name the typo and its line.
    /// </summary>
    private static void RejectUnknownRootKeys(byte[] bytes)
    {
        var parser = new YamlParser(new System.Buffers.ReadOnlySequence<byte>(bytes));
        while (parser.CurrentEventType is ParseEventType.Nothing or ParseEventType.StreamStart or ParseEventType.DocumentStart)
            if (!parser.Read()) return;
        if (parser.CurrentEventType != ParseEventType.MappingStart)
            return; // not a mapping: Deserialize reports that
        parser.Read();
        while (parser.CurrentEventType != ParseEventType.MappingEnd)
        {
            int line = parser.CurrentMark.Line;
            var key = parser.ReadScalarAsString() ?? "";
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
