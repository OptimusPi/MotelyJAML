using Motely.Filters;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// ToJaml is what every host saves with (Balatro Seed Oracle writes each filter through it).
/// For every real filter that loads, FromJaml(ToJaml(config)) must load again with the same
/// clause shape. Text may differ; meaning may not. Set MOTELY_FILTERS_DIR to run the same check
/// over another folder (a product's Motelyfilters/).
/// </summary>
public sealed class JamlWriterRoundTripTests(ITestOutputHelper output)
{
    private static string FiltersDir() =>
        Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Join(AppContext.BaseDirectory, "Motelyfilters");

    [Fact]
    public void EveryLoadableFilter_SurvivesToJamlAndBack()
    {
        var dir = FiltersDir();
        Assert.True(Directory.Exists(dir), $"no filters at {dir}");

        var files = Directory.GetFiles(dir, "*.jaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(dir, "*.yaml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(dir, "*.yml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);

        var unloadable = new List<string>();
        var broken = new List<string>();
        int roundTripped = 0;

        foreach (var file in files)
        {
            var name = Path.GetRelativePath(dir, file);
            if (!JamlConfigLoader.TryLoad(File.ReadAllText(file), out var first, out var loadError))
            {
                unloadable.Add($"{name}: {loadError}");
                continue;
            }

            string text;
            try { text = JamlConfigLoader.ToJaml(first); }
            catch (Exception ex) { broken.Add($"{name}: ToJaml threw: {ex.Message}"); continue; }

            if (!JamlConfigLoader.TryLoad(text, out var second, out var reloadError))
            {
                broken.Add($"{name}: written JAML does not load: {reloadError}\n{text}");
                continue;
            }

            var diff = Shape(first) != Shape(second) ? $"{Shape(first)}\n   vs {Shape(second)}" : null;
            if (diff is not null)
            {
                broken.Add($"{name}: clause shape changed:\n   {diff}\n{text}");
                continue;
            }
            roundTripped++;
        }

        output.WriteLine($"{dir}: {files.Count} files, {roundTripped} round-tripped, {unloadable.Count} did not load");
        foreach (var u in unloadable)
            output.WriteLine("  not loadable (authoring error, not the writer's): " + u);

        Assert.True(broken.Count == 0, string.Join("\n\n", broken));
    }

    // The part of a config that must not change across a write: deck, stake, seeds, and the
    // clause tree (types, labels, min/max/score, antes, rolls, nested arms), in order.
    private static string Shape(JamlConfig c) =>
        $"{c.Deck}/{c.Stake}/seeds={c.Seeds.Count} " +
        $"must[{string.Join(",", c.Must.Select(Shape))}] " +
        $"should[{string.Join(",", c.Should.Select(Shape))}] " +
        $"mustNot[{string.Join(",", c.MustNot.Select(Shape))}]";

    private static string Shape(IMotelyClause k)
    {
        var s = $"{k.GetType().Name}(min={k.Min},max={k.Max},score={k.Score}";
        if (k is IAnteScopedClause a) s += $",antes={string.Join("|", a.Antes)}";
        if (k is IRollScopedClause r) s += $",rolls={string.Join("|", r.Rolls)}";
        if (k is LogicClause l) s += $",mode={l.Mode},arms=[{string.Join(",", l.Clauses.Select(Shape))}]";
        return s + ")";
    }
}
