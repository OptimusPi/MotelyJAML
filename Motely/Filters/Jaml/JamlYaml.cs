using System.Text;
using VYaml.Emitter;
using VYaml.Parser;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

/// <summary>
/// VYaml 1.4 entry for JAML. Clause types in AnteCards / AnteFeatures / Events are
/// <c>[YamlObject]</c>; the wire is a discriminator mapping key, so
/// <see cref="IJamlClause"/> goes through one formatter that asks <see cref="JamlSchema"/>.
/// </summary>
internal static class JamlYaml
{
    [ThreadStatic]
    internal static string? SourceText;

    internal static readonly YamlSerializerOptions Options = new()
    {
        Resolver = CompositeResolver.Create(
            [
                new JamlConfigYamlFormatter(),
                new JamlClauseYamlFormatter(),
            ],
            [StandardResolver.Instance]
        ),
    };

    internal static string NormalizeText(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!text.EndsWith('\n'))
            text += "\n";
        return text;
    }
}

internal sealed class JamlConfigYamlFormatter : IYamlFormatter<JamlConfig>
{
    public JamlConfig Deserialize(ref YamlParser parser, YamlDeserializationContext context)
    {
        var text = JamlYaml.SourceText ?? "";
        var node = JamlYamlTree.ReadCurrent(ref parser, text);
        if (node is not JMap root)
            throw new InvalidOperationException("YAML root must be a mapping.");
        return JamlConfigLoader.FromMap(root);
    }

    public void Serialize(ref Utf8YamlEmitter emitter, JamlConfig value, YamlSerializationContext context) =>
        throw new NotSupportedException("JAML load does not serialize.");
}

internal sealed class JamlClauseYamlFormatter : IYamlFormatter<IJamlClause>
{
    public IJamlClause Deserialize(ref YamlParser parser, YamlDeserializationContext context)
    {
        var text = JamlYaml.SourceText ?? "";
        var node = JamlYamlTree.ReadCurrent(ref parser, text);
        if (node is not JMap map)
            throw new JamlSemanticException(
                "Clause list has an entry that is not a clause mapping (e.g. '- joker: Blueprint').",
                node.Span
            );
        return JamlConfigLoader.ParseClause(map);
    }

    public void Serialize(ref Utf8YamlEmitter emitter, IJamlClause value, YamlSerializationContext context) =>
        throw new NotSupportedException("JAML load does not serialize.");
}
