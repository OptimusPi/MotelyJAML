// The filter-name half of JamlFiles, with no Bootsharp in it: the part a normal .NET test can run.

/// <summary>What happened to an entry under the mounted folder, as Bootsharp.FileSystem reports it.</summary>
public enum JamlFileEvent
{
    Added,
    Removed,
    Moved,
    Modified,
}

/// <summary>
/// Filter names as <see cref="IJamlFiles"/> lists them: <c>sub/filter</c> for <c>/sub/filter.yaml</c>,
/// while a <c>.yml</c> keeps its extension. Any other file under the folder is not a filter.
/// </summary>
public static class JamlFileNames
{
    public static readonly string[] Extensions = [".yaml", ".yml"];

    public static bool IsFilter(string path) =>
        Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>"sub/filter" is "/sub/filter.yaml"; a name with a filter extension keeps it.</summary>
    public static string ToUri(string name)
    {
        string path = name.Replace('\\', '/').TrimStart('/');
        return "/" + (IsFilter(path) ? path : path + ".yaml");
    }

    /// <summary>"/sub/filter.yaml" is "sub/filter"; null for a file that is not a filter.</summary>
    public static string? ToName(string uri)
    {
        string path = uri.TrimStart('/');
        if (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            return path[..^".yaml".Length];
        return IsFilter(path) ? path : null;
    }

    /// <summary>
    /// A folder change as the filter list sees it, or null when it touches no filter. A file moved
    /// to a non-filter name is removed; one moved in from a non-filter name is added.
    /// </summary>
    public static JamlFileChange? Change(JamlFileEvent type, string uri, string? fromUri)
    {
        string? name = ToName(uri);
        string? from = fromUri is null ? null : ToName(fromUri);
        if (name is null && from is null)
            return null;
        string kind = type switch
        {
            JamlFileEvent.Moved when name is null => "removed",
            JamlFileEvent.Moved when from is null => "added",
            JamlFileEvent.Moved => "moved",
            JamlFileEvent.Added => "added",
            JamlFileEvent.Removed => "removed",
            _ => "modified",
        };
        return new JamlFileChange(kind, name ?? from!, kind == "moved" ? from : null);
    }
}

/// <summary>One change to the folder's filter list.</summary>
/// <param name="Kind">added, removed, modified or moved.</param>
/// <param name="Name">The filter's name, as <see cref="IJamlFiles.Load"/> takes it.</param>
/// <param name="FromName">The previous name when Kind is moved, otherwise null.</param>
public readonly record struct JamlFileChange(string Kind, string Name, string? FromName);
