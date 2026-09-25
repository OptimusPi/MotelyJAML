using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Motely.Filters.Jaml;

namespace Motely;

public static class MotelyJamlFile
{
    public const string FiltersDirectory = "JamlFilters";

    public static readonly string[] DocumentExtensions = [".jaml", ".json", ".yaml", ".yml"];

    public static string? TryResolveExisting(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = path.Trim();

        if (File.Exists(path))
            return path;

        if (!Path.HasExtension(path))
        {
            foreach (var ext in DocumentExtensions)
            {
                var withExt = path + ext;
                if (File.Exists(withExt))
                    return withExt;
            }
        }

        if (!Path.IsPathRooted(path))
        {
            var inFilters = Path.Combine(FiltersDirectory, path);
            if (File.Exists(inFilters))
                return inFilters;

            if (!Path.HasExtension(path))
            {
                foreach (var ext in DocumentExtensions)
                {
                    var inFiltersExt = Path.Combine(FiltersDirectory, path + ext);
                    if (File.Exists(inFiltersExt))
                        return inFiltersExt;
                }
            }
        }

        return null;
    }

    public static string ResolvePath(string path)
    {
        var existing = TryResolveExisting(path);
        if (existing is not null)
            return existing;

        var trimmed = (path ?? string.Empty).Trim();
        return !Path.IsPathRooted(trimmed) && !Path.HasExtension(trimmed)
            ? Path.Combine(FiltersDirectory, trimmed + ".jaml")
            : trimmed;
    }

    public static bool TryLoad(
        string? path,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    )
    {
        config = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "No JAML path provided.";
            return false;
        }

        var resolved = ResolvePath(path);

        string content;
        try
        {
            content = File.ReadAllText(resolved);
        }
        catch (System.Exception ex)
        {
            error = $"Error reading JAML file '{resolved}': {ex.Message}";
            return false;
        }

        if (JamlConfigLoader.TryLoad(content, out config, out error))
            return true;

        error = $"{resolved}: {error}";
        return false;
    }

    public static bool TrySaveSeeds(string? path, IReadOnlyList<string> seeds, out string? error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "No JAML path provided.";
            return false;
        }

        if (seeds.Count == 0)
        {
            error = null;
            return true;
        }

        var resolved = ResolvePath(path);

        string original;
        try
        {
            original = File.ReadAllText(resolved);
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!MotelyTopSeedSink.TryRewriteAndValidate(original, seeds, out var updated, out error))
            return false;

        try
        {
            File.WriteAllText(resolved, updated);
            return true;
        }
        catch (System.Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
