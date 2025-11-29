using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motely.TUI;

/// <summary>
/// Runtime settings for TUI searches and API server
/// </summary>
public static class TuiSettings
{
    private const string SettingsFileName = "tui.json";

    // Thread settings
    public static int ThreadCount { get; set; } = Environment.ProcessorCount;

    // Batch settings
    public static int BatchCharacterCount { get; set; } = 2;

    // API Server settings
    public static string ApiServerHost { get; set; } = "localhost";
    public static int ApiServerPort { get; set; } = 3141;

    // Secret settings (in-memory only, not persisted)
    public static bool CrudeSeedsEnabled { get; set; } = false;

    /// <summary>
    /// Load settings from tui.json (if exists)
    /// </summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsFileName))
                return;

            var json = File.ReadAllText(SettingsFileName);
            var settings = JsonSerializer.Deserialize<PersistedSettings>(json);

            if (settings != null)
            {
                ThreadCount = settings.ThreadCount ?? Environment.ProcessorCount;
                BatchCharacterCount = settings.BatchCharacterCount ?? 2;
                ApiServerHost = settings.ApiServerHost ?? "localhost";
                ApiServerPort = settings.ApiServerPort ?? 3141;
            }
        }
        catch
        {
            // If load fails, just use defaults
        }
    }

    /// <summary>
    /// Save settings to tui.json
    /// </summary>
    public static void Save()
    {
        try
        {
            var settings = new PersistedSettings
            {
                ThreadCount = ThreadCount,
                BatchCharacterCount = BatchCharacterCount,
                ApiServerHost = ApiServerHost,
                ApiServerPort = ApiServerPort,
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(SettingsFileName, json);
        }
        catch
        {
            // Silently fail if save fails
        }
    }

    /// <summary>
    /// Reset all settings to defaults
    /// </summary>
    public static void ResetToDefaults()
    {
        ThreadCount = Environment.ProcessorCount;
        BatchCharacterCount = 2;
        ApiServerHost = "localhost";
        ApiServerPort = 3141;
        Save();
    }

    private class PersistedSettings
    {
        [JsonPropertyName("threadCount")]
        public int? ThreadCount { get; set; }

        [JsonPropertyName("batchCharacterCount")]
        public int? BatchCharacterCount { get; set; }

        [JsonPropertyName("apiServerHost")]
        public string? ApiServerHost { get; set; }

        [JsonPropertyName("apiServerPort")]
        public int? ApiServerPort { get; set; }
    }
}
