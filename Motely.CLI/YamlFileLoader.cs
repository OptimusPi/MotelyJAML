using System.Diagnostics.CodeAnalysis;

namespace Motely.CLI;

/// <summary>Filter files on disk: JAML and YAML (.jaml/.yaml/.yml), and JSON, which is YAML 1.2.</summary>
public static class YamlFileLoader
{
    private const string FiltersDirectory = "Motelyfilters";
    private static readonly string[] Extensions = [".jaml", ".yaml", ".yml", ".json"];

    /// <summary>
    /// A typed name resolves to the first file that exists: as typed, with an extension, then the
    /// same two under Motelyfilters/. Nothing is guessed past that: a miss stays the path as typed.
    /// </summary>
    public static string ResolvePath(string path)
    {
        path = path.Trim();
        string[] dirs = Path.IsPathRooted(path) ? [""] : ["", FiltersDirectory];
        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate))
                return candidate;
            if (!Path.HasExtension(path))
                foreach (var ext in Extensions)
                    if (File.Exists(candidate + ext))
                        return candidate + ext;
        }
        return path;
    }

    public static bool TryLoad(
        string path,
        [NotNullWhen(true)] out JamlConfig? config,
        [NotNullWhen(true)] out string? resolved,
        [NotNullWhen(false)] out string? error
    )
    {
        config = null;
        resolved = ResolvePath(path);
        if (!Extensions.Contains(Path.GetExtension(resolved), StringComparer.OrdinalIgnoreCase))
        {
            error = $"'{resolved}' is not a filter file (.jaml, .yaml, .yml, .json).";
            resolved = null;
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(resolved);
        }
        catch (Exception ex)
        {
            error = $"'{resolved}': {ex.Message}";
            resolved = null;
            return false;
        }

        if (YamlConfigLoader.TryLoad(text, out config, out var loadError))
        {
            error = null;
            return true;
        }
        error = $"{resolved}: {loadError}";
        resolved = null;
        return false;
    }

    /// <summary>
    /// Merges seeds into the file's top-level seeds: block (existing seeds stay first). The
    /// rewritten text must load before it touches disk. No seeds is a successful no-op.
    /// </summary>
    public static bool TrySaveSeeds(string resolvedPath, IReadOnlyList<string> seeds, out string? error)
    {
        error = null;
        if (seeds.Count == 0)
            return true;
        try
        {
            if (!MotelyTopSeedSink.TryRewriteAndValidate(File.ReadAllText(resolvedPath), seeds, out var updated, out error))
                return false;
            File.WriteAllText(resolvedPath, updated);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
