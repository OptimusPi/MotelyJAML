using System.Text.Json;
using System.Text.Json.Serialization;
using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// Proof that two spellings of a filter are one filter: every clause the same type with the same
/// fields, and one fixed seed list scored the same, seed for seed.
/// </summary>
internal static class TwinFilters
{
    private static readonly JsonSerializerOptions FieldOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Fields(IMotelyClause clause) => clause.GetType().Name + JsonSerializer.Serialize(clause, clause.GetType(), FieldOptions);

    /// <summary>Every clause of the config, in order, as its type and fields.</summary>
    public static string[] AllFields(JamlConfig config) =>
        [.. config.Must.Select(c => "must " + Fields(c)), .. config.Should.Select(c => "should " + Fields(c)), .. config.MustNot.Select(c => "mustNot " + Fields(c))];

    /// <summary>A fixed pile of seeds, the same on every run.</summary>
    public static string[] SeedPile(int count)
    {
        const string alphabet = "123456789ABCDEFGHIJKLMNPQRSTUVWXYZ";
        var random = new Random(20261025);
        var seeds = new string[count];
        Span<char> chars = stackalloc char[8];
        for (int i = 0; i < count; i++)
        {
            for (int c = 0; c < chars.Length; c++)
                chars[c] = alphabet[random.Next(alphabet.Length)];
            seeds[i] = new string(chars);
        }
        return seeds;
    }

    /// <summary>Every seed the filter scores, as <c>seed,score,tallies</c>, sorted.</summary>
    public static List<string> Search(string yaml, string[] seeds)
    {
        var config = ProofSearch.LoadOrThrow(yaml);
        var results = new List<string>();
        using var search = MotelySearchBuilder
            .CreateSettings(config, 0)
            .WithAutoScoreCutoff(false)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(2)
            .WithQuietMode(true)
            .WithScoredResultCallback(r =>
            {
                var line = $"{r.Seed},{r.Score},{string.Join(",", r.TallyValuesSpan.ToArray())}";
                lock (results)
                    results.Add(line);
            })
            .Start();
        search.AwaitCompletion();
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    /// <summary>Both filters load to the same clauses and score the same seeds the same.</summary>
    public static void AssertSame(string expected, string actual, int seeds = 20_000, bool expectMatches = true)
    {
        Assert.Equal(AllFields(ProofSearch.LoadOrThrow(expected)), AllFields(ProofSearch.LoadOrThrow(actual)));
        var pile = SeedPile(seeds);
        var want = Search(expected, pile);
        if (expectMatches)
            Assert.NotEmpty(want);
        Assert.Equal(want, Search(actual, pile));
    }
}
