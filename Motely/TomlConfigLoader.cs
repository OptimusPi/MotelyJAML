using System.Text.Json;
using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Model;
using Motely.Filters;

namespace Motely;

public static class TomlConfigLoader
{
    public static bool TryLoadFromToml(string tomlPath, out MotelyJsonConfig? config, out string? error)
    {
        config = null;
        error = null;

        if (!File.Exists(tomlPath))
        {
            error = $"File not found: {tomlPath}";
            return false;
        }

        try
        {
            var tomlContent = File.ReadAllText(tomlPath);

            // Parse TOML to TomlTable
            var tomlTable = Toml.ToModel(tomlContent);

            // Serialize TomlTable to JSON string
            var jsonString = JsonSerializer.Serialize(tomlTable, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            // Deserialize JSON to MotelyJsonConfig
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            };

            var deserializedConfig = JsonSerializer.Deserialize<MotelyJsonConfig>(jsonString, options);
            if (deserializedConfig == null)
            {
                error = "Failed to deserialize TOML - result was null";
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
            error = $"Failed to parse TOML: {ex.Message}";
            return false;
        }
    }
}
