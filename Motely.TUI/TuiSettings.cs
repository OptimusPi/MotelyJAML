using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motely.TUI;

public enum SearchMode
{
    Sequential,
    Random,
    Palindrome,
    Psychosis,
    Keyword,
    FileSource,
}

/// <summary>
/// Runtime settings for TUI searches and API server
/// </summary>
public static class TuiSettings
{
    private static readonly SettingsService _settingsService = new SettingsService("tui.json");

    // Thread settings
    public static int ThreadCount { get; set; } = Environment.ProcessorCount;

    // Batch settings
    public static int BatchCharacterCount { get; set; } = 2;

    // API Server settings
    public static string ApiServerHost { get; set; } = "localhost";
    public static int ApiServerPort { get; set; } = 3141;

    // Default search IO settings
    public static SearchMode SearchMode { get; set; } = SearchMode.Sequential;
    public static string DefaultSource { get; set; } = string.Empty;
    public static string DefaultSink { get; set; } = string.Empty;
    public static string Keywords { get; set; } = string.Empty;
    public static string PaddingChars { get; set; } = string.Empty;
    public static int RandomSeedCount { get; set; } = 1000000;

    /// <summary>Optional Motely search-index bounds for sequential mode (null = full 8-char space).</summary>
    public static long? SequentialStartSeedSearchIndex { get; set; }

    public static long? SequentialStopSeedSearchIndex { get; set; }

    // Distributed worker settings
    public static string WorkerPoolUrl { get; set; } = "https://www.seedfinder.app";
    public static int WorkerThreads { get; set; } = Environment.ProcessorCount;

    // DuckLake (results data lake) root directory — resolved relative to current directory.
    public static string DataLakePath { get; set; } = "seeds";

    /// <summary>
    /// Load settings from tui.json (if exists)
    /// </summary>
    public static void Load() => _settingsService.LoadSettings();

    /// <summary>
    /// Save settings to tui.json
    /// </summary>
    public static void Save() => _settingsService.SaveSettings();

    /// <summary>
    /// Reset all settings to defaults
    /// </summary>
    public static void ResetToDefaults()
    {
        ThreadCount = Environment.ProcessorCount;
        BatchCharacterCount = 2;
        ApiServerHost = "localhost";
        ApiServerPort = 3141;
        SearchMode = SearchMode.Sequential;
        DefaultSource = string.Empty;
        DefaultSink = string.Empty;
        Keywords = string.Empty;
        PaddingChars = string.Empty;
        RandomSeedCount = 1000000;
        SequentialStartSeedSearchIndex = null;
        SequentialStopSeedSearchIndex = null;
        Save();
    }
}

public class SettingsService
{
    private readonly string _fileName;

    public SettingsService(string fileName)
    {
        _fileName = fileName;
    }

    public void LoadSettings()
    {
        try
        {
            if (string.IsNullOrEmpty(_fileName) || !File.Exists(_fileName))
                return;

            var json = File.ReadAllText(_fileName);
            var settings = JsonSerializer.Deserialize<PersistedSettings>(json);

            if (settings != null)
            {
                TuiSettings.ThreadCount = settings.ThreadCount ?? Environment.ProcessorCount;
                TuiSettings.BatchCharacterCount = settings.BatchCharacterCount ?? 2;
                TuiSettings.ApiServerHost = settings.ApiServerHost ?? "localhost";
                TuiSettings.ApiServerPort = settings.ApiServerPort ?? 3141;
                TuiSettings.SearchMode = settings.SearchMode ?? SearchMode.Sequential;
                TuiSettings.DefaultSource = settings.DefaultSource ?? string.Empty;
                TuiSettings.DefaultSink = settings.DefaultSink ?? string.Empty;
                TuiSettings.Keywords = settings.Keywords ?? string.Empty;
                TuiSettings.PaddingChars = settings.PaddingChars ?? string.Empty;
                TuiSettings.RandomSeedCount = settings.RandomSeedCount ?? 1000000;
                TuiSettings.SequentialStartSeedSearchIndex =
                    settings.SequentialStartSeedSearchIndex;
                TuiSettings.SequentialStopSeedSearchIndex = settings.SequentialStopSeedSearchIndex;
                TuiSettings.WorkerPoolUrl = settings.WorkerPoolUrl ?? "https://www.seedfinder.app";
                TuiSettings.WorkerThreads = settings.WorkerThreads ?? Environment.ProcessorCount;
                TuiSettings.DataLakePath = settings.DataLakePath ?? "seeds";
            }
        }
        catch (Exception)
        {
            // If load fails, just use defaults; never throw
        }
    }

    public void SaveSettings()
    {
        try
        {
            var settings = new PersistedSettings
            {
                ThreadCount = TuiSettings.ThreadCount,
                BatchCharacterCount = TuiSettings.BatchCharacterCount,
                ApiServerHost = TuiSettings.ApiServerHost,
                ApiServerPort = TuiSettings.ApiServerPort,
                SearchMode = TuiSettings.SearchMode,
                DefaultSource = TuiSettings.DefaultSource,
                DefaultSink = TuiSettings.DefaultSink,
                Keywords = TuiSettings.Keywords,
                PaddingChars = TuiSettings.PaddingChars,
                RandomSeedCount = TuiSettings.RandomSeedCount,
                SequentialStartSeedSearchIndex = TuiSettings.SequentialStartSeedSearchIndex,
                SequentialStopSeedSearchIndex = TuiSettings.SequentialStopSeedSearchIndex,
                WorkerPoolUrl = TuiSettings.WorkerPoolUrl,
                WorkerThreads = TuiSettings.WorkerThreads,
                DataLakePath = TuiSettings.DataLakePath,
            };

            var options = new JsonSerializerOptions { WriteIndented = true };

            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(_fileName, json);
        }
        catch
        {
            // Silently fail if save fails
        }
    }

    private class PersistedSettings
    {
        public int? ThreadCount { get; set; }
        public int? BatchCharacterCount { get; set; }
        public string? ApiServerHost { get; set; }
        public int? ApiServerPort { get; set; }
        public SearchMode? SearchMode { get; set; }
        public string? DefaultSource { get; set; }
        public string? DefaultSink { get; set; }
        public string? Keywords { get; set; }
        public string? PaddingChars { get; set; }
        public int? RandomSeedCount { get; set; }
        public long? SequentialStartSeedSearchIndex { get; set; }
        public long? SequentialStopSeedSearchIndex { get; set; }
        public string? WorkerPoolUrl { get; set; }
        public int? WorkerThreads { get; set; }
        public string? DataLakePath { get; set; }
    }
}
