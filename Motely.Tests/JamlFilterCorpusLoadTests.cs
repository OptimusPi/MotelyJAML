
namespace Motely.Tests;

/// <summary>
/// ADR-001 item 9: every filter in the repo loads through the one loader. Reads the source
/// folders in place (repo YamlFilters/ and Motely.Tests/YamlFilters/), not the copies in bin/,
/// so a file that isn't copied to output still gets checked. All four extensions the loader
/// accepts: .jaml .yaml .yml .json.
/// </summary>
public sealed class MotelyfilterCorpusLoadTests
{
    private static readonly string[] Extensions = [".jaml", ".yaml", ".yml", ".json"];

    private static string TestsDir =>
        Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", ".."));

    private static string RepoDir => Path.GetFullPath(Path.Join(TestsDir, ".."));

    public static TheoryData<string> Folders =>
        new() { Path.Join("YamlFilters"), Path.Join("Motely.Tests", "YamlFilters") };

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
