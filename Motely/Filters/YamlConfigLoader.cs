using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely.Filters;

/// <summary>
/// YAML text → <see cref="JamlConfig"/> through YamlDotNet's static deserializer. JSON is YAML,
/// so a JSON document loads through the same path.
/// </summary>
public static class YamlConfigLoader
{
    private static readonly IDeserializer Deserializer = CreateDeserializer();

    private static IDeserializer CreateDeserializer()
    {
        var builder = new StaticDeserializerBuilder(new JamlYamlContext())
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithCaseInsensitivePropertyMatching()
            .WithNodeDeserializer(new ClauseTypeDiscriminatingNodeDeserializer());
        foreach (var converter in YamlClauseBinding.Converters)
            builder = builder.WithTypeConverter(converter);
        return builder.Build();
    }

    public static JamlConfig FromYaml(string text)
    {
        try
        {
            return Deserializer.Deserialize<JamlConfig?>(text)
                ?? throw new InvalidOperationException("YAML: the document is empty.");
        }
        catch (YamlException ex)
        {
            var inner = ex;
            while (inner.InnerException is YamlException next)
                inner = next;
            throw new InvalidOperationException(
                $"YAML line {inner.Start.Line}: {inner.InnerException?.Message ?? inner.Message}", ex);
        }
    }

    public static JamlConfig FromFile(string path) => FromYaml(File.ReadAllText(path));

    /// <summary>Every key that starts a YAML filter clause (<c>joker</c>, <c>legendaryJoker</c>, <c>or</c>, …).</summary>
    public static IReadOnlyCollection<string> ClauseKeys => YamlClauseBinding.ClauseKeys;

    public static bool TryLoad(string text, [NotNullWhen(true)] out JamlConfig? config, out string? error)
    {
        try
        {
            config = FromYaml(text);
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

[YamlStaticContext]
[YamlSerializable(typeof(IMotelyClause))]
public partial class JamlYamlContext : YamlDotNet.Serialization.StaticContext;
