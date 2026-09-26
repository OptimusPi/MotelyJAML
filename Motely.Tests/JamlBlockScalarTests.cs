using Motely.Filters.Jaml;
using Xunit;

namespace Motely.Tests;

/// <summary>
/// Block scalars follow YAML: '|' keeps the line breaks an author typed, '>' folds them into
/// spaces, and the chomping indicators ('-' strip, '+' keep) decide the trailing newline.
/// </summary>
public sealed class JamlBlockScalarTests
{
    private static string Doc(string indicator) =>
        $"name: probe\ndescription: {indicator}\n  line one\n  line two\nstake: White\n";

    private static string Description(string indicator)
    {
        Assert.True(JamlConfigLoader.TryLoad(Doc(indicator), out var config, out var error), error);
        return config!.Description!;
    }

    [Theory]
    [InlineData("|", "line one\nline two\n")]
    [InlineData("|-", "line one\nline two")]
    [InlineData(">", "line one line two\n")]
    [InlineData(">-", "line one line two")]
    public void BlockStyles_FollowYaml(string indicator, string expected) =>
        Assert.Equal(expected, Description(indicator));

    [Fact]
    public void BlockText_LineEndingInColon_IsKeptAsTyped()
    {
        const string doc = "name: probe\ndescription: |\n  Options:\n  more text\nstake: White\n";
        Assert.True(JamlConfigLoader.TryLoad(doc, out var config, out var error), error);
        Assert.Equal("Options:\nmore text\n", config!.Description);
    }
}
