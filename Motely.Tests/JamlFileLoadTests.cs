using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

public sealed class JamlFileLoadTests
{
    [Fact]
    public void TestJamlFiles_AllLoadAndPlan()
    {
        var dir = Path.Join(AppContext.BaseDirectory, "JamlFilters");
        Assert.True(Directory.Exists(dir), $"Test JAML files not in output: {dir}");

        var files = Directory
            .GetFiles(dir, "*.jaml", SearchOption.AllDirectories)
            .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            if (!JamlConfigLoader.TryLoad(content, out var config, out var error))
            {
                failures.Add($"{Path.GetRelativePath(dir, file)}: {error}");
                continue;
            }

            Assert.NotNull(config);
            try
            {
                if (config.HasAnyClauses())
                    _ = JamlSearchBuilder.CreatePlan(config);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetRelativePath(dir, file)}: plan failed: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
