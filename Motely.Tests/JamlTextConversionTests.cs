namespace Motely.Tests;

/// <summary>
/// <see cref="JamlConfigLoader.ToJson"/> and <see cref="JamlConfigLoader.ToYaml"/> let a JS app
/// hold every filter as JSON. Pinned on every filter in the test corpus: the JSON loads, and
/// YAML → JSON → YAML → JSON gives the same JSON, so neither direction loses or changes a value.
/// </summary>
public sealed class JamlTextConversionTests
{
    private static readonly string CorpusDir = Path.GetFullPath(
        Path.Join(AppContext.BaseDirectory, "..", "..", "..", "JamlFilters"));

    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory
            .GetFiles(CorpusDir, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".yaml" or ".yml" or ".jaml" or ".json")
            .OrderBy(f => f, StringComparer.Ordinal))
            data.Add(Path.GetRelativePath(CorpusDir, file));
        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void CorpusFilter_RoundTripsThroughJson(string file)
    {
        var yaml = File.ReadAllText(Path.Join(CorpusDir, file));
        var json = JamlConfigLoader.ToJson(yaml);
        Assert.True(JamlConfigLoader.TryLoad(json, out _, out var jsonError), $"{file} as JSON: {jsonError}");

        var yamlAgain = JamlConfigLoader.ToYaml(json);
        Assert.True(JamlConfigLoader.TryLoad(yamlAgain, out _, out var yamlError), $"{file} back to YAML: {yamlError}\n{yamlAgain}");
        Assert.Equal(json, JamlConfigLoader.ToJson(yamlAgain));
    }

    [Fact]
    public void ToJson_TypesUnquotedScalarsAndKeepsQuotedOnesAsStrings()
    {
        var json = JamlConfigLoader.ToJson(
            "name: \"123\"\nid: 123\ncutoff: ~\nmust:\n  - joker: Blueprint\n    antes: [1, 2]\n    score: 1.5\n    edition: 'true'\n");
        Assert.Equal(
            """
            {
              "name": "123",
              "id": 123,
              "cutoff": null,
              "must": [
                {
                  "joker": "Blueprint",
                  "antes": [
                    1,
                    2
                  ],
                  "score": 1.5,
                  "edition": "true"
                }
              ]
            }

            """.ReplaceLineEndings("\n"),
            json);
    }

    [Fact]
    public void ToYaml_WritesBlockClausesAndFlowValueLists()
    {
        var yaml = JamlConfigLoader.ToYaml(
            """{ "name": "Map", "deck": "Ghost", "seeds": ["12345678"], "must": [ { "joker": "Blueprint", "antes": [1], "sources": { "shopItems": [2] } } ] }""");
        Assert.Equal(
            """
            name: Map
            deck: Ghost
            seeds: ["12345678"]
            must:
              - joker: Blueprint
                antes: [1]
                sources:
                  shopItems: [2]

            """.ReplaceLineEndings("\n"),
            yaml);
    }

    [Fact]
    public void ToYaml_KeepsATrailingNewline()
    {
        // A folded or literal block ends its value with "\n"; written plain it would vanish.
        var json = JamlConfigLoader.ToJson("description: >\n  two\n  lines\nmust:\n  - joker: Any\n");
        Assert.Contains("\"two lines\\n\"", json);
        Assert.Equal(json, JamlConfigLoader.ToJson(JamlConfigLoader.ToYaml(json)));
    }

    [Fact]
    public void ToJson_NotYaml_ThrowsWithTheLine()
    {
        var ex = Assert.Throws<JamlFilterException>(() => JamlConfigLoader.ToJson("must:\n  - joker: [Blueprint\n"));
        Assert.True(ex.Line > 0);
    }
}
