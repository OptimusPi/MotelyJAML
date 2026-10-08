using System.Text;
using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// The loader reads YAML by recursion, so nesting is bounded: hostile input is a load error, not a
/// stack overflow that takes the process down. The bound is the 64 levels filters always had.
/// </summary>
public class JamlLoaderDepthTests
{
    private static string NestedAnds(int count)
    {
        var yaml = new StringBuilder("must:\n");
        string indent = "  ";
        for (int i = 0; i < count; i++, indent += "    ")
            yaml.Append(indent).Append("- and:\n");
        return yaml.Append(indent).Append("- joker: Blueprint\n").ToString();
    }

    [Fact]
    public void HostileNesting_IsALoadError()
    {
        var ok = YamlConfigLoader.TryLoad(
            "seeds: " + new string('[', 100_000) + new string(']', 100_000),
            out _,
            out var error
        );

        Assert.False(ok);
        Assert.Contains("YAML line 1", error);
        Assert.Contains("nests deeper than 64 levels", error);
    }

    [Fact]
    public void ThirtyNestedAnds_StillLoad()
    {
        Assert.True(YamlConfigLoader.TryLoad(NestedAnds(30), out _, out var error), error);
        Assert.False(YamlConfigLoader.TryLoad(NestedAnds(31), out _, out _));
    }
}
