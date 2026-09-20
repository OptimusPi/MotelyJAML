using System.Diagnostics.CodeAnalysis;
using System.Text;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

/// <summary>
/// YAML text → <see cref="JamlConfig"/>. The types are <c>[YamlObject]</c>, so VYaml's source
/// generator is the loader: no reflection, no hand-rolled tree, nothing to keep in sync.
/// </summary>
public static class JamlConfigLoader
{
    public static JamlConfig FromJaml(string yaml) =>
        YamlSerializer.Deserialize<JamlConfig>(Encoding.UTF8.GetBytes(yaml));

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
