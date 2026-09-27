using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// Anti-fake rail. Proof = list-search with known seeds (or empty match).
/// No sequential sweeps here — those hang CI when the hit rate is low.
/// </summary>
public static class ProofSearch
{
    public static JamlConfig LoadOrThrow(string jaml)
    {
        if (!JamlConfigLoader.TryLoad(jaml, out var config, out var error) || config is null)
            throw new InvalidOperationException($"JAML load failed: {error}");
        return config;
    }

    public static (long Matching, List<string> Matched) ListMatch(
        string jaml,
        string[] seeds,
        int threads = 1,
        int cutoff = 0
    )
    {
        var config = LoadOrThrow(jaml);
        var matched = new List<string>();
        using var search = JamlSearchBuilder
            .CreateSettings(config, cutoff)
            .WithAutoScoreCutoff(false)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(threads)
            .WithQuietMode(true)
            .WithSeedMatchCallback(matched.Add)
            .Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, matched);
    }

    public static void MustMatchAll(string jaml, params string[] seeds)
    {
        var (matching, matched) = ListMatch(jaml, seeds);
        Assert.Equal(seeds.Length, (int)matching);
        Assert.Equal(
            seeds.OrderBy(static s => s, StringComparer.Ordinal).ToArray(),
            matched.OrderBy(static s => s, StringComparer.Ordinal).ToArray()
        );
    }

    public static void MustMatchNone(string jaml, params string[] seeds)
    {
        var (matching, _) = ListMatch(jaml, seeds);
        Assert.Equal(0L, matching);
    }
}
