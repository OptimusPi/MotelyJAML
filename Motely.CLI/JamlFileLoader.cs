using System.Diagnostics.CodeAnalysis;
using Motely;
using Motely.Filters.Jaml;

namespace Motely.CLI;

public static class JamlFileLoader
{
    private const string FiltersDirectory = "JamlFilters";
    private static readonly string[] Extensions = [".jaml", ".yaml", ".yml", ".json"];

    /// <summary>
    /// A typed name resolves to the first file that exists: as typed, with an extension, then the
    /// same two under JamlFilters/. A bare name that matches nothing means JamlFilters/name.jaml.
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
        return Path.IsPathRooted(path) || Path.HasExtension(path)
            ? path
            : Path.Combine(FiltersDirectory, path + ".jaml");
    }

    public static bool TryLoadFromPath(
        string path,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    )
    {
        config = null;
        var resolved = ResolvePath(path);
        string text;
        try
        {
            text = File.ReadAllText(resolved);
        }
        catch (Exception ex)
        {
            error = $"Error reading '{resolved}': {ex.Message}";
            return false;
        }

        if (JamlConfigLoader.TryLoad(text, out config, out error))
            return true;
        error = $"{resolved}: {error}";
        return false;
    }

    /// <summary>Writes seeds into the file's top-level seeds: block. The rewritten text must load
    /// before it touches disk. No seeds is a successful no-op.</summary>
    public static bool TrySaveSeeds(string path, IReadOnlyList<string> seeds, out string? error)
    {
        error = null;
        if (seeds.Count == 0)
            return true;

        var resolved = ResolvePath(path);
        try
        {
            if (!MotelyTopSeedSink.TryRewriteAndValidate(File.ReadAllText(resolved), seeds, out var updated, out error))
                return false;
            File.WriteAllText(resolved, updated);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
