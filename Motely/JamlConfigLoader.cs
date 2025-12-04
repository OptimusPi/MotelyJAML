using Motely.Filters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely;

/// <summary>
/// JAML (Joker Ante Markup Language) configuration loader.
/// JAML is a YAML-based format specifically designed for Balatro seed filter configuration.
/// </summary>
public static class JamlConfigLoader
{
    /// <summary>
    /// Try to load a MotelyJsonConfig from a JAML file.
    /// </summary>
    public static bool TryLoadFromJaml(
        string jamlPath,
        out MotelyJsonConfig? config,
        out string? error
    )
    {
        config = null;
        error = null;

        if (!File.Exists(jamlPath))
        {
            error = $"File not found: {jamlPath}";
            return false;
        }

        try
        {
            var jamlContent = File.ReadAllText(jamlPath);
            return TryLoadFromJamlString(jamlContent, out config, out error);
        }
        catch (Exception ex)
        {
            config = null;
            error = $"Failed to read JAML file: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Try to load a MotelyJsonConfig from a JAML string.
    /// </summary>
    public static bool TryLoadFromJamlString(
        string jamlContent,
        out MotelyJsonConfig? config,
        out string? error
    )
    {
        config = null;
        error = null;

        try
        {
            // Pre-process JAML to support type-as-key syntax (but let YAML handle arrays naturally)
            jamlContent = PreProcessJaml(jamlContent);

            // Parse JAML (YAML-based) to object
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var deserializedConfig = deserializer.Deserialize<MotelyJsonConfig>(jamlContent);

            if (deserializedConfig == null)
            {
                error = "Failed to deserialize JAML - result was null";
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
            error = $"Failed to parse JAML: {ex.Message}";
            return false;
        }
    }

    private static string PreProcessJaml(string jamlContent)
    {
        // Support clean type-as-key syntax: "joker: Blueprint" instead of "type: Joker, value: Blueprint"
        // Support plural values arrays: "jokers: [Blueprint, Brainstorm]" expands to multiple clauses
        var typeKeys = new[] { "joker", "soulJoker", "souljoker", "voucher", "tarot", "tarotCard", "tarotcard",
            "planet", "planetCard", "planetcard", "spectral", "spectralCard", "spectralcard",
            "playingCard", "playingcard", "standardCard", "standardcard", "boss", "tag", "smallBlindTag", "bigBlindTag", "and", "or" };
        
        var pluralTypeKeys = new[] { "jokers", "soulJokers", "vouchers", "tarots", "tarotCards", 
            "planets", "planetCards", "spectrals", "spectralCards", "playingCards", "standardCards", "bosses", "tags" };

        var lines = jamlContent.Split('\n');
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            bool matched = false;

            // Check if line has type-as-key pattern (e.g., "  - joker: Blueprint")
            if (trimmed.StartsWith("- "))
            {
                // Handle plural arrays (jokers: [Blueprint, Brainstorm])
                foreach (var pluralKey in pluralTypeKeys)
                {
                    var pattern = $"- {pluralKey}:";
                    if (trimmed.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        var indent = line.Substring(0, line.IndexOf('-'));
                        var singularType = GetSingularTypeName(pluralKey);
                        var normalizedType = NormalizeTypeName(singularType);
                        var arrayContent = trimmed.Substring(pattern.Length).Trim();
                        
                        // Convert jokers: [Blueprint, Brainstorm] to type: Joker + values: [Blueprint, Brainstorm]
                        result.AppendLine($"{indent}- type: {normalizedType}");
                        result.AppendLine($"{indent}  values: {arrayContent}");
                        matched = true;
                        break;
                    }
                }
                
                // Then check for singular type-as-key patterns
                if (!matched)
                {
                    foreach (var typeKey in typeKeys)
                    {
                        var pattern = $"- {typeKey}:";
                        if (trimmed.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            var indent = line.Substring(0, line.IndexOf('-'));
                            var value = trimmed.Substring(pattern.Length).Trim();

                            // Convert to standard format
                            var normalizedType = NormalizeTypeName(typeKey);
                            result.AppendLine($"{indent}- type: {normalizedType}");
                            result.AppendLine($"{indent}  value: {value}");
                            matched = true;
                            break; // Found match, stop checking other typeKeys
                        }
                    }
                }
            }

            // Only append original line if no type-as-key pattern was found
            if (!matched)
            {
                result.AppendLine(line);
            }
        }

        return result.ToString();
    }

    private static string GetSingularTypeName(string pluralKey)
    {
        return pluralKey.ToLowerInvariant() switch
        {
            "jokers" => "joker",
            "souljokers" => "soulJoker",
            "vouchers" => "voucher", 
            "tarots" or "tarotcards" => "tarot",
            "planets" or "planetcards" => "planet",
            "spectrals" or "spectralcards" => "spectral",
            "playingcards" or "standardcards" => "playingCard",
            "bosses" => "boss",
            "tags" => "tag",
            _ => pluralKey.TrimEnd('s') // fallback: remove 's'
        };
    }

    private static string NormalizeTypeName(string typeKey)
    {
        return typeKey.ToLowerInvariant() switch
        {
            "joker" => "Joker",
            "souljoker" => "SoulJoker",
            "voucher" => "Voucher",
            "tarot" or "tarotcard" => "TarotCard",
            "planet" or "planetcard" => "PlanetCard",
            "spectral" or "spectralcard" => "SpectralCard",
            "playingcard" or "standardcard" => "PlayingCard",
            "boss" => "Boss",
            "smallblindtag" => "SmallBlindTag",
            "bigblindtag" => "BigBlindTag",
            "and" => "And",
            "or" => "Or",
            _ => typeKey
        };
    }
}
