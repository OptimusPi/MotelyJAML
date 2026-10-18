using Motely.Schema;

namespace Motely.Tests;

/// <summary>
/// The TypeScript declarations motely-wasm ships as <c>motely-wasm/jaml</c> are read off the
/// engine's own vocabulary. These pin that every word the loader binds is in them, and that the
/// copies checked into Motely.Wasm/jaml are the current output of <c>motely schema</c>.
/// </summary>
public sealed class FilterTypeScriptTests
{
    private static readonly string Declarations = FilterTypeScript.Generate();

    [Fact]
    public void EveryRootKeyIsAFilterProperty()
    {
        var filter = Declarations[Declarations.IndexOf("export interface JamlFilter {", StringComparison.Ordinal)..];
        filter = filter[..filter.IndexOf("\n}", StringComparison.Ordinal)];
        foreach (var key in FilterVocabulary.RootKeys)
            Assert.Contains($"\n  {key}?: ", filter);
    }

    [Fact]
    public void EveryDiscriminatorIsAClauseMember()
    {
        var clause = Declarations[Declarations.IndexOf("export type JamlClause = ", StringComparison.Ordinal)..];
        clause = clause[..clause.IndexOf(";\n", StringComparison.Ordinal)];
        foreach (var disc in FilterVocabulary.DiscriminatorTable)
            Assert.Contains($", \"{disc.Key}\">>", clause);
    }

    [Theory]
    [InlineData(typeof(MotelyJoker))]
    [InlineData(typeof(MotelyVoucher))]
    [InlineData(typeof(MotelyTag))]
    [InlineData(typeof(MotelyBossBlind))]
    [InlineData(typeof(MotelyDeck))]
    [InlineData(typeof(MotelyStake))]
    [InlineData(typeof(MotelyItemEdition))]
    public void EnumUnionsNameEveryValue(Type enumType)
    {
        var line = Declarations
            .Split('\n')
            .Single(l => l.StartsWith($"export type {enumType.Name}Name = ", StringComparison.Ordinal));
        foreach (var name in Enum.GetNames(enumType))
            Assert.Contains($"\"{name}\"", line);
    }

    [Fact]
    public void ShippedDeclarationsAreCurrent() =>
        Assert.Equal(Declarations, File.ReadAllText(PackagePath("index.d.ts")).ReplaceLineEndings("\n"));

    [Fact]
    public void ShippedSchemaIsCurrent() =>
        Assert.Equal(
            FilterJsonSchema.Generate(),
            File.ReadAllText(PackagePath("motely-filter.schema.json")).ReplaceLineEndings("\n")
        );

    private static string PackagePath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Motely.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Motely.Wasm", "jaml", file);
    }
}
