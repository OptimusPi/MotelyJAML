using System.Text;
using Motely.Config;
using Motely.Filters.Jaml;

namespace Motely.Tests;

public sealed class YamlConfigLoaderTests
{
    private const string MinimalYaml =
        """
        id: yaml-config-loader-test
        deck: Red
        stake: White
        must:
          - joker: Blueprint
            antes: [1]
        """;

    [Fact]
    public void LoadFromString_ParsesTypedConfig()
    {
        var config = YamlConfigLoader.Load(MinimalYaml);
        Assert.Equal("yaml-config-loader-test", config.Id);
        Assert.Single(config.Must);
    }

    [Fact]
    public void LoadFromStream_ParsesTypedConfig()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(MinimalYaml));
        var config = YamlConfigLoader.Load(stream);
        Assert.Equal("yaml-config-loader-test", config.Id);
    }

    [Fact]
    public void LoadFromBytes_ParsesTypedConfig()
    {
        ReadOnlySpan<byte> bytes = "id: bytes-test\ndeck: Red\nstake: White\n"u8;
        var config = YamlConfigLoader.Load(bytes);
        Assert.Equal("bytes-test", config.Id);
    }

    [Fact]
    public void LoadFile_UsesProjectFixture()
    {
        var fixture = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Motely.ConfigAot.Smoke",
            "Fixtures",
            "smoke-config.yaml"
        );
        fixture = Path.GetFullPath(fixture);
        Assert.True(File.Exists(fixture), fixture);

        var config = YamlConfigLoader.LoadFile(fixture);
        Assert.Equal("motely-config-aot-smoke", config.Id);
        Assert.Single(config.Must);
        Assert.Single(config.Should);
    }

    [Fact]
    public void TryLoad_SemanticError_HasLineColumn()
    {
        const string bad =
            """
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1]
                min: two
            """;
        Assert.False(YamlConfigLoader.TryLoad(bad, out _, out var error));
        Assert.Contains("line 6", error);
    }
}
