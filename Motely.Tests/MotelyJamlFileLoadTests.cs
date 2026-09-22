using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

public sealed class MotelyJamlFileLoadTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void TryResolveExisting_BareName_PrefersYamlUnderJamlFilters()
    {
        var cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(RepoRoot);
            var resolved = MotelyJamlFile.TryResolveExisting("always-pass");
            Assert.NotNull(resolved);
            Assert.EndsWith("always-pass.yaml", resolved, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(resolved));
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }
    }

    [Fact]
    public void TryLoad_AlwaysPassJson_ByBareName()
    {
        var cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(RepoRoot);

            Assert.True(
                MotelyJamlFile.TryLoad("always-pass.json", out var config, out var error),
                error
            );
            Assert.Equal("always-pass-json", config!.Name);
            AssertSingleMustIsAnyJoker(config);
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }
    }

    [Fact]
    public void TryLoad_YamlFixture_OnDisk()
    {
        var cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(RepoRoot);

            Assert.True(
                MotelyJamlFile.TryLoad("always-pass.yaml", out var config, out var error),
                error
            );
            Assert.Equal("always-pass-yaml", config!.Name);
            AssertSingleMustIsAnyJoker(config);
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }
    }

    [Fact]
    public void TryLoad_JamlExtension_IsRejected()
    {
        var cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(RepoRoot);

            Assert.False(MotelyJamlFile.TryLoad("AlwaysPass.jaml", out _, out var bareError));
            Assert.Contains(".jaml", bareError, StringComparison.OrdinalIgnoreCase);

            var onDisk = Path.Combine("JamlFilters", "AlwaysPass.jaml");
            Assert.True(File.Exists(onDisk), onDisk);
            Assert.False(MotelyJamlFile.TryLoad(onDisk, out _, out var pathError));
            Assert.Contains(".jaml", pathError, StringComparison.OrdinalIgnoreCase);

            Assert.Null(MotelyJamlFile.TryResolveExisting("AlwaysPass.jaml"));
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
        }
    }

    [Fact]
    public void ResolvePath_BareName_DefaultsToYaml()
    {
        Assert.Equal(
            Path.Combine("JamlFilters", "my-filter.yaml"),
            MotelyJamlFile.ResolvePath("my-filter")
        );
    }

    [Fact]
    public void DocumentExtensions_ExcludesJaml()
    {
        Assert.DoesNotContain(".jaml", MotelyJamlFile.DocumentExtensions);
        Assert.Equal(".yaml", MotelyJamlFile.DefaultDocumentExtension);
    }

    private static void AssertSingleMustIsAnyJoker(JamlConfig config)
    {
        var clause = Assert.Single(config.Must);
        var joker = Assert.IsType<JokerClause>(clause);
        Assert.Empty(joker.Jokers);
    }
}
