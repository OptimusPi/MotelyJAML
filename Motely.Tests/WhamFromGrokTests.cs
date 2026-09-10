namespace Motely.Tests;

/// <summary>
/// WHAM from grok and pifreak with love. These tests fail on purpose until the
/// leftover cleanup is done. Making them pass IS the task.
/// </summary>
public sealed class WhamFromGrokTests
{
    private static string TestsDir =>
        Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "..", "..", ".."));

    [Fact]
    public void DeadFiltersFolder_MustNotExist()
    {
        var dir = Path.Join(TestsDir, "filters");
        Assert.False(
            Directory.Exists(dir),
            "WHAM: Motely.Tests/filters is a dead duplicate. csproj does not copy it. Delete the folder."
        );
    }

    [Fact]
    public void GoldenRarityPrefixes_MatchEngineRarity()
    {
        var golden = Path.Join(TestsDir, "JamlFilters");
        Assert.True(Directory.Exists(golden), $"JamlFilters missing at {golden}");

        var bySlug = new Dictionary<string, (string Rarity, string EnumName)>(
            StringComparer.Ordinal
        );
        Index(bySlug, "common", Enum.GetNames<MotelyJokerCommon>());
        Index(bySlug, "uncommon", Enum.GetNames<MotelyJokerUncommon>());
        Index(bySlug, "rare", Enum.GetNames<MotelyJokerRare>());
        Index(bySlug, "legendary", Enum.GetNames<MotelyJokerLegendary>());

        var failures = new List<string>();
        foreach (
            var file in Directory
                .GetFiles(golden, "*.jaml", SearchOption.TopDirectoryOnly)
                .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
        )
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (!TrySplitRarity(stem, out var prefix, out var slug))
                continue;

            if (!bySlug.TryGetValue(slug, out var hit))
            {
                failures.Add($"{Path.GetFileName(file)}: slug '{slug}' is not an engine joker");
                continue;
            }

            if (!string.Equals(hit.Rarity, prefix, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{Path.GetFileName(file)}: filename says {prefix}, engine says {hit.Rarity} ({hit.EnumName})"
                );
            }
        }

        Assert.True(
            failures.Count == 0,
            "WHAM: rename until prefixes match MotelyJoker* rarity.\n"
                + string.Join(Environment.NewLine, failures)
        );
    }

    private static void Index(
        Dictionary<string, (string Rarity, string EnumName)> bySlug,
        string rarity,
        string[] names
    )
    {
        foreach (var name in names)
        {
            var slug = Slug(name);
            if (bySlug.TryGetValue(slug, out var existing))
            {
                throw new InvalidOperationException(
                    $"engine slug collision {slug}: {existing.EnumName} and {name}"
                );
            }
            bySlug[slug] = (rarity, name);
        }
    }

    private static bool TrySplitRarity(string stem, out string prefix, out string slug)
    {
        foreach (var rarity in new[] { "legendary", "uncommon", "common", "rare" })
        {
            var head = rarity + "-";
            if (!stem.StartsWith(head, StringComparison.OrdinalIgnoreCase))
                continue;
            prefix = rarity;
            slug = Slug(stem[head.Length..]);
            return slug.Length > 0;
        }

        prefix = "";
        slug = "";
        return false;
    }

    private static string Slug(string name)
    {
        var chars = name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }
}
