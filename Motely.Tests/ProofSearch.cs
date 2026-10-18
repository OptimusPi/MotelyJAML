
namespace Motely.Tests;

/// <summary>
/// Anti-fake rail. Proof = list-search with known seeds (or empty match).
/// No sequential sweeps here — those hang CI when the hit rate is low.
/// </summary>
public static class ProofSearch
{
    public static JamlConfig LoadOrThrow(string yaml)
    {
        if (!JamlConfigLoader.TryLoad(yaml, out var config, out var error) || config is null)
            throw new InvalidOperationException($"JAML load failed: {error}");
        return config;
    }

    public static (long Matching, List<string> Matched) ListMatch(
        string yaml,
        string[] seeds,
        int threads = 1,
        int cutoff = 0
    )
    {
        var config = LoadOrThrow(yaml);
        var matched = new List<string>();
        using var search = MotelySearchBuilder
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

    public static void MustMatchAll(string yaml, params string[] seeds)
    {
        var (matching, matched) = ListMatch(yaml, seeds);
        Assert.Equal(seeds.Length, (int)matching);
        Assert.Equal(
            seeds.OrderBy(static s => s, StringComparer.Ordinal).ToArray(),
            matched.OrderBy(static s => s, StringComparer.Ordinal).ToArray()
        );
    }

    public static void MustMatchNone(string yaml, params string[] seeds)
    {
        var (matching, _) = ListMatch(yaml, seeds);
        Assert.Equal(0L, matching);
    }
}
