using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Motely.Analysis;
using Motely.Executors;
using Motely.Filters;
using DuckDB.NET.Data;

namespace Motely.API;

public class SavedSearch
{
    public string Id { get; set; } = "";
    public string FilterJaml { get; set; } = "";
    public string Deck { get; set; } = "Red";
    public string Stake { get; set; } = "White";
    public long Timestamp { get; set; }
}

public class BackgroundSearchState
{
    public bool IsRunning { get; set; }
    public int SeedsAdded { get; set; }
    public long StartBatch { get; set; } // Batch we started from (for resume)
    public long CurrentBatch { get; set; } // Updated during search via progress callback
    public long TotalBatches { get; set; } // Total batches for progress calculation
    public long SeedsSearched { get; set; } // Total seeds searched so far
    public double SeedsPerMs { get; set; } // Current search speed
    public JsonSearchExecutor? Search { get; set; }
    public DuckDBConnection? Connection { get; set; }
    public DuckDBAppender? Appender { get; set; }
    public string? FilterJamlHash { get; set; } // Track if JAML changed to invalidate DB
}

/// <summary>
/// Simple HTTP API server for Motely seed searching
/// </summary>
public class MotelyApiServer
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly string _host;
    private readonly int _port;
    private readonly Action<string> _logCallback;

    // Fertilizer pile: ONLY stores seeds (strings), no results!
    // Motely is fast enough to re-search the pile each time with any filter
    // GLOBAL pile - top 1000 from EVERY search gets added, NEVER cleared!
    private static readonly HashSet<string> _fertilizerPile = new();
    private static readonly object _pileLock = new();
    private static readonly ConcurrentDictionary<string, SavedSearch> _savedSearches = new();
    private static readonly ConcurrentDictionary<string, BackgroundSearchState> _backgroundSearches = new();

    // Paths for persistence
    private static readonly string _filtersDir = "JamlFilters";
    private static readonly string _fertilizerPath = "fertilizer.txt";

    public bool IsRunning => _listener?.IsListening ?? false;
    public string Url => $"http://{_host}:{_port}/";
    public int ThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Stops THE running search (there can only be one due to SIMD/CPU constraints).
    /// Dumps seeds to fertilizer, saves batch position, closes connections cleanly.
    /// </summary>
    private async Task StopRunningSearchAsync()
    {
        // Find THE running search (there should only be one)
        var runningSearch = _backgroundSearches.FirstOrDefault(kvp => kvp.Value.IsRunning);
        if (runningSearch.Value == null) return;

        var searchId = runningSearch.Key;
        var bgState = runningSearch.Value;

        _logCallback($"[{DateTime.Now:HH:mm:ss}] Stopping search '{searchId}' (batch {bgState.CurrentBatch}, {bgState.SeedsAdded} seeds)...");

        // 1. Mark as stopped so callback stops processing
        bgState.IsRunning = false;

        // 2. Cancel the Motely executor
        bgState.Search?.Cancel();

        // 3. Wait a moment for graceful shutdown
        await Task.Delay(500);

        // 4. Dump top seeds from this search's DB to fertilizer pile
        try
        {
            if (bgState.Connection != null)
            {
                // Close appender first to flush data
                bgState.Appender?.Close();
                bgState.Appender = null;

                using var cmd = bgState.Connection.CreateCommand();
                cmd.CommandText = "SELECT seed FROM results ORDER BY score DESC LIMIT 1000";
                using var reader = cmd.ExecuteReader();

                var seedsToAdd = new List<string>();
                while (reader.Read())
                {
                    seedsToAdd.Add(reader.GetString(0));
                }

                if (seedsToAdd.Count > 0)
                {
                    lock (_pileLock)
                    {
                        foreach (var seed in seedsToAdd)
                        {
                            _fertilizerPile.Add(seed);
                        }
                    }
                    SaveFertilizerPile();
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Dumped {seedsToAdd.Count} seeds to fertilizer pile");
                }
            }
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Failed to dump seeds: {ex.Message}");
        }

        // 5. Close the connection
        try
        {
            bgState.Connection?.Close();
            bgState.Connection = null;
        }
        catch { /* ignore */ }

        // Save the batch position for resume
        bgState.StartBatch = bgState.CurrentBatch;
        _logCallback($"[{DateTime.Now:HH:mm:ss}] Search '{searchId}' stopped at batch {bgState.CurrentBatch}");
    }

    private static List<int> ReadTallies(System.Data.IDataReader reader)
    {
        var tallies = new List<int>();
        for (int i = 2; i < reader.FieldCount; i++)
        {
            tallies.Add(reader.IsDBNull(i) ? 0 : reader.GetInt32(i));
        }
        return tallies;
    }

    public MotelyApiServer(
        string host = "localhost",
        int port = 3141,
        Action<string>? logCallback = null,
        int? threadCount = null
    )
    {
        _host = host;
        _port = port;
        _logCallback = logCallback ?? Console.WriteLine;
        ThreadCount = threadCount ?? Environment.ProcessorCount;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_listener != null)
            throw new InvalidOperationException("Server is already running");

        // Initialize data directories
        Directory.CreateDirectory(".");
        Directory.CreateDirectory(_filtersDir);

        // Load fertilizer pile from disk
        LoadFertilizerPile();

        // Convert any JSON filters to JAML (one-time migration)
        ConvertJsonFiltersToJaml();

        // Load saved filters from disk
        LoadSavedFilters();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add(Url);

        try
        {
            _listener.Start();
            _logCallback($"[{DateTime.Now:HH:mm:ss}] API Server started on {Url}");

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), _cts.Token);
                }
                catch (HttpListenerException)
                {
                    // GetContextAsync throws when Stop() is called
                    if (_cts.Token.IsCancellationRequested || !_listener.IsListening)
                        break;
                    throw; // Re-throw if it's a real error
                }
            }
        }
        finally
        {
            _listener.Stop();
            _listener.Close();
            _logCallback($"[{DateTime.Now:HH:mm:ss}] API Server stopped");
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop(); // Force GetContextAsync() to throw and exit the loop

        // Persist fertilizer pile on shutdown
        SaveFertilizerPile();
    }

    private void LoadFertilizerPile()
    {
        try
        {
            if (File.Exists(_fertilizerPath))
            {
                var seeds = File.ReadAllLines(_fertilizerPath)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                lock (_pileLock)
                {
                    foreach (var seed in seeds)
                    {
                        _fertilizerPile.Add(seed);
                    }
                }

                _logCallback($"[{DateTime.Now:HH:mm:ss}] Loaded {seeds.Count} seeds from fertilizer pile");
            }
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to load fertilizer pile: {ex.Message}");
        }
    }

    private void SaveFertilizerPile()
    {
        try
        {
            List<string> seeds;
            lock (_pileLock)
            {
                seeds = _fertilizerPile.ToList();
            }

            File.WriteAllLines(_fertilizerPath, seeds);
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Saved {seeds.Count} seeds to fertilizer pile");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to save fertilizer pile: {ex.Message}");
        }
    }

    private void LoadSavedFilters()
    {
        try
        {
            var jamlFiles = Directory.GetFiles(_filtersDir, "*.jaml");
            foreach (var file in jamlFiles)
            {
                var filterName = Path.GetFileNameWithoutExtension(file);
                var jaml = File.ReadAllText(file);

                // Parse deck/stake from filename if present (format: FilterName_Deck_Stake.jaml)
                var parts = filterName.Split('_');
                var deck = parts.Length > 1 ? parts[^2] : "Red";
                var stake = parts.Length > 2 ? parts[^1] : "White";
                var name = parts.Length > 2 ? string.Join("_", parts.Take(parts.Length - 2)) : filterName;

                var searchId = $"{name}_{deck}_{stake}";
                _savedSearches[searchId] = new SavedSearch
                {
                    Id = searchId,
                    FilterJaml = jaml,
                    Deck = deck,
                    Stake = stake,
                    Timestamp = File.GetLastWriteTimeUtc(file).Ticks
                };
            }

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Loaded {jamlFiles.Length} saved filters");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to load saved filters: {ex.Message}");
        }
    }

    private void ConvertJsonFiltersToJaml()
    {
        var jsonFiltersDir = "JsonItemFilters";
        if (!Directory.Exists(jsonFiltersDir))
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] No JsonItemFilters directory found, skipping conversion");
            return;
        }

        var jsonFiles = Directory.GetFiles(jsonFiltersDir, "*.json");
        var converted = 0;
        var skipped = 0;

        foreach (var jsonPath in jsonFiles)
        {
            try
            {
                var jsonContent = File.ReadAllText(jsonPath);
                var config = ConfigFormatConverter.LoadFromJsonString(jsonContent);

                if (config == null)
                {
                    skipped++;
                    continue;
                }

                var jaml = config.SaveAsJaml();
                var baseName = Path.GetFileNameWithoutExtension(jsonPath);
                var jamlPath = Path.Combine(_filtersDir, $"{baseName}.jaml");

                // Only write if JAML doesn't already exist (don't overwrite user edits)
                if (!File.Exists(jamlPath))
                {
                    File.WriteAllText(jamlPath, jaml);
                    converted++;
                }
            }
            catch (Exception ex)
            {
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to convert {Path.GetFileName(jsonPath)}: {ex.Message}");
                skipped++;
            }
        }

        if (converted > 0 || skipped > 0)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] JSON→JAML: {converted} converted, {skipped} skipped, {jsonFiles.Length} total");
        }
    }

    private void SaveFilter(string searchId, string jaml)
    {
        try
        {
            var filePath = Path.Combine(_filtersDir, $"{searchId}.jaml");
            File.WriteAllText(filePath, jaml);
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Saved filter: {searchId}");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to save filter {searchId}: {ex.Message}");
        }
    }

    private static string ExtractFilterName(MotelyJsonConfig config, string jaml)
    {
        // Try to get name from config first
        if (!string.IsNullOrWhiteSpace(config.Name))
        {
            return SanitizeFilterName(config.Name);
        }

        // Try to extract from JAML (look for "name:" line)
        var lines = jaml.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                var name = trimmed.Substring(5).Trim().Trim('"', '\'');
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return SanitizeFilterName(name);
                }
            }
        }

        // Fallback: generate from first must/should item
        if (config.Must?.Count > 0)
        {
            var first = config.Must[0];
            return SanitizeFilterName($"{first.Type}_{first.Value}");
        }
        if (config.Should?.Count > 0)
        {
            var first = config.Should[0];
            return SanitizeFilterName($"{first.Type}_{first.Value}");
        }

        return "UnnamedFilter";
    }

    private static string SanitizeFilterName(string name)
    {
        // Remove invalid filename characters and limit length
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c) && c != ' ').ToArray());
        return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
    }

    private static string ExtractDeckFromJaml(string jaml)
    {
        var lines = jaml.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("deck:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(5).Trim().Trim('"', '\'');
            }
        }
        return "Red";
    }

    private static string ExtractStakeFromJaml(string jaml)
    {
        var lines = jaml.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("stake:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(6).Trim().Trim('"', '\'');
            }
        }
        return "White";
    }

    private static string SanitizeSearchId(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            id = id.Replace(c, '-');
        }
        return id.Replace(',', '-').Replace(' ', '-');
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Enable CORS
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath ?? "/";
            _logCallback($"[{DateTime.Now:HH:mm:ss}] {request.HttpMethod} {path}");

            if (request.HttpMethod == "GET" && path == "/")
            {
                await HandleIndexAsync(response);
            }
            else if (request.HttpMethod == "GET" && path == "/styles.css")
            {
                await ServeFileAsync(response, "wwwroot/styles.css", "text/css");
            }
            else if (request.HttpMethod == "GET" && path == "/script.js")  
            {
                await ServeFileAsync(response, "wwwroot/script.js", "application/javascript");
            }
            else if (path.StartsWith("/.well-known/"))
            {
                response.StatusCode = 404;
                response.Close();
            }
            else if (request.HttpMethod == "POST" && path == "/search")
            {
                response.ContentType = "application/json";
                await HandleSearchAsync(request, response);
            }
            else if (request.HttpMethod == "GET" && path == "/search")
            {
                response.ContentType = "application/json";
                await HandleSearchGetAsync(request, response);
            }
            else if (request.HttpMethod == "POST" && path == "/search/continue")
            {
                response.ContentType = "application/json";
                await HandleSearchContinueAsync(request, response);
            }
            else if (request.HttpMethod == "POST" && path == "/search/stop")
            {
                response.ContentType = "application/json";
                await HandleSearchStopAsync(request, response);
            }
            else if (request.HttpMethod == "POST" && path == "/analyze")
            {
                response.ContentType = "application/json";
                await HandleAnalyzeAsync(request, response);
            }
            else if (request.HttpMethod == "POST" && path == "/convert")
            {
                response.ContentType = "application/json";
                await HandleConvertAsync(request, response);
            }
            else if (request.HttpMethod == "GET" && path == "/filters")
            {
                response.ContentType = "application/json";
                await HandleFiltersGetAsync(response);
            }
            else if (request.HttpMethod == "DELETE" && path == "/search")
            {
                response.ContentType = "application/json";
                await HandleSearchDeleteAsync(request, response);
            }
            else if (request.HttpMethod == "DELETE" && path.StartsWith("/filters/"))
            {
                response.ContentType = "application/json";
                await HandleFilterDeleteAsync(request, response, path);
            }
            else
            {
                response.ContentType = "application/json";
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "Not Found" });
            }
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    private async Task HandleIndexAsync(HttpListenerResponse response)
    {
        await ServeFileAsync(response, "wwwroot/index.html", "text/html");
    }

    private async Task HandleSearchAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        // CRITICAL: Only ONE search can run at a time (SIMD/CPU constraint)
        // Stop any running search first, dump seeds to fertilizer, save batch position
        await StopRunningSearchAsync();

        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "Request body cannot be empty" });
            return;
        }

        var searchRequest = JsonSerializer.Deserialize<SearchRequest>(body);
        var filterJaml = searchRequest?.FilterJaml;
        var seedCount = searchRequest?.SeedCount ?? 1000000;

        if (string.IsNullOrWhiteSpace(filterJaml))
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "filterJaml is required" });
            return;
        }

        if (!JamlConfigLoader.TryLoadFromJamlString(filterJaml, out var config, out var loadError))
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = $"Invalid JAML: {loadError}" });
            return;
        }

        // Extract filter name, deck, stake from JAML
        var filterName = ExtractFilterName(config!, filterJaml);
        var deck = ExtractDeckFromJaml(filterJaml);
        var stake = ExtractStakeFromJaml(filterJaml);
        var searchId = SanitizeSearchId($"{filterName}_{deck}_{stake}");

        var isUpdated = _savedSearches.TryGetValue(searchId, out var existingSearch)
            && existingSearch.FilterJaml.Trim() != filterJaml.Trim();

        // If filter changed, reset the background search state AND delete stale DB
        if (isUpdated)
        {
            if (_backgroundSearches.TryGetValue(searchId, out var existingBgState))
            {
                existingBgState.StartBatch = 0;
                existingBgState.SeedsAdded = 0;
                existingBgState.IsRunning = false;
            }

            // Delete stale DB file - filter changed so old results are invalid
            var staleDbPath = $"{searchId}.db";
            try
            {
                if (File.Exists(staleDbPath)) File.Delete(staleDbPath);
                if (File.Exists(staleDbPath + ".wal")) File.Delete(staleDbPath + ".wal");
            }
            catch (Exception ex)
            {
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Could not delete stale DB: {ex.Message}");
            }

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Filter updated - cleared stale results, starting fresh");
        }

        _savedSearches[searchId] = new SavedSearch
        {
            Id = searchId,
            FilterJaml = filterJaml,
            Deck = deck,
            Stake = stake,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Initialize or reuse background search state to preserve StartBatch
        if (!_backgroundSearches.ContainsKey(searchId))
        {
            _backgroundSearches[searchId] = new BackgroundSearchState { StartBatch = 0, SeedsAdded = 0 };
        }

        SaveFilter(searchId, filterJaml);

        // NOTE: Running search already stopped at start of this handler via StopRunningSearchAsync()

        try
        {
            List<string> pileSeeds;
            lock (_pileLock)
            {
                pileSeeds = _fertilizerPile.ToList();
            }

            var results = new List<SearchResult>();

            if (pileSeeds.Count > 0)
            {
                var pileParams = new JsonSearchParams
                {
                    Threads = ThreadCount,
                    EnableDebug = false,
                    NoFancy = true,
                    Quiet = true,
                    SeedList = pileSeeds,
                    AutoCutoff = false,
                    Cutoff = 1,
                };

                Action<MotelySeedScoreTally> pileCallback = (tally) =>
                {
                    lock (results)
                    {
                        results.Add(new SearchResult
                        {
                            Seed = tally.Seed,
                            Score = tally.Score,
                            Tallies = tally.TallyColumns
                        });
                    }
                };

                var pileExecutor = new JsonSearchExecutor(config!, pileParams, pileCallback);
                pileExecutor.Execute();

                _logCallback($"[{DateTime.Now:HH:mm:ss}] Fertilizer search: {results.Count} matched from {pileSeeds.Count} in pile");
            }

            var topResults = results.OrderByDescending(r => r.Score).Take(1000).ToList();

            lock (_pileLock)
            {
                foreach (var result in topResults)
                {
                    _fertilizerPile.Add(result.Seed);
                }
            }

            int pileSize;
            lock (_pileLock)
            {
                pileSize = _fertilizerPile.Count;
            }

            response.StatusCode = 200;
            await WriteJsonAsync(response, new
            {
                searchId = searchId,
                results = topResults,
                total = results.Count,
                columns = config!.GetColumnNames(),
                pileSize = pileSize,
                isBackgroundRunning = true // We JUST started it!
            });

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Immediate response sent with {topResults.Count} results");

            // Smart cutoff from fertilizer results:
            // - No results = rare filter, accept everything (cutoff = 0)
            // - Has results = use 10th best score as cutoff threshold
            int smartCutoff = 0;
            if (topResults.Count >= 10)
            {
                smartCutoff = topResults[9].Score; // 10th best (0-indexed, already sorted desc)
            }
            else if (topResults.Count > 0)
            {
                smartCutoff = topResults[topResults.Count - 1].Score; // Worst of what we found
            }
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Smart cutoff: {smartCutoff} (from {topResults.Count} fertilizer results)");

            var bgConfig = config!;
            
            // Get existing background state or the one we just created
            if (!_backgroundSearches.TryGetValue(searchId, out var bgState))
            {
                bgState = new BackgroundSearchState { StartBatch = 0, SeedsAdded = 0 };
                _backgroundSearches[searchId] = bgState;
            }
            
            // Mark as running (preserve existing StartBatch for resume)  
            bgState.IsRunning = true;
            
            // Store in background searches for GET endpoint to find
            _backgroundSearches[searchId] = bgState;
            
            // Set up DuckDB connection and appender for this search
            var dbPath = $"{searchId}.db";
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Creating DB at: {Path.GetFullPath(dbPath)}");
            bgState.Connection = new DuckDBConnection($"Data Source={dbPath}");
            bgState.Connection.Open();
            
            // Create simple table - just seed and score
            using var createCmd = bgState.Connection.CreateCommand();
            createCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS results (
                    seed VARCHAR,
                    score INTEGER,
                    PRIMARY KEY (seed)
                )";
            createCmd.ExecuteNonQuery();
            
            bgState.Appender = bgState.Connection.CreateAppender("results");
            
            _backgroundSearches[searchId] = bgState;

            _ = Task.Run(() =>
            {
                try
                {
                    var bgExecutor = new JsonSearchExecutor(bgConfig, new JsonSearchParams
                    {
                        Threads = ThreadCount,
                        EnableDebug = false,
                        NoFancy = true,
                        Quiet = true,
                        BatchSize = 2, // Use 2-character sequential search
                        StartBatch = (ulong)bgState.StartBatch,
                        EndBatch = 0, // No end limit
                        AutoCutoff = false,
                        Cutoff = smartCutoff, // Smart cutoff from fertilizer results
                        ProgressCallback = (completed, total, seedsSearched, seedsPerMs) =>
                        {
                            // Update progress state for GET /search to read
                            bgState.CurrentBatch = bgState.StartBatch + completed;
                            bgState.TotalBatches = total;
                            bgState.SeedsSearched = seedsSearched;
                            bgState.SeedsPerMs = seedsPerMs;
                        }
                    }, (tally) => {
                        if (!bgState.IsRunning) return;
                        
                        // Track seeds found
                        bgState.SeedsAdded++;
                        
                        // Save to search-specific database that GET /search reads from
                        try 
                        {
                            var row = bgState.Appender?.CreateRow();
                            row?.AppendValue(tally.Seed);
                            row?.AppendValue(tally.Score);
                            row?.EndRow();
                            
                            // DuckDBAppender doesn't have Flush() - data is visible automatically
                            // Increment count for progress tracking
                            bgState.SeedsAdded++;
                        }
                        catch (Exception ex)
                        {
                            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to save to search DB: {ex.Message}");
                        }
                        
                        // Add just the seed to fertilizer pile  
                        lock (_pileLock)
                        {
                            _fertilizerPile.Add(tally.Seed);
                        }
                        
                        _logCallback($"[{DateTime.Now:HH:mm:ss}] Found seed: {tally.Seed} (score: {tally.Score})");
                    });
                    
                    bgState.Search = bgExecutor;
                    bgState.CurrentBatch = bgState.StartBatch;

                    // Execute without awaiting completion - it will run in background
                    bgExecutor.Execute(awaitCompletion: false);

                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Background search started for {searchId} from batch {bgState.StartBatch}");

                    // Background search will continue running until cancelled or completed
                }
                catch (Exception ex)
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Background search error: {ex.Message}");
                    if (_backgroundSearches.TryGetValue(searchId, out var state))
                    {
                        state.IsRunning = false;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Search failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    private async Task HandleSearchGetAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var searchId = request.QueryString["id"];
            
            if (string.IsNullOrEmpty(searchId))
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "search id required" });
                return;
            }

            if (!_savedSearches.TryGetValue(searchId, out var savedSearch))
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "search not found" });
                return;
            }

            if (!JamlConfigLoader.TryLoadFromJamlString(savedSearch.FilterJaml, out var config, out var loadError))
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = $"Invalid JAML: {loadError}" });
                return;
            }

            // Get results from DuckDB database
            var results = new List<SearchResult>();
            var dbPath = $"{searchId}.db";

            // If this search is running, use its open connection
            // Otherwise, open a new connection to the .db file (no conflict - different DBs are independent)
            if (_backgroundSearches.TryGetValue(searchId, out var bgState) && bgState.IsRunning && bgState.Connection != null)
            {
                // SAME search is running - use existing connection
                try
                {
                    using var cmd = bgState.Connection.CreateCommand();
                    cmd.CommandText = "SELECT * FROM results ORDER BY score DESC LIMIT 1000";
                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        results.Add(new SearchResult
                        {
                            Seed = reader.GetString(0),
                            Score = reader.GetInt32(1),
                            Tallies = ReadTallies(reader)
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to read from running search connection: {ex.Message}");
                }
            }
            else if (File.Exists(dbPath))
            {
                // Search NOT running - open new connection to .db file (safe, no conflict)
                results = GetTopResultsFromDb(dbPath, 1000);
            }
            
            // Check if background search is running
            var isRunning = false;
            long currentBatch = 0;
            long totalBatches = 0;
            long seedsSearched = 0;
            double seedsPerMs = 0;
            int seedsFound = 0;
            if (_backgroundSearches.TryGetValue(searchId, out var bgSearchState))
            {
                isRunning = bgSearchState.IsRunning;
                currentBatch = bgSearchState.CurrentBatch;
                totalBatches = bgSearchState.TotalBatches;
                seedsSearched = bgSearchState.SeedsSearched;
                seedsPerMs = bgSearchState.SeedsPerMs;
                seedsFound = bgSearchState.SeedsAdded;
            }

            // Determine status
            var status = isRunning ? "RUNNING" : "STOPPED";

            response.StatusCode = 200;
            await WriteJsonAsync(response, new
            {
                searchId = searchId,
                filterJaml = savedSearch.FilterJaml,
                deck = savedSearch.Deck,
                stake = savedSearch.Stake,
                results = results,
                total = results.Count,
                columns = config!.GetColumnNames(),
                status = status,
                currentBatch = currentBatch,
                totalBatches = totalBatches,
                seedsSearched = seedsSearched,
                seedsPerSecond = seedsPerMs * 1000, // Convert to per-second for UI
                seedsFound = seedsFound,
                isBackgroundRunning = isRunning
            });
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] GET Search failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    private async Task HandleSearchContinueAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            var requestData = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            
            if (!requestData!.TryGetValue("searchId", out var searchIdObj))
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "searchId required" });
                return;
            }
            
            var searchId = searchIdObj.ToString()!;
            
            if (!_backgroundSearches.TryGetValue(searchId, out var bgState))
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "background search not found" });
                return;
            }
            
            if (bgState.IsRunning)
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "search already running" });
                return;
            }
            
            // Restart the search
            bgState.IsRunning = true;
            var result = bgState.Search?.Execute(awaitCompletion: false);
            
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Continued search for {searchId}");
            
            response.StatusCode = 200;
            await WriteJsonAsync(response, new { 
                message = "search continued",
                searchId = searchId,
                status = "running"
            });
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Continue search failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    private async Task HandleSearchStopAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream);
            var body = await reader.ReadToEndAsync();
            var requestData = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            
            if (!requestData!.TryGetValue("searchId", out var searchIdObj))
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "searchId required" });
                return;
            }
            
            var searchId = searchIdObj.ToString()!;
            
            if (!_backgroundSearches.TryGetValue(searchId, out var bgState))
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "background search not found" });
                return;
            }
            
            if (!bgState.IsRunning)
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "search not running" });
                return;
            }
            
            // Stop the search
            bgState.IsRunning = false;
            bgState.Search?.Cancel();
            
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Stopped search for {searchId}");
            
            response.StatusCode = 200;
            await WriteJsonAsync(response, new { 
                message = "search stopped",
                searchId = searchId,
                status = "stopped"
            });
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Stop search failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    private async Task HandleAnalyzeAsync(
        HttpListenerRequest request,
        HttpListenerResponse response
    )
    {
        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();

        var analyzeRequest = JsonSerializer.Deserialize<AnalyzeRequest>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (analyzeRequest == null || string.IsNullOrWhiteSpace(analyzeRequest.Seed))
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "seed is required" });
            return;
        }

        try
        {
            var deck =
                string.IsNullOrEmpty(analyzeRequest.Deck)
                || !Enum.TryParse<MotelyDeck>(analyzeRequest.Deck, true, out var d)
                    ? MotelyDeck.Red
                    : d;

            var stake =
                string.IsNullOrEmpty(analyzeRequest.Stake)
                || !Enum.TryParse<MotelyStake>(analyzeRequest.Stake, true, out var s)
                    ? MotelyStake.White
                    : s;

            var config = new MotelySeedAnalysisConfig(analyzeRequest.Seed, deck, stake);
            var analysis = MotelySeedAnalyzer.Analyze(config);

            response.StatusCode = 200;
            await WriteJsonAsync(
                response,
                new
                {
                    seed = analyzeRequest.Seed,
                    deck = deck.ToString(),
                    stake = stake.ToString(),
                    analysis = analysis.ToString(),
                }
            );

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Analyzed seed {analyzeRequest.Seed}");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Analyze failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }


    private async Task HandleConvertAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();

        var convertRequest = JsonSerializer.Deserialize<ConvertRequest>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (convertRequest == null || string.IsNullOrWhiteSpace(convertRequest.JsonContent))
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "jsonContent is required" });
            return;
        }

        try
        {
            // Convert JSON to JAML using ConfigFormatConverter
            var config = ConfigFormatConverter.LoadFromJsonString(convertRequest.JsonContent);
            if (config == null)
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "Invalid JSON filter format" });
                return;
            }

            var jaml = config.SaveAsJaml();

            response.StatusCode = 200;
            await WriteJsonAsync(response, new { jaml });

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Converted JSON filter to JAML: {config.Name ?? "unnamed"}");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Convert failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    private async Task HandleFiltersGetAsync(HttpListenerResponse response)
    {
        try
        {
            var filters = new List<object>();

            if (Directory.Exists(_filtersDir))
            {
                // Load both .jaml and .json files
                var allFiles = Directory.GetFiles(_filtersDir, "*.jaml")
                    .Concat(Directory.GetFiles(_filtersDir, "*.json"));

                foreach (var filePath in allFiles)
                {
                    var fileName = Path.GetFileName(filePath);
                    var content = await File.ReadAllTextAsync(filePath);

                    // Extract the actual "name:" field from the filter content
                    var displayName = ExtractFilterName(content) ?? Path.GetFileNameWithoutExtension(fileName);

                    filters.Add(new
                    {
                        name = displayName,
                        filePath = fileName,
                        filterJaml = content
                    });
                }
            }

            response.StatusCode = 200;
            await WriteJsonAsync(response, filters);
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Returned {filters.Count} filter files");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Get filters failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Extract the "name:" field from JAML or "name" from JSON content
    /// </summary>
    private string? ExtractFilterName(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // Try JAML format first (name: value or name: "value")
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed.Substring(5).Trim();
                // Remove quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);
                if (value.StartsWith("'") && value.EndsWith("'"))
                    value = value.Substring(1, value.Length - 2);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        // Try JSON format ("name": "value")
        var nameMatch = System.Text.RegularExpressions.Regex.Match(content, "\"name\"\\s*:\\s*\"([^\"]+)\"");
        if (nameMatch.Success)
            return nameMatch.Groups[1].Value;

        return null;
    }

    private async Task HandleSearchDeleteAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            var query = request.Url?.Query ?? "";
            var searchId = "";
            
            // Parse search ID from query string
            if (query.StartsWith("?id="))
            {
                searchId = Uri.UnescapeDataString(query.Substring(4));
            }
            
            if (string.IsNullOrEmpty(searchId))
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "Search ID required" });
                return;
            }
            
            // Remove from saved searches (safe - only removes from memory)
            if (_savedSearches.TryRemove(searchId, out var removedSearch))
            {
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Deleted search: {searchId}");
                await WriteJsonAsync(response, new { success = true, message = $"Search {searchId} deleted" });
            }
            else
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "Search not found" });
            }
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Delete search failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
        }
    }

    private async Task HandleFilterDeleteAsync(HttpListenerRequest request, HttpListenerResponse response, string path)
    {
        try
        {
            // Extract filename safely - just the name part
            var fileName = path.Substring("/filters/".Length);
            
            // Validate: must be .jaml or .json and no path chars
            if (string.IsNullOrEmpty(fileName) || 
                (!fileName.EndsWith(".jaml") && !fileName.EndsWith(".json")) ||
                fileName.Contains("/") || 
                fileName.Contains("\\") || 
                fileName.Contains(".."))
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "Invalid filter name" });
                return;
            }
            
            var filePath = Path.Combine(_filtersDir, fileName);
            
            if (!File.Exists(filePath))
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "Filter not found" });
                return;
            }
            
            File.Delete(filePath);
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Deleted filter file: {fileName}");
            
            await WriteJsonAsync(response, new { success = true, message = $"Filter {fileName} deleted" });
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Delete filter failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
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
            cmd.CommandText = $"SELECT * FROM results ORDER BY score DESC LIMIT {limit}";
            
            var results = new List<SearchResult>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var seed = reader.GetString(0);
                var score = reader.GetInt32(1);

                var tallies = new List<int>();
                for (int i = 2; i < reader.FieldCount; i++)
                {
                    tallies.Add(reader.IsDBNull(i) ? 0 : reader.GetInt32(i));
                }

                results.Add(new SearchResult
                {
                    Seed = seed,
                    Score = score,
                    Tallies = tallies
                });
            }
            
            return results;
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to read from DB {dbPath}: {ex.Message}");
            return new List<SearchResult>();
        }
    }

    private async Task ServeFileAsync(HttpListenerResponse response, string filePath, string contentType)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                response.StatusCode = 404;
                await response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"File not found: {filePath}"));
                return;
            }

            var content = await File.ReadAllTextAsync(filePath);
            response.ContentType = contentType;
            response.StatusCode = 200;
            
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to serve {filePath}: {ex.Message}");
            response.StatusCode = 500;
            await response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("Server error"));
            response.Close();
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(
            data,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }
        );

        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

}

