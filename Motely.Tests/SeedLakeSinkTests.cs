using DuckDB.NET.Data;
using Motely.DataLake;
using Motely.Filters;

namespace Motely.Tests;

public sealed class SeedLakeSinkTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "motely-lake-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public SeedLakeSinkTests() => _root = Path.Combine(_base, "Seeds");

    public void Dispose()
    {
        try { if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true); }
        catch (IOException) { }
    }

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

    private string[] ReadSeeds(string filterId)
    {
        var path = SeedLakeSink.SeedFilePath(_root, filterId);
        if (!File.Exists(path)) return [];
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToArray();
    }

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
    public void PathHelpers()
    {
        Assert.Equal(Path.Combine(_root, "perkeo.duckdb"), SeedLakeSink.LakePath(_root, "perkeo"));
        Assert.Equal(Path.Combine(_root, "perkeo.txt"), SeedLakeSink.SeedFilePath(_root, "perkeo"));
    }

    [Fact]
    public void Scored_find_writes_seed_to_text_file()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo", tallyLabels: ["Perkeo", "Showman", "Negative Tag"]))
        {
            sink.OnScored(Result("AAAAAAAA", 42, 1, 0, 2));
            sink.OnSeed("BBBBBBBB");
        }

        var seeds = ReadSeeds("perkeo");
        Assert.Equal(2, seeds.Length);
        Assert.Contains("AAAAAAAA", seeds);
        Assert.Contains("BBBBBBBB", seeds);
        Assert.Empty(Directory.EnumerateFiles(_root, "*.duckdb"));
    }

    [Fact]
    public void Two_filters_share_root_but_separate_files()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.OnScored(Result("AAAAAAAA", 42));
            sink.OnScored(Result("BBBBBBBB", 99));
        }
        using (var sink = new SeedLakeSink(_root, "observatory"))
            sink.OnScored(Result("CCCCCCCC", 1));

        Assert.Equal(["AAAAAAAA", "BBBBBBBB"], ReadSeeds("perkeo"));
        Assert.Equal(["CCCCCCCC"], ReadSeeds("observatory"));
    }

    [Fact]
    public void FromLakeFilter_reads_one_filters_seeds()
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
    }

    [Fact]
    public void FromLakeRoot_drowns_across_filters_and_legacy_files_deduped()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.OnScored(Result("AAAAAAAA", 42));
            sink.OnScored(Result("BBBBBBBB", 99));
        }
        using (var sink = new SeedLakeSink(_root, "observatory"))
        {
            sink.OnScored(Result("BBBBBBBB", 1));
            sink.OnScored(Result("CCCCCCCC", 1));
        }
        File.WriteAllLines(Path.Combine(_root, "old.csv"), ["Seed,Score", "DDDDDDDD,5"]);
        WriteLegacyFile(Path.Combine(_root, "ancient.duckdb"), "EEEEEEEE", "AAAAAAAA");

        using var provider = SeedSourceProvider.FromLakeRoot(_root);

        Assert.Equal(5, provider.SeedCount);
        Assert.Equal(["AAAAAAAA", "BBBBBBBB", "CCCCCCCC", "DDDDDDDD", "EEEEEEEE"], Drain(provider));
    }

    [Fact]
    public void Seed_file_can_be_written_while_a_provider_reads()
    {
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
    public void Dedupes_within_a_single_run()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.Write("AAAAAAAA", 1);
            sink.Write("AAAAAAAA", 2);
            sink.Write("5X5", 3);
        }

        Assert.Equal(["AAAAAAAA", "5X5"], ReadSeeds("perkeo"));
    }

    [Fact]
    public void Dedupes_across_runs()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
            sink.Write("AAAAAAAA", 1);

        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.Write("AAAAAAAA", 2);
            sink.Write("5X5", 3);
        }

        var seeds = ReadSeeds("perkeo");
        Assert.Equal(2, seeds.Length);
        Assert.Contains("AAAAAAAA", seeds);
        Assert.Contains("5X5", seeds);
    }

    [Fact]
    public void Finds_stay_in_memory_until_Flush()
    {
        using var sink = new SeedLakeSink(_root, "perkeo");
        sink.OnScored(Result("AAAAAAAA", 42));
        Assert.Empty(ReadSeeds("perkeo"));

        sink.Flush();
        Assert.Equal(["AAAAAAAA"], ReadSeeds("perkeo"));
    }

    [Fact]
    public void Two_concurrent_writers_lose_nothing()
    {
        const int perWriter = 1500, overlap = 500;
        string Seed(int i) => "S" + i.ToString("D7");

        Parallel.For(0, 2, writer =>
        {
            using var sink = new SeedLakeSink(_root, $"filter{writer}");
            int start = writer * (perWriter - overlap);
            for (int i = start; i < start + perWriter; i++)
                sink.OnScored(Result(Seed(i), i % 100, i % 3));
        });

        for (int i = 0; i < 2; i++)
            Assert.Equal(perWriter, ReadSeeds($"filter{i}").Length);
    }

    [Fact]
    public void Empty_seed_ignored()
    {
        using var sink = new SeedLakeSink(_root, "perkeo");
        sink.Write("", null);
        sink.Write(null!, null);
        sink.Flush();

        Assert.Empty(ReadSeeds("perkeo"));
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
    public void FromLakeRoot_AlsoPoursExtraSeeds_DedupedAgainstFiles()
    {
        using (var sink = new SeedLakeSink(_root, "perkeo"))
        {
            sink.OnScored(Result("AAAAAAAA", 42));
            sink.OnScored(Result("BBBBBBBB", 99));
        }

        using var provider = SeedSourceProvider.FromLakeRoot(
            _root,
            ["BBBBBBBB", "CCCCCCCC", " CCCCCCCC ", "Seed", "", "not-a-seed"]
        );

        Assert.Equal(3, provider.SeedCount);
        Assert.Equal(["AAAAAAAA", "BBBBBBBB", "CCCCCCCC"], Drain(provider));
    }

    [Fact]
    public void FromLakeRoot_WithNoFilesYet_DrownsInTheExtraSeedsAlone()
    {
        Assert.False(Directory.Exists(_root));
        Assert.False(SeedSourceProvider.HasLakeFiles(_root));

        using var provider = SeedSourceProvider.FromLakeRoot(_root, ["AAAAAAAA", "BBBBBBBB"]);

        Assert.Equal(2, provider.SeedCount);
        Assert.Equal("AAAAAAAA", provider.NextSeed());
        Assert.Equal("BBBBBBBB", provider.NextSeed());
        Assert.Equal(string.Empty, provider.NextSeed());
    }

    [Fact]
    public void HasLakeFiles_SeesNonEmptyLakeShapedFiles()
    {
        Directory.CreateDirectory(_root);
        Assert.False(SeedSourceProvider.HasLakeFiles(_root));

        File.WriteAllText(Path.Combine(_root, "notes.md"), "not a lake");
        File.WriteAllText(Path.Combine(_root, "empty.csv"), "");
        Assert.False(SeedSourceProvider.HasLakeFiles(_root));

        using (var sink = new SeedLakeSink(_root, "perkeo"))
            sink.OnScored(Result("AAAAAAAA", 1));
        Assert.True(SeedSourceProvider.HasLakeFiles(_root));
    }
}
