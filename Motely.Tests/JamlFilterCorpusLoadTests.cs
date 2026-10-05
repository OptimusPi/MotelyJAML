using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// ADR-001 item 9: every filter in the repo loads through the one loader. Reads the source
/// folders in place (repo JamlFilters/ and Motely.Tests/JamlFilters/), not the copies in bin/,
/// so a file that isn't copied to output still gets checked. All four extensions the loader
/// accepts: .jaml .yaml .yml .json.
/// </summary>
public sealed class JamlFilterCorpusLoadTests
{
    private static readonly string[] Extensions = [".jaml", ".yaml", ".yml", ".json"];

    private static string TestsDir =>
        Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", ".."));

    private static string RepoDir => Path.GetFullPath(Path.Join(TestsDir, ".."));

    public static TheoryData<string> Folders =>
        new() { Path.Join("JamlFilters"), Path.Join("Motely.Tests", "JamlFilters") };

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryFilterFile_LoadsFromFile(string folder)
    {
        var dir = Path.Join(RepoDir, folder);
        Assert.True(Directory.Exists(dir), $"filter folder missing: {dir}");

        var files = Directory
            .GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(static f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(static f => f, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                _ = JamlConfigLoader.FromFile(file);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.Join(folder, Path.GetRelativePath(dir, file))}: {ex.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {files.Length} filters failed to load:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures)
        );
    }

    /// <summary>
    /// The loader's error lines come from JamlScalarLines, which walks the source beside the
    /// parser. It gives up (and the loader falls back to marks) on anything it can't follow, so
    /// check it follows every real filter, and that each plain scalar is on the line it names.
    /// </summary>
    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryFilterFile_ScalarLinesWalkTheWholeDocument(string folder)
    {
        var dir = Path.Join(RepoDir, folder);
        var files = Directory
            .GetFiles(dir, "*", SearchOption.AllDirectories)
            .Where(static f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(static f => f, StringComparer.Ordinal)
            .ToArray();

        var failures = new List<string>();
        int checkedScalars = 0;
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.Join(folder, Path.GetRelativePath(dir, file));
            var walk = JamlScalarLines.TryBuild(System.Text.Encoding.UTF8.GetBytes(text));
            if (walk is null)
            {
                failures.Add($"{name}: walk gave up");
                continue;
            }
            var lines = text.Split('\n');
            for (int i = 0; i < walk.Lines.Count; i++)
            {
                var value = walk.Values[i];
                if (string.IsNullOrEmpty(value) || value.Contains(' ') || value.Contains('\n'))
                    continue; // made-up empties and folded text: no single line to find them on
                var line = walk.Lines[i];
                if (line < 1 || line > lines.Length || !lines[line - 1].Contains(value, StringComparison.Ordinal))
                    failures.Add($"{name}: `{value}` filed under line {line}");
                else
                    checkedScalars++;
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(checkedScalars > 0);
    }
}
