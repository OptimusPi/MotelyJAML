using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>Former Motely.Config YamlConfigLoader coverage — loader is <see cref="JamlConfigLoader"/>.</summary>
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
        var config = JamlConfigLoader.FromJaml(MinimalYaml);
        Assert.Equal("yaml-config-loader-test", config.Id);
        Assert.Single(config.Must);
    }

    [Fact]
    public void TryLoad_SemanticError_HasMessage()
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
        Assert.False(JamlConfigLoader.TryLoad(bad, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
