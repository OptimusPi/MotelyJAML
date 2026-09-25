using System.Diagnostics.CodeAnalysis;
using System.Text;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

public static class JamlConfigLoader
{
    private static readonly YamlSerializerOptions Options = new()
    {
        Resolver = CompositeResolver.Create(
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

    public static JamlConfig FromJaml(string yaml)
    {
        var config =
            YamlSerializer.Deserialize<JamlConfig>(Encoding.UTF8.GetBytes(yaml), Options)
            ?? throw new InvalidOperationException("JAML: the document is empty.");

        config.Id ??= "";
        config.Seeds ??= [];
        config.Must ??= [];
        config.Should ??= [];
        config.MustNot ??= [];
        return config;
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
