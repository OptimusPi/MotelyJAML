namespace Motely.Tests;

public sealed class JamlConfigWriterTests
{
    [Fact]
    public void ToJaml_RoundTripsEveryTestJamlFile()
    {
        var dir = TestJamlDir();
        var files = Directory
            .GetFiles(dir, "*.jaml", SearchOption.AllDirectories)
            .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(dir, file);
            if (!JamlConfigLoader.TryLoad(File.ReadAllText(file), out var original, out var loadError)
                || original is null)
            {
                failures.Add($"{relative}: load failed: {loadError}");
                continue;
            }

            string written;
            try
            {
                written = JamlConfigLoader.ToJaml(original);
            }
            catch (Exception ex)
            {
                failures.Add($"{relative}: ToJaml threw: {ex.Message}");
                continue;
            }

            if (!JamlConfigLoader.TryLoad(written, out var reloaded, out var error))
            {
                failures.Add($"{relative}: round-tripped JAML failed to reload: {error}\n---\n{written}");
                continue;
            }

            Assert.NotNull(reloaded);
            if (reloaded.Must.Count != original.Must.Count
                || reloaded.Should.Count != original.Should.Count
                || reloaded.MustNot.Count != original.MustNot.Count)
            {
                failures.Add(
                    $"{relative}: clause counts changed on round trip "
                        + $"(must {original.Must.Count}->{reloaded.Must.Count}, "
                        + $"should {original.Should.Count}->{reloaded.Should.Count}, "
                        + $"mustNot {original.MustNot.Count}->{reloaded.MustNot.Count})"
                );
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // Regression: an explicit `sources: {}` means "override with match-nowhere" and is distinct
    // from an absent sources: (use DefaultSources). ToJaml must not collapse the two by dropping
    // an all-default sources block.
    [Fact]
    public void ToJaml_PreservesExplicitEmptySources()
    {
        const string jaml = """
            id: empty-sources
            must:
              - joker: [Blueprint]
                sources: {}
            """;
        var original = JamlConfigLoader.FromJaml(jaml);
        var originalClause = Assert.IsType<JokerClause>(original.Must[0]);
        Assert.NotNull(originalClause.Sources);

        var reloaded = JamlConfigLoader.FromJaml(JamlConfigLoader.ToJaml(original));
        var reloadedClause = Assert.IsType<JokerClause>(reloaded.Must[0]);
        Assert.NotNull(reloadedClause.Sources);
    }

    // Regression: erraticRanks: [...] with an explicit min: must carry that min onto the wrapping
    // OrClause (how many of the listed ranks must appear), not silently reset to 1.
    [Fact]
    public void ErraticRanks_HonorsExplicitMin()
    {
        const string jaml = """
            id: erratic-min
            must:
              - erraticRanks: [Two, Three, Four]
                min: 3
            """;
        var config = JamlConfigLoader.FromJaml(jaml);
        var or = Assert.IsType<Motely.Filters.OrClause>(config.Must[0]);
        Assert.Equal(3, or.Min);
    }

    private static string TestJamlDir()
    {
        var dir = Path.Join(AppContext.BaseDirectory, "JamlFilters");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Test JAML files not in output: {dir}");
        return dir;
    }
}
