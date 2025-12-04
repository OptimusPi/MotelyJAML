using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DuckDB.NET.Data;

namespace Motely.API;

/// <summary>
/// Singleton DuckDB-based fertilizer pile for ranked seed storage
/// Handles migration from fertilizer.txt and provides top-K queries
/// </summary>
public sealed class FertilizerDatabase : IDisposable
{
    private static FertilizerDatabase? _instance;
    private static readonly object _lock = new();

    public static FertilizerDatabase Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new FertilizerDatabase();
                }
            }
            return _instance;
        }
    }

    private readonly string _dbPath;
    private readonly string _txtPath;
    private DuckDBConnection? _connection;
    private bool _disposed;

    private FertilizerDatabase()
    {
        var dataDir = Environment.CurrentDirectory;
        _dbPath = Path.Combine(dataDir, "fertilizer.db");
        _txtPath = Path.Combine(dataDir, "fertilizer.txt");
        
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            _connection = new DuckDBConnection($"Data Source={_dbPath}");
            _connection.Open();

            // Create table if not exists
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS fertilizer_pile (
                    seed VARCHAR PRIMARY KEY,
                    score INTEGER DEFAULT 0,
                    added_timestamp TIMESTAMP DEFAULT NOW()
                )";
            cmd.ExecuteNonQuery();

            // Migrate from fertilizer.txt if it exists
            MigrateFromTxtIfNeeded();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize fertilizer database: {ex.Message}");
        }
    }

    private void MigrateFromTxtIfNeeded()
    {
        if (!File.Exists(_txtPath) || _connection == null) return;

        try
        {
            // Check if DB already has data
            using var countCmd = _connection.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM fertilizer_pile";
            var count = (long)countCmd.ExecuteScalar()!;

            if (count > 0) return; // DB already populated

            Console.WriteLine($"Migrating fertilizer.txt to DuckDB...");

            // Use DuckDB's COPY command for efficient bulk import
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"COPY fertilizer_pile(seed) FROM '{_txtPath.Replace("\\", "\\\\")}' (FORMAT CSV, HEADER false, DELIMITER '\n')";
            cmd.ExecuteNonQuery();

            // Get final count
            var finalCount = (long)countCmd.ExecuteScalar()!;
            Console.WriteLine($"Migrated {finalCount} seeds to DuckDB");

            // Delete the txt file after successful migration
            File.Delete(_txtPath);
            Console.WriteLine("fertilizer.txt deleted after successful migration");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Add seeds with scores to the fertilizer pile (upserts on conflict)
    /// </summary>
    public async Task AddSeedsAsync(IEnumerable<(string seed, int score)> seedsWithScores)
    {
        if (_connection == null) return;

        try
        {
            using var appender = _connection.CreateAppender("fertilizer_pile");
            foreach (var (seed, score) in seedsWithScores)
            {
                var row = appender.CreateRow();
                row.AppendValue(seed);
                row.AppendValue(score);
                row.AppendValue(DateTime.UtcNow);
                row.EndRow();
            }
            appender.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to add seeds to fertilizer pile: {ex.Message}");
        }
    }

    /// <summary>
    /// Get top N seeds by score for API responses
    /// </summary>
    public List<(string seed, int score)> GetTopSeeds(int limit = 1000)
    {
        if (_connection == null) return new List<(string, int)>();

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT seed, score FROM fertilizer_pile ORDER BY score DESC LIMIT {limit}";
            
            var results = new List<(string, int)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add((reader.GetString(0), reader.GetInt32(1)));
            }
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to query fertilizer pile: {ex.Message}");
            return new List<(string, int)>();
        }
    }

    /// <summary>
    /// Get total seed count
    /// </summary>
    public long GetSeedCount()
    {
        if (_connection == null) return 0;

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM fertilizer_pile";
            return (long)cmd.ExecuteScalar()!;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _connection?.Close();
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during fertilizer database disposal: {ex.Message}");
        }

        _disposed = true;
    }
}