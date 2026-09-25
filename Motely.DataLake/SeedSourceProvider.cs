using System.Data.Common;
using DuckDB.NET.Data;
using Motely.SeedProviders;

namespace Motely.DataLake;

public sealed class SeedSourceProvider : IMotelySeedProvider, IDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly DbDataReader _reader;
    private readonly System.Threading.Lock _lock = new();
    private bool _disposed;

    public long SeedCount { get; }

    public SeedSourceProvider(string path, bool distinct = false)
        : this(path, distinct, extraSeeds: null, filterId: null) { }

    private SeedSourceProvider(string path, bool distinct, IReadOnlyList<string>? extraSeeds, string? filterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('\\', '/');

        _connection = new DuckDBConnection("Data Source=:memory:;threads=1");
        _connection.Open();

        if (IsRemotePath(path))
        {
            using var extCmd = _connection.CreateCommand();
            extCmd.CommandText = "INSTALL httpfs; LOAD httpfs;";
            extCmd.ExecuteNonQuery();
        }

        bool hasExtras = extraSeeds is { Count: > 0 };
        bool wholeLake = false;
        if (filterId is not null)
        {
            EnsureLakeTable();
            ImportFilterFiles(path, filterId);
            wholeLake = true;
        }
        else if (Directory.Exists(path))
        {
            if (distinct)
            {
                ImportLakeRoot(path);
                wholeLake = true;
            }
            else
            {
                path = path.TrimEnd('/') + "/*.csv";
            }
        }
        else if (distinct && hasExtras)
        {
            EnsureLakeTable();
            wholeLake = true;
        }

        if (wholeLake && hasExtras)
            ImportSeedList(extraSeeds!);

        var ext = wholeLake ? ".lake" : Path.GetExtension(path).ToLowerInvariant();
        bool structured = ext is ".json" or ".jaml";

        string select = distinct ? "SELECT DISTINCT" : "SELECT";
        const string seedExpr = "trim(#1, ' ' || chr(9) || chr(13))";
        string count = distinct ? $"COUNT(DISTINCT {seedExpr})" : "COUNT(*)";
        string seedShape =
            $" WHERE {seedExpr} IS NOT NULL AND length({seedExpr}) > 0"
            + (distinct || structured ? $" AND {seedExpr} SIMILAR TO '[1-9A-Z]{{1,8}}'" : "");

        string from = ext switch
        {
            ".lake" => LakeTable,
            ".parquet" or ".pq" => $"read_parquet('{EscapeSql(path)}')",
            ".db" or ".duckdb" or ".sqlite" or ".sqlite3" => ImportDatabaseSeeds(path),
            ".json" => $"""
                (SELECT unnest(
                    coalesce(json_extract_string(j, '$.seeds[*]'), []) ||
                    coalesce(json_extract_string(j, '$[*].seed'), []) ||
                    CASE WHEN json_type(j) = 'ARRAY' AND json_type(j, '$[0]') = 'VARCHAR'
                         THEN coalesce(json_extract_string(j, '$[*]'), []) ELSE [] END
                ) AS seed FROM (SELECT content::JSON AS j FROM read_text('{EscapeSql(path)}')))
                """,
            ".jaml" => $$"""
                (SELECT unnest(regexp_extract_all(
                    regexp_extract(content, '(?ms)^seeds:(.*)$', 1),
                    '(?m)^\s*-\s*([1-9A-Z]{1,8})\s*$', 1
                )) AS seed FROM read_text('{{EscapeSql(path)}}'))
                """,
            _ =>
                $"read_csv('{EscapeSql(path)}', header = false, null_padding = true, all_varchar = true, strict_mode = false, ignore_errors = true)",
        };

        string streamSql = $"{select} {seedExpr} AS seed FROM {from}{seedShape}";
        string countSql = $"SELECT {count} FROM {from}{seedShape}";

        if (distinct)
        {
            using var stage = _connection.CreateCommand();
            stage.CommandText = $"CREATE TEMP TABLE {StreamTable} AS {streamSql}";
            stage.ExecuteNonQuery();
            streamSql = $"SELECT seed FROM {StreamTable}";
            countSql = $"SELECT COUNT(*) FROM {StreamTable}";
        }

        using var countCmd = _connection.CreateCommand();
        countCmd.CommandText = countSql;
        SeedCount = Convert.ToInt64(countCmd.ExecuteScalar());

        var cmd = _connection.CreateCommand();
        cmd.CommandText = streamSql;
        cmd.UseStreamingMode = true;
        _reader = cmd.ExecuteReader();
    }

    public static SeedSourceProvider FromLake(string lakeFile) => new(lakeFile, distinct: true);

    public static SeedSourceProvider FromLakeFilter(string? lakeRoot, string filterId) =>
        new(SeedLakeSink.LakeRoot(lakeRoot), distinct: true, extraSeeds: null, filterId);

    public static SeedSourceProvider FromLakeRoot(
        string lakeRoot,
        IReadOnlyList<string>? extraSeeds = null
    ) => new(lakeRoot, distinct: true, extraSeeds, filterId: null);

    private static readonly string[] LakeFileExtensions =
    [
        ".duckdb",
        ".db",
        ".sqlite",
        ".sqlite3",
        ".csv",
        ".txt",
    ];

    private static bool IsLakeFile(string file) =>
        Array.IndexOf(LakeFileExtensions, Path.GetExtension(file).ToLowerInvariant()) >= 0;

    public static bool HasLakeFiles(string lakeRoot) =>
        Directory.Exists(lakeRoot)
        && Directory
            .EnumerateFiles(lakeRoot)
            .Any(f => IsLakeFile(f) && new FileInfo(f).Length > 0);

    public string NextSeed()
    {
        lock (_lock)
        {
            if (_disposed)
                return string.Empty;

            while (_reader.Read())
            {
                var seed = _reader.GetString(0);
                if (!string.IsNullOrEmpty(seed))
                    return seed;
            }
            return string.Empty;
        }
    }

    public int NextSeeds(string[] buffer)
    {
        if (buffer is not { Length: > 0 })
            return 0;

        lock (_lock)
        {
            if (_disposed)
                return 0;

            int count = 0;
            while (count < buffer.Length && _reader.Read())
            {
                var seed = _reader.GetString(0);
                if (!string.IsNullOrEmpty(seed))
                    buffer[count++] = seed;
            }
            return count;
        }
    }

    private const string LakeTable = "lake_seeds";
    private const string StreamTable = "seed_stream";

    private void EnsureLakeTable()
    {
        using var create = _connection.CreateCommand();
        create.CommandText = $"CREATE TABLE IF NOT EXISTS {LakeTable} (seed VARCHAR)";
        create.ExecuteNonQuery();
    }

    private void ImportFilterFiles(string root, string filterId)
    {
        var txtFile = SeedLakeSink.SeedFilePath(root, filterId).Replace('\\', '/');
        if (File.Exists(txtFile))
            ImportCsvOrText(txtFile);

        var csvFile = ScoredResultsCsvSink.ResultsPath(root, filterId).Replace('\\', '/');
        if (File.Exists(csvFile))
            ImportCsvOrText(csvFile);

        var legacy = SeedLakeSink.LakePath(root, filterId).Replace('\\', '/');
        if (File.Exists(legacy))
            ImportDatabaseSeeds(legacy);
    }

    private void ImportLakeRoot(string root)
    {
        EnsureLakeTable();
        var files = Directory
            .EnumerateFiles(root)
            .Where(f => IsLakeFile(f)
                && !Path.GetFileName(f).Equals("ducklake.sqlite", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var normalized = file.Replace('\\', '/');
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".csv" or ".txt")
            {
                ImportCsvOrText(normalized);
            }
            else
            {
                ImportDatabaseSeeds(normalized);
            }
        }
    }

    private void ImportCsvOrText(string path)
    {
        using var csv = _connection.CreateCommand();
        csv.CommandText =
            $"INSERT INTO {LakeTable} SELECT #1 FROM read_csv('{EscapeSql(path)}', header = false, null_padding = true, all_varchar = true, strict_mode = false, ignore_errors = true)";
        csv.ExecuteNonQuery();
    }

    private void ImportSeedList(IReadOnlyList<string> seeds)
    {
        EnsureLakeTable();
        using var insert = _connection.CreateCommand();
        insert.CommandText = $"INSERT INTO {LakeTable} VALUES (?)";
        var param = new DuckDBParameter();
        insert.Parameters.Add(param);
        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed))
                continue;
            param.Value = seed;
            insert.ExecuteNonQuery();
        }
    }

    private string ImportDatabaseSeeds(string path)
    {
        EnsureLakeTable();
        try
        {
            using var attach = _connection.CreateCommand();
            attach.CommandText = $"ATTACH '{EscapeSql(path)}' AS src (READ_ONLY)";
            attach.ExecuteNonQuery();
        }
        catch (Exception)
        {
            using var sqlite = _connection.CreateCommand();
            sqlite.CommandText =
                $"INSTALL sqlite; LOAD sqlite; ATTACH '{EscapeSql(path)}' AS src (TYPE sqlite, READ_ONLY)";
            sqlite.ExecuteNonQuery();
        }

        try
        {
            var tables = new List<string>();
            using (var q = _connection.CreateCommand())
            {
                q.CommandText =
                    "SELECT table_name FROM duckdb_tables() WHERE database_name = 'src'";
                using var r = q.ExecuteReader();
                while (r.Read())
                    tables.Add(r.GetString(0));
            }

            string? table = tables.Find(t =>
                t.Equals("seeds", StringComparison.OrdinalIgnoreCase)
            );
            table ??= tables.Find(t => t.Equals("results", StringComparison.OrdinalIgnoreCase));
            table ??= tables.Count == 1 ? tables[0] : null;
            if (table is null)
                throw new InvalidOperationException(
                    $"Seed database '{path}' resolves with a table named 'seeds' or 'results', or exactly one table; it has: [{string.Join(", ", tables)}]."
                );

            string quoted = $"src.\"{table.Replace("\"", "\"\"")}\"";
            using var copy = _connection.CreateCommand();
            copy.CommandText = $"INSERT INTO {LakeTable} SELECT #1 FROM {quoted}";
            copy.ExecuteNonQuery();
        }
        finally
        {
            using var detach = _connection.CreateCommand();
            detach.CommandText = "DETACH src";
            detach.ExecuteNonQuery();
        }
        return LakeTable;
    }

    private static string EscapeSql(string path) => path.Replace("'", "''");

    private static bool IsRemotePath(string path) =>
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("s3://", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _reader.Dispose();
        _connection.Dispose();
    }
}
