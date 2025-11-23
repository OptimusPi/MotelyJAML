using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Motely.Filters;

namespace Motely;

public static class YamlConfigLoader
{
    public static bool TryLoadFromYaml(string yamlPath, out MotelyJsonConfig? config, out string? error)
    {
        config = null;
        error = null;

        if (!File.Exists(yamlPath))
        {
            error = $"File not found: {yamlPath}";
            return false;
        }

        try
        {
            var yamlContent = File.ReadAllText(yamlPath);

            // Parse YAML to object
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var deserializedConfig = deserializer.Deserialize<MotelyJsonConfig>(yamlContent);

            if (deserializedConfig == null)
            {
                error = "Failed to deserialize YAML - result was null";
                return false;
            }

            deserializedConfig.PostProcess();

            // Validate config
            MotelyJsonConfigValidator.ValidateConfig(deserializedConfig);

            config = deserializedConfig;
            return true;
        }
        catch (Exception ex)
        {
            config = null;
            error = $"Failed to parse YAML: {ex.Message}";
            return false;
        }
    }
}
