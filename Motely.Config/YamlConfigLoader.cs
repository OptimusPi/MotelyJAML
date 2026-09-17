using System.Diagnostics.CodeAnalysis;
using System.Text;
using Motely.Filters.Jaml;

namespace Motely.Config;

/// <summary>
/// NativeAOT-safe YAML config entrypoint: file or stream → <see cref="JamlConfig"/>.
/// Uses VYaml (<c>[YamlObject]</c> resolvers + source-generated formatters) and the existing
/// <see cref="JamlConfigLoader"/> grammar — no YamlDotNet, no reflection-based mapping.
/// </summary>
public static class YamlConfigLoader
{
    /// <summary>Parse UTF-8 YAML text into a typed <see cref="JamlConfig"/>.</summary>
    public static JamlConfig Load(string yamlText) => JamlConfigLoader.FromJaml(yamlText);

    /// <summary>Parse UTF-8 YAML bytes into a typed <see cref="JamlConfig"/>.</summary>
    public static JamlConfig Load(ReadOnlySpan<byte> yamlUtf8) =>
        Load(Encoding.UTF8.GetString(yamlUtf8));

    /// <summary>Read a YAML stream (UTF-8) and deserialize to <see cref="JamlConfig"/>.</summary>
    public static JamlConfig Load(Stream yamlStream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(yamlStream);
        using var reader = new StreamReader(
            yamlStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: leaveOpen
        );
        return Load(reader.ReadToEnd());
    }

    /// <summary>Read a YAML file from disk and deserialize to <see cref="JamlConfig"/>.</summary>
    public static JamlConfig LoadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    /// <summary>Try-parse YAML text; semantic errors include line/column when available.</summary>
    public static bool TryLoad(
        string yamlText,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    ) => JamlConfigLoader.TryLoad(yamlText, out config, out error);

    /// <summary>Try-read and parse a YAML file.</summary>
    public static bool TryLoadFile(
        string path,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    )
    {
        config = null;
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = $"Error reading YAML file '{path}': {ex.Message}";
            return false;
        }

        if (!TryLoad(content, out config, out error))
        {
            error = $"{path}: {error}";
            return false;
        }

        return true;
    }
}
