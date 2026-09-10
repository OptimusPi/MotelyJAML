using DuckDB.NET.Data;
using Motely.DataLake;

namespace Motely.Tests;

/// <summary>
/// The seed lake: one SQLite-catalog DuckLake — catalog beside the data root, data in it — that every
/// filter and every writer share. Legacy per-filter <c>.duckdb</c> files and CSVs sitting in the root
/// still pour on <c>--drown</c>. Each test gets its own temp tree so catalogs never collide.
/// </summary>
public sealed class SeedLakeSinkTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "motely-lake-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public SeedLakeSinkTests() => _root = Path.Combine(_base, "Seeds");

    public void Dispose()
    {
        try { if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true); }
        catch (IOException) { /* a straggling handle on Windows; the temp tree is disposable */ }
    }

    private string Catalog => Path.Combine(_base, SeedLake.CatalogFileName);

    private static MotelyScoredSeedResult Result(string seed, int score, params int[] tallies)
    {
        var r = new MotelyScoredSeedResult();
        r.Reset(seed, score);
        foreach (var t in tallies)
            r.AddTally(t);
        return r;
    }

    private static SortedSet<string> Drain(SeedSourceProvider provider)
    {
        var seeds = new SortedSet<string>(StringComparer.Ordinal);
        for (string s; (s = provider.NextSeed()) != string.Empty; )
            seeds.Add(s);
        return seeds;
    }

    /// <summary>What a pre-lake per-filter file looks like: <c>seeds(seed VARCHAR PRIMARY KEY)</c>.</summary>
    private static void WriteLegacyFile(string path, params string[] seeds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new DuckDBConnection($"Data Source={path}");
        connection.Open();
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE seeds (seed VARCHAR PRIMARY KEY)";
        create.ExecuteNonQuery();
        foreach (var seed in seeds)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO seeds VALUES (?)";
            insert.Parameters.Add(new DuckDBParameter { Value = seed });
            insert.ExecuteNonQuery();
        }
    }

    [Fact]
    public void Catalog_sits_beside_the_data_root()
    {
        Assert.Equal(Catalog, SeedLake.CatalogPathFor(_root));
        Assert.Equal(Path.GetFullPath(_root), SeedLake.DataRoot(_root));
        // The legacy per-filter file is still addressable — it is the fallback and the old on-disk shape.
        Assert.Equal(Path.Combine(_root, "perkeo.duckdb"), SeedLakeSink.LakePath(_root, "perkeo"));
        Assert.False(SeedLake.Exists(_root));
    }

    [Fact]
    public void Scored_find_round_trips_score_tallies_and_labels()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo", tallyLabels: ["Perkeo", "Showman", "Negative Tag"]))
        {
            sink.OnScored(Result("AAAAAAAA", 42, 1, 0, 2));
            sink.OnSeed("BBBBBBBB");
            Assert.True(sink.UsingLake, "the lake itself must take the writes, not the legacy fallback");
        }

        Assert.True(File.Exists(Catalog));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.duckdb"));

        using var lake = SeedLake.Open(_root);
        var rows = lake.Results("perkeo");
        Assert.Equal(2, rows.Count);
        Assert.Equal("AAAAAAAA", rows[0].Seed);
        Assert.Equal(42, rows[0].Score);
        Assert.Equal([1, 0, 2], rows[0].Tallies!);
        Assert.Equal("BBBBBBBB", rows[1].Seed);
        Assert.Equal(0, rows[1].Score);
        Assert.Equal(["Perkeo", "Showman", "Negative Tag"], lake.TallyLabels("perkeo"));
        Assert.Equal(["perkeo"], lake.FilterIds());
    }

    [Fact]
    public void Two_filters_share_one_lake_but_read_separately()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.OnScored(Result("AAAAAAAA", 42));
            sink.OnScored(Result("BBBBBBBB", 99));
        }
        using (var sink = new SeedLakeSink(_root, "observatory"))
            sink.OnScored(Result("CCCCCCCC", 1));

        using var perkeo = SeedSourceProvider.FromLakeFilter(_root, "perkeo");
        using var observatory = SeedSourceProvider.FromLakeFilter(_root, "observatory");

        Assert.Equal(2, perkeo.SeedCount);
        Assert.Equal(["AAAAAAAA", "BBBBBBBB"], Drain(perkeo));
        Assert.Equal(1, observatory.SeedCount);
        Assert.Equal("CCCCCCCC", observatory.NextSeed());
        Assert.Single(Directory.EnumerateFiles(_base, SeedLake.CatalogFileName));
    }

    [Fact]
    public void FromLakeRoot_drowns_in_every_filters_seeds_and_every_legacy_file_deduped()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.OnScored(Result("AAAAAAAA", 42));
            sink.OnScored(Result("BBBBBBBB", 99));
        }
        using (var sink = new SeedLakeSink(_root, "observatory"))
        {
            sink.OnScored(Result("BBBBBBBB", 1)); // shared with perkeo — must dedupe
            sink.OnScored(Result("CCCCCCCC", 1));
        }
        // Files from before the lake, still sitting in the root, pour too: a headered CSV (header row
        // is shape-tested out) and a legacy per-filter .duckdb (overlap dedupes).
        File.WriteAllLines(Path.Combine(_root, "old.csv"), ["Seed,Score", "DDDDDDDD,5"]);
        WriteLegacyFile(Path.Combine(_root, "ancient.duckdb"), "EEEEEEEE", "AAAAAAAA");

        using var provider = SeedSourceProvider.FromLakeRoot(_root);

        Assert.Equal(5, provider.SeedCount);
        Assert.Equal(["AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "EEEEEEEE"], Drain(provider));
    }

    [Fact]
    public void Lake_can_be_written_while_a_provider_reads_it()
    {
        // The --drown contract: read the lake, then keep writing finds into that same lake
        // during the run. The provider must not hold anything open after construction.
        using (var sink = new SeedLakeSink(_root, "perkeo"))
            sink.OnScored(Result("AAAAAAAA", 42));

        using var provider = SeedSourceProvider.FromLakeRoot(_root);
        Assert.Equal(1, provider.SeedCount);

        using (var sink = new SeedLakeSink(_root, "perkeo"))
            sink.OnScored(Result("BBBBBBBB", 7));

        Assert.Equal("AAAAAAAA", provider.NextSeed());
        using var after = SeedSourceProvider.FromLakeFilter(_root, "perkeo");
        Assert.Equal(2, after.SeedCount);
    }

    [Fact]
    public void Finds_stay_in_memory_until_Flush()
    {
        using var sink = new SeedLakeSink(_root, "perkeo");
        sink.OnScored(Result("AAAAAAAA", 42));
        Assert.True(sink.UsingLake);

        using (var peek = SeedLake.Open(_root))
            Assert.Equal(0, peek.DistinctSeedCount("perkeo"));

        sink.Flush();

        using var after = SeedLake.Open(_root);
        Assert.Equal(1, after.DistinctSeedCount("perkeo"));
        Assert.Equal("AAAAAAAA", after.Seeds("perkeo")[0]);
    }

    [Fact]
    public void Two_writers_on_one_catalog_at_once_lose_nothing()
    {
        const int perWriter = 1500, overlap = 500;
        string Seed(int i) => "S" + i.ToString("D7");

        Parallel.For(0, 2, writer =>
        {
            using var sink = new SeedLakeSink(_root, "perkeo");
            int start = writer * (perWriter - overlap);
            for (int i = start; i < start + perWriter; i++)
                sink.OnScored(Result(Seed(i), i % 100, i % 3));
            Assert.True(sink.UsingLake);
        });

        using var lake = SeedLake.Open(_root);
        Assert.Equal(2 * perWriter - overlap, lake.DistinctSeedCount("perkeo"));
        Assert.Equal(2 * perWriter - overlap, lake.Seeds("perkeo").Count);
    }

    [Fact]
    public void Falls_back_to_the_legacy_file_when_the_catalog_cannot_attach()
    {
        // A catalog path under a *file* can never be created: ATTACH fails, the sink says so and
        // writes bare seeds to <root>/<filter>.duckdb so the run still keeps its finds.
        Directory.CreateDirectory(_base);
        var blocker = Path.Combine(_base, "blocker.txt");
        File.WriteAllText(blocker, "not a directory");
        var impossibleCatalog = Path.Combine(blocker, "ducklake.sqlite");

        using (var sink = new SeedLakeSink(_root, "perkeo", catalogPath: impossibleCatalog))
        {
            sink.OnScored(Result("AAAAAAAA", 42));
            Assert.False(sink.UsingLake);
        }

        var legacy = SeedLakeSink.LakePath(_root, "perkeo");
        Assert.True(File.Exists(legacy));
        using var provider = SeedSourceProvider.FromLake(legacy);
        Assert.Equal(1, provider.SeedCount);
        Assert.Equal("AAAAAAAA", provider.NextSeed());
    }

    [Fact]
    public void SeedSourceProvider_DistinctFlagDedupesSeeds()
    {
        var textPath = Path.Combine(_root, "seeds.txt");
        Directory.CreateDirectory(_root);
        File.WriteAllLines(textPath, ["AAAAAAAA", "BBBBBBBB", "AAAAAAAA"]);

        using var provider = new SeedSourceProvider(textPath, distinct: true);

        Assert.Equal(2, provider.SeedCount);
    }

    [Fact]
    public void SeedSourceProvider_ReadsSeedsFromARealJamlFile()
    {
        // A real corpus filter with a seeds: block — no fabricated JAML. The provider's seed count
        // must equal what the loader parses from the same file, cross-checking the two readers.
        var jamlPath = Path.Combine(AppContext.BaseDirectory, "JamlFilters", "Zerkeo_Pure.jaml");
        Assert.True(
            JamlConfigLoader.TryLoad(File.ReadAllText(jamlPath), out var config, out var error),
            error
        );
        Assert.NotEmpty(config!.Seeds);

        using var provider = new SeedSourceProvider(jamlPath);

        Assert.Equal(config.Seeds.Count, provider.SeedCount);
    }

    [Fact]
    public void FromLakeRoot_AlsoPoursExtraSeeds_DedupedAgainstTheLake()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.OnScored(Result("AAAAAAAA", 42));
            sink.OnScored(Result("BBBBBBBB", 99));
        }

        // The JAML's seeds: block rides along — overlap dedupes, junk is shape-tested out.
        using var provider = SeedSourceProvider.FromLakeRoot(
            _root,
            ["BBBBBBBB", "CCCCCCCC", " CCCCCCCC ", "Seed", "", "not-a-seed"]
        );

        Assert.Equal(3, provider.SeedCount);
        Assert.Equal(["AAAAAAAA", "BBBBBBBB", "CCCCCCCC"], Drain(provider));
    }

    [Fact]
    public void FromLakeRoot_WithNoLakeYet_DrownsInTheExtraSeedsAlone()
    {
        // A fresh filter's first --drown: no lake on disk, but the JAML already saved finds.
        Assert.False(Directory.Exists(_root));
        Assert.False(SeedSourceProvider.HasLakeFiles(_root));

        using var provider = SeedSourceProvider.FromLakeRoot(_root, ["AAAAAAAA", "BBBBBBBB"]);

        Assert.Equal(2, provider.SeedCount);
        Assert.Equal("AAAAAAAA", provider.NextSeed());
        Assert.Equal("BBBBBBBB", provider.NextSeed());
        Assert.Equal(string.Empty, provider.NextSeed());
    }

    [Fact]
    public void HasLakeFiles_SeesTheCatalogOrNonEmptyLakeShapedFiles()
    {
        Directory.CreateDirectory(_root);
        Assert.False(SeedSourceProvider.HasLakeFiles(_root)); // empty directory, no catalog

        File.WriteAllText(Path.Combine(_root, "notes.md"), "not a lake");
        File.WriteAllText(Path.Combine(_root, "empty.csv"), "");
        Assert.False(SeedSourceProvider.HasLakeFiles(_root)); // wrong shape / zero bytes

        using (var sink = new SeedLakeSink(_root, "perkeo"))
            sink.OnScored(Result("AAAAAAAA", 1));
        Assert.True(SeedSourceProvider.HasLakeFiles(_root)); // the catalog now exists
    }
}
