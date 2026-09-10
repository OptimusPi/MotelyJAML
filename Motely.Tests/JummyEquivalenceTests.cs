using System.Collections.Generic;
using System.IO;
using System.Linq;
using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// JUMMY (Jammy Understands My Mumbling) equivalence. The Zerkeo variants are the same filter
/// written several ways a real person might type it — bare names, verbose "1 or 2 or … 8", and the
/// range shorthand "1-8". Same must-clauses, same seed block. The accessibility contract: however
/// you mumble the antes, the engine understands it the same way. So every variant must (a) load and
/// list-search its own seeds without throwing, and (b) match the identical set of seeds.
///
/// The variant list is DISCOVERED from JamlFilters/Zerkeo*.jaml, not hard-coded — drop a new
/// Zerkeo_*.jaml in the folder and it joins the gate automatically, no test edit. If a change breaks
/// either property, JUMMY regressed, and this fails the build to say so.
/// </summary>
public class JummyEquivalenceTests
{
    private static string TestJamlDir => Path.Join(AppContext.BaseDirectory, "JamlFilters");

    /// <summary>Every Zerkeo variant on disk, discovered — the single source of truth is the folder.</summary>
    public static IEnumerable<object[]> ZerkeoVariants() =>
        Directory
            .EnumerateFiles(TestJamlDir, "Zerkeo*.jaml")
            .Select(Path.GetFileName)
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .Select(name => new object[] { name! });

    private static (int Matching, IReadOnlyList<string> Matched) ListSearchOwnSeeds(string file)
    {
        Assert.True(
            JamlConfigLoader.TryLoad(
                File.ReadAllText(Path.Join(TestJamlDir, file)),
                out var config,
                out var error
            ),
            $"{file} failed to load: {error}"
        );
        Assert.NotEmpty(config!.Seeds);

        var matched = new List<string>();
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator(config.Seeds, config.Seeds.Count)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(matched.Add);

        using var search = settings.Start();
        search.AwaitCompletion();
        return ((int)search.MatchingSeeds, matched);
    }

    // "…and not throw errors no duh :)" — each variant loads and searches its own seed block clean.
    [Theory]
    [MemberData(nameof(ZerkeoVariants))]
    public void Variant_ListSearchesItsOwnSeeds_WithoutThrowing(string file)
    {
        var (matching, _) = ListSearchOwnSeeds(file);
        Assert.True(matching > 0, $"{file} matched none of its own seeds.");
    }

    // "…it should find the same seeds" — however the antes are mumbled, the matched set is identical
    // across every variant. Pinned against the first variant as the shared expected set.
    [Fact]
    public void AllVariants_FindTheSameSeeds()
    {
        var files = ZerkeoVariants().Select(row => (string)row[0]).ToArray();
        Assert.True(files.Length >= 2, "Expected at least two Zerkeo variants to compare.");

        var results = files
            .Select(f => (File: f, Matched: ListSearchOwnSeeds(f).Matched.OrderBy(s => s).ToArray()))
            .ToArray();

        var expected = results[0];
        foreach (var actual in results.Skip(1))
            Assert.True(
                expected.Matched.SequenceEqual(actual.Matched),
                $"{actual.File} matched a different set than {expected.File} "
                    + $"({actual.Matched.Length} vs {expected.Matched.Length} seeds)."
            );
    }
}
