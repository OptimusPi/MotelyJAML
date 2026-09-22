using System.Diagnostics.CodeAnalysis;
using System.Text;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

/// <summary>YAML text → <see cref="JamlConfig"/>. VYaml does the document; <see cref="JamlClauseFormatter"/> does the clauses.</summary>
public static class JamlConfigLoader
{
    private static readonly YamlSerializerOptions Options = new()
    {
        Resolver = CompositeResolver.Create(
            new IYamlFormatter[] { new JamlClauseFormatter() },
            new IYamlFormatterResolver[] { StandardResolver.Instance }),
    };

    public static JamlConfig FromJaml(string yaml) =>
        YamlSerializer.Deserialize<JamlConfig>(Encoding.UTF8.GetBytes(yaml), Options);

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
