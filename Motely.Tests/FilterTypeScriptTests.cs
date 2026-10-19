using Motely.Schema;

namespace Motely.Tests;

/// <summary>
/// The TypeScript declarations motely-wasm ships as <c>motely-wasm/jaml</c> are read off the
/// engine's own vocabulary. These pin that every word the loader binds is in them, and that the
/// copies checked into Motely.Wasm/jaml are the current output of <c>dotnet run tools/schema.cs</c>.
/// </summary>
public sealed class FilterTypeScriptTests
{
    private static readonly string Declarations = FilterTypeScript.Generate();

    [Fact]
    public void EveryRootKeyIsAFilterProperty()
    {
        var filter = Section("export interface JamlFilter {", "\n}");
        foreach (var key in FilterVocabulary.RootKeys)
            Assert.Contains($"\n  {key}?: ", filter);
    }

    [Fact]
    public void EveryDiscriminatorIsAClauseMember()
    {
        var clause = Section("export type JamlClause = ", ";\n");
        foreach (var disc in FilterVocabulary.DiscriminatorTable)
            Assert.True(clause.Contains($", \"{disc.Key}\">>", StringComparison.Ordinal), $"JamlClause has no member keyed {disc.Key}.");
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
    public void AListOfBlocksIsAnArray()
    {
        // The loader binds soulOrder as a sequence and refuses a lone block, so the types say so.
        Assert.True(JamlConfigLoader.TryLoad("must:\n  - soulOrder: [{ legendary: Perkeo }]\n", out _, out _));
        Assert.False(JamlConfigLoader.TryLoad("must:\n  - soulOrder: { legendary: Perkeo }\n", out _, out _));

        var clause = Section("export interface JamlLegendarySoulOrderClause {", "\n}");
        Assert.Contains("\n  soulOrder?: JamlSoulOrderEntry[];", clause);
        Assert.Contains("\n  entries?: JamlSoulOrderEntry[];", clause);
    }

    [Fact]
    public void LuckTakesItsNumbers()
    {
        // The loader reads `luck: 4` as X4, so the type takes the numbers as well as the names.
        Assert.True(JamlConfigLoader.TryLoad("must:\n  - luckyMoney: [0]\n    with:\n      luck: 4", out _, out var error), error);
        Assert.Equal(
            "export type MotelyLuckName = \"X1\" | \"X2\" | \"X4\" | \"X5\" | \"X8\" | \"X16\" | \"X32\" | \"X64\" | 1 | 2 | 4 | 5 | 8 | 16 | 32 | 64;",
            Declarations.Split('\n').Single(l => l.StartsWith("export type MotelyLuckName = ", StringComparison.Ordinal)));
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

    /// <summary>The declarations from <paramref name="start"/> up to the first <paramref name="end"/> after it.</summary>
    private static string Section(string start, string end)
    {
        int from = Declarations.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"The declarations have no `{start}`.");
        int to = Declarations.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"`{start}` is never closed by `{end}`.");
        return Declarations[from..to];
    }

    private static string PackagePath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Motely.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Motely.Wasm", "jaml", file);
    }
}
