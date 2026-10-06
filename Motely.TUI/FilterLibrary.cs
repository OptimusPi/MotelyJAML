namespace Motely.TUI;

public sealed record FilterLibraryEntry(string Name, string Format, string FullPath)
{
    public string DisplayName => $"{Name}.{Format}";
}

public static class FilterLibrary
{
    public static string WorkingDirectory => Directory.GetCurrentDirectory();

    public static string JamlDirectory => Path.Combine(WorkingDirectory, "JamlFilters");

    public static IReadOnlyList<FilterLibraryEntry> DiscoverLocalFilters()
    {
        EnsureDirectories();

        var filters = new List<FilterLibraryEntry>();

        foreach (
            var file in Directory.GetFiles(JamlDirectory, "*.jaml", SearchOption.TopDirectoryOnly)
        )
            filters.Add(
                new FilterLibraryEntry(Path.GetFileNameWithoutExtension(file), "jaml", file)
            );

        return filters
            .OrderBy(static filter => filter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string SaveJamlFilter(string fileNameWithoutExtension, string content) =>
        SaveFilterInternal(fileNameWithoutExtension, content, JamlDirectory, "jaml");

    private static string SaveFilterInternal(
        string fileNameWithoutExtension,
        string content,
        string directory,
        string extension
    )
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            throw new ArgumentException(
                "Filter name is required.",
                nameof(fileNameWithoutExtension)
            );

        EnsureDirectories();

        // Strip any caller-supplied extension before sanitizing so "foo.jaml" → "foo".
        var rawName = Path.GetFileNameWithoutExtension(fileNameWithoutExtension.Trim());

        var sanitizedName = string.Concat(
            rawName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
        );

        if (string.IsNullOrWhiteSpace(sanitizedName))
            throw new InvalidOperationException("Filter name produced an empty file name.");

        var filePath = Path.Combine(directory, $"{sanitizedName}.{extension}");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(JamlDirectory);
    }
}
