
namespace Motely.Tests;

/// <summary>
/// ADR-001 item 9: every test fixture filter loads through the one loader. Reads
/// Motely.Tests/YamlFilters/ in place, not the copies in bin/, so a file that isn't copied to
/// output still gets checked. All four extensions the loader accepts: .jaml .yaml .yml .json.
/// The repo-root YamlFilters/ is the user's own filter folder and is not a test input.
/// </summary>
public sealed class MotelyfilterCorpusLoadTests
{
    private static readonly string[] Extensions = [".jaml", ".yaml", ".yml", ".json"];

    private static string TestsDir =>
        Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", ".."));

    [Fact]
    public void EveryFilterFile_LoadsFromFile()
    {
        const string folder = "YamlFilters";
        var dir = Path.Join(TestsDir, folder);
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
                _ = YamlConfigLoader.FromFile(file);
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
}