public class SearchRequest
{
    [JsonPropertyName("filterJaml")]
    public string? FilterJaml { get; set; }

    [JsonPropertyName("seedCount")]
    public long SeedCount { get; set; }
}

public class SearchResult
{
    [JsonPropertyName("seed")]
    public string Seed { get; set; } = "";

    [JsonPropertyName("score")]
    public int Score { get; set; }

    [JsonPropertyName("tallies")]
    public List<int> Tallies { get; set; } = new();
}

public class AnalyzeRequest
{
    [JsonPropertyName("seed")]
    public string Seed { get; set; } = "";

    [JsonPropertyName("deck")]
    public string? Deck { get; set; }

    [JsonPropertyName("stake")]
    public string? Stake { get; set; }
}


public class ConvertRequest
{
    [JsonPropertyName("jsonContent")]
    public string JsonContent { get; set; } = "";
}

/// <summary>
/// Generates JAML filters from natural language prompts using keyword matching
/// </summary>
public static class JamlGenie
{
    // Legendary jokers (soulJoker type)
    private static readonly HashSet<string> SoulJokers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Canio", "Triboulet", "Yorick", "Chicot", "Perkeo"
    };

    // Rare jokers
    private static readonly HashSet<string> RareJokers = new(StringComparer.OrdinalIgnoreCase)
    {
        "DNA", "Vagabond", "Baron", "Obelisk", "BaseballCard", "AncientJoker", "Campfire",
        "Blueprint", "WeeJoker", "HitTheRoad", "TheDuo", "TheTrio", "TheFamily", "TheOrder",
        "TheTribe", "Stuntman", "InvisibleJoker", "Brainstorm", "DriversLicense", "BurntJoker"
    };

    // Uncommon jokers
    private static readonly HashSet<string> UncommonJokers = new(StringComparer.OrdinalIgnoreCase)
    {
        "JokerStencil", "FourFingers", "Mime", "CeremonialDagger", "MarbleJoker", "LoyaltyCard",
        "Dusk", "Fibonacci", "SteelJoker", "Hack", "Pareidolia", "SpaceJoker", "Burglar",
        "Blackboard", "SixthSense", "Constellation", "Hiker", "CardSharp", "Madness", "Seance",
        "Vampire", "Shortcut", "Hologram", "Cloud9", "Rocket", "MidasMask", "Luchador",
        "GiftCard", "TurtleBean", "Erosion", "ToTheMoon", "StoneJoker", "LuckyCat", "Bull",
        "DietCola", "TradingCard", "FlashCard", "SpareTrousers", "Ramen", "Seltzer", "Castle",
        "MrBones", "Acrobat", "SockAndBuskin", "Troubadour", "Certificate", "SmearedJoker",
        "Throwback", "RoughGem", "Bloodstone", "Arrowhead", "OnyxAgate", "GlassJoker", "Showman",
        "FlowerPot", "MerryAndy", "OopsAll6s", "TheIdol", "SeeingDouble", "Matador", "Satellite",
        "Cartomancer", "Astronomer", "Bootstraps"
    };

    // Common jokers
    private static readonly HashSet<string> CommonJokers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Joker", "GreedyJoker", "LustyJoker", "WrathfulJoker", "GluttonousJoker", "JollyJoker",
        "ZanyJoker", "MadJoker", "CrazyJoker", "DrollJoker", "SlyJoker", "WilyJoker", "CleverJoker",
        "DeviousJoker", "CraftyJoker", "HalfJoker", "CreditCard", "Banner", "MysticSummit",
        "EightBall", "Misprint", "RaisedFist", "ChaostheClown", "ScaryFace", "AbstractJoker",
        "DelayedGratification", "GrosMichel", "EvenSteven", "OddTodd", "Scholar", "BusinessCard",
        "Supernova", "RideTheBus", "Egg", "Runner", "IceCream", "Splash", "BlueJoker",
        "FacelessJoker", "GreenJoker", "Superposition", "ToDoList", "Cavendish", "RedCard",
        "SquareJoker", "RiffRaff", "Photograph", "ReservedParking", "MailInRebate", "Hallucination",
        "FortuneTeller", "Juggler", "Drunkard", "GoldenJoker", "Popcorn", "WalkieTalkie",
        "SmileyFace", "GoldenTicket", "Swashbuckler", "HangingChad", "ShootTheMoon"
    };

    // All jokers combined for easy lookup
    private static readonly HashSet<string> AllJokers = SoulJokers
        .Concat(RareJokers)
        .Concat(UncommonJokers)
        .Concat(CommonJokers)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Vouchers
    private static readonly HashSet<string> Vouchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Overstock", "OverstockPlus", "ClearanceSale", "Liquidation", "Hone", "GlowUp",
        "RerollSurplus", "RerollGlut", "CrystalBall", "OmenGlobe", "Telescope", "Observatory",
        "Grabber", "NachoTong", "Wasteful", "Recyclomancy", "TarotMerchant", "TarotTycoon",
        "PlanetMerchant", "PlanetTycoon", "SeedMoney", "MoneyTree", "Blank", "Antimatter",
        "MagicTrick", "Illusion", "Hieroglyph", "Petroglyph", "DirectorsCut", "Retcon",
        "PaintBrush", "Palette"
    };

    // Tags
    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        "UncommonTag", "RareTag", "NegativeTag", "FoilTag", "HolographicTag", "PolychromeTag",
        "InvestmentTag", "VoucherTag", "BossTag", "StandardTag", "CharmTag", "MeteorTag",
        "BuffoonTag", "HandyTag", "GarbageTag", "EtherealTag", "CouponTag", "DoubleTag",
        "JuggleTag", "D6Tag", "TopupTag", "SpeedTag", "OrbitalTag", "EconomyTag"
    };

    // Tarot cards
    private static readonly HashSet<string> Tarots = new(StringComparer.OrdinalIgnoreCase)
    {
        "TheFool", "TheMagician", "TheHighPriestess", "TheEmpress", "TheEmperor", "TheHierophant",
        "TheLovers", "TheChariot", "Justice", "TheHermit", "TheWheelOfFortune", "Strength",
        "TheHangedMan", "Death", "Temperance", "TheDevil", "TheTower", "TheStar", "TheMoon",
        "TheSun", "Judgement", "TheWorld"
    };

    // Spectral cards
    private static readonly HashSet<string> Spectrals = new(StringComparer.OrdinalIgnoreCase)
    {
        "Familiar", "Grim", "Incantation", "Talisman", "Aura", "Wraith", "Sigil", "Ouija",
        "Ectoplasm", "Immolate", "Ankh", "DejaVu", "Hex", "Trance", "Medium", "Cryptid",
        "Soul", "BlackHole"
    };

    // Planet cards
    private static readonly HashSet<string> Planets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune",
        "Pluto", "PlanetX", "Ceres", "Eris"
    };

    // Boss blinds
    private static readonly HashSet<string> Bosses = new(StringComparer.OrdinalIgnoreCase)
    {
        "AmberAcorn", "CeruleanBell", "CrimsonHeart", "VerdantLeaf", "VioletVessel", "TheArm",
        "TheClub", "TheEye", "TheFish", "TheFlint", "TheGoad", "TheHead", "TheHook", "TheHouse",
        "TheManacle", "TheMark", "TheMouth", "TheNeedle", "TheOx", "ThePillar", "ThePlant",
        "ThePsychic", "TheSerpent", "TheTooth", "TheWall", "TheWater", "TheWheel", "TheWindow"
    };

    // Decks
    private static readonly HashSet<string> Decks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Red", "Blue", "Yellow", "Green", "Black", "Magic", "Nebula", "Ghost", "Abandoned",
        "Checkered", "Zodiac", "Painted", "Anaglyph", "Plasma", "Erratic", "Challenge"
    };

    // Stakes
    private static readonly HashSet<string> Stakes = new(StringComparer.OrdinalIgnoreCase)
    {
        "White", "Red", "Green", "Black", "Blue", "Purple", "Orange", "Gold"
    };

    // Editions
    private static readonly HashSet<string> Editions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Foil", "Holo", "Polychrome", "Negative"
    };

    private static string BuildJaml(List<JamlClause> clauses, string deck, string stake, string prompt)
    {
        var sb = new System.Text.StringBuilder();

        // Determine if should vs must based on prompt keywords
        var useMust = prompt.Contains("must") || prompt.Contains("require") || prompt.Contains("need");
        var useShould = prompt.Contains("should") || prompt.Contains("prefer") || prompt.Contains("want") || prompt.Contains("score");

        // If neither specified, default to must for primary items
        if (!useMust && !useShould)
        {
            useMust = true;
        }

        if (useMust && clauses.Count > 0)
        {
            sb.AppendLine("must:");
            foreach (var clause in clauses)
            {
                sb.AppendLine($"  - {clause.Type}: {clause.Value}");
                if (clause.Edition != null)
                {
                    sb.AppendLine($"    edition: {clause.Edition}");
                }
                sb.AppendLine($"    antes: [{string.Join(", ", clause.Antes)}]");
            }
        }

        if (useShould && !useMust && clauses.Count > 0)
        {
            sb.AppendLine("should:");
            var score = 100;
            foreach (var clause in clauses)
            {
                sb.AppendLine($"  - {clause.Type}: {clause.Value}");
                if (clause.Edition != null)
                {
                    sb.AppendLine($"    edition: {clause.Edition}");
                }
                sb.AppendLine($"    antes: [{string.Join(", ", clause.Antes)}]");
                sb.AppendLine($"    score: {score}");
                score = Math.Max(10, score - 20);
            }
        }

        sb.AppendLine($"deck: {deck}");
        sb.AppendLine($"stake: {stake}");

        return sb.ToString().TrimEnd();
    }

    private static string GenerateHelpfulDefault(string prompt, string deck, string stake)
    {
        // Provide a helpful template with comments
        return $@"# Genie couldn't find specific items in your request.
# Try mentioning specific item names like:
#   - Jokers: Blueprint, Perkeo, Baron, DNA
#   - Vouchers: Telescope, Observatory, Antimatter
#   - Tags: NegativeTag, RareTag, PolychromeTag
#   - Editions: Negative, Polychrome, Foil, Holo
#
# Example: ""Find negative Perkeo in early antes with Telescope""

must:
  - voucher: Telescope
    antes: [1, 2]
should:
  - joker: Blueprint
    antes: [1, 2, 3]
    score: 50
deck: {deck}
stake: {stake}";
    }

    private class JamlClause
    {
        public string Type { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Edition { get; set; }
        public List<int> Antes { get; set; } = new();
    }
}
