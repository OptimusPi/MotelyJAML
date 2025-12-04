using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.NET.Data;
using Motely.Executors;

namespace Motely.API;

/// <summary>
/// Manages search instances with DuckDB persistence and proper sequential searching
/// Follows BalatroSeedOracle patterns for search management
/// </summary>
public class SearchManager
{
    private static SearchManager? _instance;
    private static readonly object _lock = new();
    
    public static SearchManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new SearchManager();
                }
            }
            return _instance;
        }
    }

    private readonly ConcurrentDictionary<string, ActiveSearch> _activeSearches = new();
    
    public class ActiveSearch
    {
        public string SearchId { get; set; } = "";
        public JsonSearchExecutor? Executor { get; set; }
        public CancellationTokenSource? CancellationToken { get; set; }
        public DuckDBConnection? Connection { get; set; }
        public DuckDBAppender? Appender { get; set; }
        public int CompletedBatches { get; set; } = 0;
        public int TotalResults { get; set; } = 0;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
    }
    
    /// <summary>
    /// Start a new search - stops any existing searches first
    /// Returns immediate results from existing DB if available
    /// </summary>
    public async Task<(List<SearchResult> immediateResults, string searchId)> StartSearchAsync(
        string filterJaml, string deck, string stake, int seedCount)
    {
        var searchId = $"{GetFilterName(filterJaml)}_{deck}_{stake}";
        var dbPath = $"{searchId}.db";
        
        // Step 1: Stop all existing searches
        StopAllSearches();
        
        // Step 2: Check if DB exists for immediate results
        var immediateResults = new List<SearchResult>();
        if (File.Exists(dbPath))
        {
            immediateResults = GetTopResultsFromDb(dbPath, 1000);
            
            // Step 2a: Dump to fertilizer and delete DB
            DumpToFertilizerAndDeleteDb(dbPath);
        }
        
        // Step 2b: Start new sequential search
        var search = new ActiveSearch
        {
            SearchId = searchId,
            CancellationToken = new CancellationTokenSource()
        };
        
        // Initialize DuckDB for this search
        search.Connection = new DuckDBConnection($"Data Source={dbPath}");
        search.Connection.Open();
        
        // Create results table with tallies
        using var cmd = search.Connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS results (
                seed VARCHAR,
                score INTEGER,
                tallies JSON,
                timestamp TIMESTAMP DEFAULT NOW()
            )";
        cmd.ExecuteNonQuery();
        
        search.Appender = search.Connection.CreateAppender("results");
        
        // Start the sequential search in background
        _ = Task.Run(() => RunSequentialSearch(search, filterJaml, deck, stake, seedCount));
        
        _activeSearches[searchId] = search;
        
        return (immediateResults, searchId);
    }
    
    /// <summary>
    /// Get current top results and progress for a search
    /// </summary>
    public (List<SearchResult> results, int progressPercent) GetSearchStatus(string searchId)
    {
        var dbPath = $"{searchId}.db";
        var results = GetTopResultsFromDb(dbPath, 1000);
        
        var progressPercent = 0;
        if (_activeSearches.TryGetValue(searchId, out var search))
        {
            // Calculate progress based on completed batches (like BalatroSeedOracle does)
            var totalBatches = 2000000; // Rough estimate for progress bar
            progressPercent = Math.Min(100, (search.CompletedBatches * 100) / totalBatches);
        }
        
        return (results, progressPercent);
    }
    
    /// <summary>
    /// Stop a search and return final results 
    /// </summary>
    public List<SearchResult> StopSearch(string searchId)
    {
        if (_activeSearches.TryRemove(searchId, out var search))
        {
            search.CancellationToken?.Cancel();
            search.Appender?.Close();
            search.Connection?.Close();
        }
        
        var dbPath = $"{searchId}.db";
        return GetTopResultsFromDb(dbPath, 1000);
    }
    
    private void StopAllSearches()
    {
        foreach (var kvp in _activeSearches)
        {
            kvp.Value.CancellationToken?.Cancel();
            kvp.Value.Appender?.Close();
            kvp.Value.Connection?.Close();
        }
        _activeSearches.Clear();
    }
    
    private async Task RunSequentialSearch(ActiveSearch search, string filterJaml, string deck, string stake, int seedCount)
    {
        try
        {
            // Save JAML to temp file for JsonSearchExecutor
            var tempConfigPath = $"{search.SearchId}_temp.jaml";
            await File.WriteAllTextAsync(tempConfigPath, filterJaml);
            
            // Create JsonSearchParams (check actual properties)
            var searchParams = new JsonSearchParams
            {
                Threads = Environment.ProcessorCount,
                BatchSize = 4, // Start with 4-character seeds
                StartBatch = (ulong)search.CompletedBatches, // Continue from where we left off
                EnableDebug = false,
                NoFancy = true,
                Quiet = false
            };

            search.Executor = new JsonSearchExecutor(tempConfigPath, searchParams, "jaml");
            
            // Run the actual search in background - JsonSearchExecutor handles everything
            await Task.Run(() => 
            {
                search.Executor.Execute();
                // Clean up temp file after search
                try { File.Delete(tempConfigPath); } catch { }
            }, search.CancellationToken?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled - normal
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Search {search.SearchId} failed: {ex.Message}");
        }
        finally
        {
            // Clean up
            search.Appender?.Close();
        }
    }
    
    private List<SearchResult> GetTopResultsFromDb(string dbPath, int limit)
    {
        if (!File.Exists(dbPath)) return new List<SearchResult>();
        
        try
        {
            using var conn = new DuckDBConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT seed, score FROM results ORDER BY score DESC LIMIT {limit}";
            
            var results = new List<SearchResult>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SearchResult 
                { 
                    Seed = reader.GetString(0), 
                    Score = reader.GetInt32(1) 
                });
            }
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read from {dbPath}: {ex.Message}");
            return new List<SearchResult>();
        }
    }
    
    private void DumpToFertilizerAndDeleteDb(string dbPath)
    {
        // TODO: Get top 1000 from search DB and add to fertilizer pile
        // Then delete the search DB file
        try
        {
            var topResults = GetTopResultsFromDb(dbPath, 1000);
            // Add to fertilizer logic here
            File.Delete(dbPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to dump {dbPath} to fertilizer: {ex.Message}");
        }
    }
    
    private string GetFilterName(string filterJaml)
    {
        // Extract filter name from JAML content
        try
        {
            var lines = filterJaml.Split('\n');
            var nameLine = lines.FirstOrDefault(l => l.StartsWith("name:", StringComparison.OrdinalIgnoreCase));
            if (nameLine != null)
            {
                return nameLine.Substring(5).Trim().Trim('"');
            }
        }
        catch { }
        
        return "UnknownFilter";
    }
}

// SearchResult class already exists in MotelyApiServer.cs