using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Motely.Filters.Jaml;

namespace Motely;

/// <summary>
/// The one true JAML file gateway: path resolution, loading, and seeds-block save-back, shared by
/// every front-end (Motely.CLI, Motely.TUI, Motely.DataLake, and any GUI). Before this existed the
/// CLI and TUI each had their own resolver with different rules, so a bare <c>--jaml</c> name could
/// resolve to two different files depending on which app you ran. Now they can never disagree.
///
/// Resolution order for a user-typed value:
///   1. verbatim, if the file exists;
///   2. the value with a <c>.yaml</c>, <c>.yml</c>, or <c>.json</c> extension, if that file exists;
///   3. under <c>JamlFilters/</c> (verbatim, then with those extensions);
///   4. otherwise, for a bare, unrooted, extension-less name, the conventional
///      <c>JamlFilters/&lt;name&gt;.yaml</c> — save-back and "file not found" use this path.
/// </summary>
public static class MotelyJamlFile
{
    /// <summary>The conventional folder bare filter names live in.</summary>
    public const string FiltersDirectory = "JamlFilters";

    /// <summary>Default extension for bare filter names (save-back and conventional path).</summary>
    public const string DefaultDocumentExtension = ".yaml";

    /// <summary>JSON and YAML filter documents — same config bag once loaded.</summary>
    public static readonly string[] DocumentExtensions = [".yaml", ".yml", ".json"];

    /// <summary>True when the path ends with <c>.jaml</c> (case-insensitive).</summary>
    public static bool IsJamlExtension(string path) =>
        Path.GetExtension(path).Equals(".jaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve a user-typed value to an existing file path, or <c>null</c> if none of the candidate
    /// locations exist. Pure lookup — no IO beyond <see cref="File.Exists"/>.
    /// </summary>
    public static string? TryResolveExisting(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = path.Trim();

        if (IsJamlExtension(path))
            return null;

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

    /// <summary>
    /// The canonical on-disk path for a value even when the file does not exist yet: an existing
    /// match if there is one, else the conventional <c>JamlFilters/&lt;name&gt;.yaml</c> for a bare
    /// name, else the value verbatim. Load and save-back both route through here so they always
    /// agree about where the file is.
    /// </summary>
    public static string ResolvePath(string path)
    {
        var existing = TryResolveExisting(path);
        if (existing is not null)
            return existing;

        var trimmed = (path ?? string.Empty).Trim();
        return !Path.IsPathRooted(trimmed) && !Path.HasExtension(trimmed)
            ? Path.Combine(FiltersDirectory, trimmed + DefaultDocumentExtension)
            : trimmed;
    }

    /// <summary>
    /// Resolve, read, and parse a JAML file. On failure <paramref name="error"/> carries the
    /// resolved path so a bare <c>--jaml</c> name still tells you which file it means.
    /// </summary>
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

        path = path.Trim();
        if (IsJamlExtension(path))
        {
            error = $".jaml filter files are not supported; use .yaml, .yml, or .json ({path}).";
            return false;
        }

        var resolved = ResolvePath(path);
        if (IsJamlExtension(resolved))
        {
            error = $".jaml filter files are not supported; use .yaml, .yml, or .json ({resolved}).";
            return false;
        }

        string content;
        try
        {
            content = File.ReadAllText(resolved);
        }
        catch (System.Exception ex)
        {
            error = $"Error reading filter file '{resolved}': {ex.Message}";
            return false;
        }

        var format = LoadFormatForExtension(Path.GetExtension(resolved));
        if (JamlConfigLoader.TryLoad(content, format, out config, out error))
            return true;

        error = $"{resolved}: {error}";
        return false;
    }

    private static JamlLoadFormat LoadFormatForExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".json" => JamlLoadFormat.Json,
            ".yaml" or ".yml" => JamlLoadFormat.Yaml,
            _ => JamlLoadFormat.Auto,
        };

    /// <summary>
    /// Merge <paramref name="seeds"/> into the top-level <c>seeds:</c> block of the JAML file and
    /// write it back, resolving the path exactly like <see cref="TryLoad"/> and validating the
    /// rewritten text before it touches disk (via <see cref="MotelyTopSeedSink"/>). Existing curated
    /// seeds are preserved, in order, ahead of new finds. A no-op that returns success when
    /// <paramref name="seeds"/> is empty, so callers can persist unconditionally.
    /// </summary>
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

        path = path.Trim();
        if (IsJamlExtension(path))
        {
            error = $".jaml filter files are not supported; use .yaml, .yml, or .json ({path}).";
            return false;
        }

        var resolved = ResolvePath(path);
        if (IsJamlExtension(resolved))
        {
            error = $".jaml filter files are not supported; use .yaml, .yml, or .json ({resolved}).";
            return false;
        }

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
