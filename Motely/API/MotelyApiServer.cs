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

    /// <summary>
    /// Safely get top results from this search's connection (called by GET /search)
    /// </summary>
    public List<SearchResult> GetTopResults(int limit = 1000)
    {
        var results = new List<SearchResult>();
        if (Connection == null) return results;

        try
        {
            // Flush appender before query so we see latest results (BSO pattern)
            lock (this)
            {
                if (Appender != null)
                {
                    try
                    {
                        Appender.Dispose();
                    }
                    catch { /* ignore dispose errors */ }
                    Appender = null;
                }
            }

            using var cmd = Connection.CreateCommand();
            cmd.CommandText = $"SELECT * FROM results ORDER BY score DESC LIMIT {limit}";
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var tallies = new List<int>();
                for (int i = 2; i < reader.FieldCount; i++)
                {
                    tallies.Add(reader.IsDBNull(i) ? 0 : reader.GetInt32(i));
                }

                results.Add(new SearchResult
                {
                    Seed = reader.GetString(0),
                    Score = reader.GetInt32(1),
                    Tallies = tallies
                });
            }
        }
        catch
        {
            // Connection busy or error - return empty, fallback will use file
        }

        return results;
    }
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
    // NOW STORED IN DUCKDB - no more in-memory HashSet!
    private static DuckDBConnection? _fertilizerConnection;
    private static readonly object _fertilizerLock = new();
    private static readonly ConcurrentDictionary<string, SavedSearch> _savedSearches = new();

    // Single running search (only one can run at a time due to SIMD/CPU constraints)
    private static BackgroundSearchState? _currentSearch;
    private static string? _currentSearchId;

    // Paths for persistence
    private static readonly string _filtersDir = "JamlFilters";
    private static readonly string _fertilizerDbPath = "fertilizer.db";

    public bool IsRunning => _listener?.IsListening ?? false;
    public string Url => $"http://{_host}:{_port}/";
    public int ThreadCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Stops THE running search (there can only be one due to SIMD/CPU constraints).
    /// Dumps seeds to fertilizer, saves batch position, closes connections cleanly.
    /// </summary>
    private async Task StopRunningSearchAsync()
    {
        if (_currentSearch == null || !_currentSearch.IsRunning) return;

        var searchId = _currentSearchId!;
        var bgState = _currentSearch;

        _logCallback($"[{DateTime.Now:HH:mm:ss}] Stopping search '{searchId}' (batch {bgState.CurrentBatch}, {bgState.SeedsAdded} seeds)...");

        // 1. Mark as stopped so callback stops processing
        bgState.IsRunning = false;

        // 2. Cancel the Motely executor
        bgState.Search?.Cancel();

        // 3. Wait a moment for graceful shutdown
        await Task.Delay(500);

        // 4. Close appender first (may fail if duplicates exist - that's ok)
        try
        {
            bgState.Appender?.Close();
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Appender flush warning (duplicates ok): {ex.Message}");
        }
        bgState.Appender = null;

        // 5. Dump top seeds from this search's DB to fertilizer DB (INSERT INTO SELECT - zero C# memory!)
        try
        {
            if (bgState.Connection != null)
            {
                DumpSearchSeedsToFertilizer(bgState.Connection, 1000);
            }
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Failed to dump seeds: {ex.Message}");
        }

        // 6. Save batch position to DuckDB BEFORE closing connection
        try
        {
            if (bgState.Connection != null)
            {
                using var saveCmd = bgState.Connection.CreateCommand();
                saveCmd.CommandText = @"
                    INSERT INTO search_state (id, batch_size, last_completed_batch, updated_at)
                    VALUES (1, 4, ?, CURRENT_TIMESTAMP)
                    ON CONFLICT (id) DO UPDATE SET
                        last_completed_batch = excluded.last_completed_batch,
                        updated_at = excluded.updated_at";
                saveCmd.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter(bgState.CurrentBatch));
                saveCmd.ExecuteNonQuery();
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Saved batch position {bgState.CurrentBatch} to DB");

                // Flush WAL to main DB file
                using var checkpointCmd = bgState.Connection.CreateCommand();
                checkpointCmd.CommandText = "FORCE CHECKPOINT";
                checkpointCmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Failed to save batch position: {ex.Message}");
        }

        // 7. Close the connection
        try
        {
            bgState.Connection?.Close();
            bgState.Connection = null;
        }
        catch { /* ignore */ }

        // Update in-memory state for resume
        bgState.StartBatch = bgState.CurrentBatch;
        _logCallback($"[{DateTime.Now:HH:mm:ss}] Search '{searchId}' stopped at batch {bgState.CurrentBatch}");
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

        // Initialize fertilizer DuckDB (replaces old txt file)
        InitializeFertilizerDb();

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

        // Checkpoint fertilizer DB to persist any pending changes
        CheckpointFertilizer();

        // Close fertilizer connection
        lock (_fertilizerLock)
        {
            _fertilizerConnection?.Close();
            _fertilizerConnection = null;
        }
    }

    /// <summary>
    /// Initialize the fertilizer DuckDB database (creates table if needed)
    /// </summary>
    private void InitializeFertilizerDb()
    {
        try
        {
            var fullPath = Path.GetFullPath(_fertilizerDbPath);
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Initializing fertilizer DB at: {fullPath}");

            lock (_fertilizerLock)
            {
                _fertilizerConnection = new DuckDBConnection($"Data Source={_fertilizerDbPath}");
                _fertilizerConnection.Open();

                // Create seeds table with just seed string (no results - Motely re-searches!)
                using var createCmd = _fertilizerConnection.CreateCommand();
                createCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS seeds (
                        seed VARCHAR PRIMARY KEY
                    )";
                createCmd.ExecuteNonQuery();

                // Get count
                using var countCmd = _fertilizerConnection.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(*) FROM seeds";
                var count = Convert.ToInt64(countCmd.ExecuteScalar());

                _logCallback($"[{DateTime.Now:HH:mm:ss}] Fertilizer DB ready with {count} seeds");

                // Show preview if any seeds exist
                if (count > 0)
                {
                    using var previewCmd = _fertilizerConnection.CreateCommand();
                    previewCmd.CommandText = "SELECT seed FROM seeds LIMIT 5";
                    using var reader = previewCmd.ExecuteReader();
                    var preview = new List<string>();
                    while (reader.Read()) preview.Add(reader.GetString(0));
                    _logCallback($"[{DateTime.Now:HH:mm:ss}]   Preview: {string.Join(", ", preview)}{(count > 5 ? "..." : "")}");
                }
            }

            // Migrate from old fertilizer.txt if it exists (one-time migration)
            MigrateFertilizerTxtToDb();
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to initialize fertilizer DB: {ex.Message}");
        }
    }

    /// <summary>
    /// One-time migration from fertilizer.txt to fertilizer.db
    /// </summary>
    private void MigrateFertilizerTxtToDb()
    {
        const string oldPath = "fertilizer.txt";
        if (!File.Exists(oldPath)) return;

        try
        {
            var seeds = File.ReadAllLines(oldPath)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToList();

            if (seeds.Count == 0) return;

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Migrating {seeds.Count} seeds from fertilizer.txt to DB...");

            lock (_fertilizerLock)
            {
                if (_fertilizerConnection == null) return;

                // Bulk insert using INSERT OR IGNORE for deduplication
                foreach (var seed in seeds)
                {
                    using var insertCmd = _fertilizerConnection.CreateCommand();
                    insertCmd.CommandText = "INSERT OR IGNORE INTO seeds (seed) VALUES (?)";
                    insertCmd.Parameters.Add(new DuckDBParameter(seed));
                    insertCmd.ExecuteNonQuery();
                }

                // Checkpoint to persist
                using var checkpointCmd = _fertilizerConnection.CreateCommand();
                checkpointCmd.CommandText = "CHECKPOINT";
                checkpointCmd.ExecuteNonQuery();
            }

            // Rename old file so we don't migrate again
            File.Move(oldPath, oldPath + ".migrated");
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Migration complete, renamed old file to {oldPath}.migrated");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Migration warning: {ex.Message}");
        }
    }

    /// <summary>
    /// Get count of seeds in fertilizer DB
    /// </summary>
    private long GetFertilizerCount()
    {
        lock (_fertilizerLock)
        {
            if (_fertilizerConnection == null) return 0;
            try
            {
                using var cmd = _fertilizerConnection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM seeds";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }
    }

    /// <summary>
    /// Add a single seed to fertilizer DB
    /// </summary>
    private void AddSeedToFertilizer(string seed)
    {
        lock (_fertilizerLock)
        {
            if (_fertilizerConnection == null) return;
            try
            {
                using var cmd = _fertilizerConnection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO seeds (seed) VALUES (?)";
                cmd.Parameters.Add(new DuckDBParameter(seed));
                cmd.ExecuteNonQuery();
            }
            catch { /* ignore duplicates */ }
        }
    }

    /// <summary>
    /// Dump top seeds from a search DB to fertilizer DB using INSERT INTO SELECT (no C# memory!)
    /// </summary>
    private void DumpSearchSeedsToFertilizer(DuckDBConnection searchConnection, int limit = 1000)
    {
        lock (_fertilizerLock)
        {
            if (_fertilizerConnection == null) return;

            try
            {
                // DuckDB cross-database: ATTACH the fertilizer DB to the search connection
                // Then INSERT INTO ... SELECT directly!
                var fertilizerFullPath = Path.GetFullPath(_fertilizerDbPath);

                using var attachCmd = searchConnection.CreateCommand();
                attachCmd.CommandText = $"ATTACH '{fertilizerFullPath}' AS fertilizer_db";
                attachCmd.ExecuteNonQuery();

                // INSERT INTO fertilizer_db.seeds SELECT from local results - NO C# MEMORY!
                using var insertCmd = searchConnection.CreateCommand();
                insertCmd.CommandText = $@"
                    INSERT OR IGNORE INTO fertilizer_db.seeds (seed)
                    SELECT seed FROM results ORDER BY score DESC LIMIT {limit}";
                var inserted = insertCmd.ExecuteNonQuery();

                // Detach when done
                using var detachCmd = searchConnection.CreateCommand();
                detachCmd.CommandText = "DETACH fertilizer_db";
                detachCmd.ExecuteNonQuery();

                _logCallback($"[{DateTime.Now:HH:mm:ss}] Dumped up to {limit} seeds to fertilizer DB (INSERT INTO SELECT - zero C# memory!)");
            }
            catch (Exception ex)
            {
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Fertilizer dump warning: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Checkpoint fertilizer DB to persist changes
    /// </summary>
    private void CheckpointFertilizer()
    {
        lock (_fertilizerLock)
        {
            if (_fertilizerConnection == null) return;
            try
            {
                using var cmd = _fertilizerConnection.CreateCommand();
                cmd.CommandText = "CHECKPOINT";
                cmd.ExecuteNonQuery();
            }
            catch { /* ignore */ }
        }
    }

    private void LoadSavedFilters()
    {
        try
        {
            var jamlFiles = Directory.GetFiles(_filtersDir, "*.jaml");
            foreach (var file in jamlFiles)
            {
                var jaml = File.ReadAllText(file);

                // Parse the JAML to extract name, deck, stake - same logic as POST /search
                if (!JamlConfigLoader.TryLoadFromJamlString(jaml, out var config, out _))
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to parse {Path.GetFileName(file)}, skipping");
                    continue;
                }

                // Use same searchId generation as POST /search for consistency
                var filterName = ExtractFilterName(config!, jaml);
                var deck = ExtractDeckFromJaml(jaml);
                var stake = ExtractStakeFromJaml(jaml);
                var searchId = SanitizeSearchId($"{filterName}_{deck}_{stake}");

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
            if (_currentSearchId == searchId && _currentSearch != null)
            {
                _currentSearch.StartBatch = 0;
                _currentSearch.SeedsAdded = 0;
                _currentSearch.IsRunning = false;
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

        // Track if filter was updated so we skip loading stale batch position from DB
        var filterWasUpdated = isUpdated;

        _savedSearches[searchId] = new SavedSearch
        {
            Id = searchId,
            FilterJaml = filterJaml,
            Deck = deck,
            Stake = stake,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        SaveFilter(searchId, filterJaml);

        // NOTE: Running search already stopped at start of this handler via StopRunningSearchAsync()

        try
        {
            var bgConfig = config!;

            // Create or reuse background state for this search
            var bgState = (_currentSearchId == searchId && _currentSearch != null)
                ? _currentSearch
                : new BackgroundSearchState { StartBatch = 0, SeedsAdded = 0 };

            _currentSearch = bgState;
            _currentSearchId = searchId;

            // Set up DuckDB connection FIRST (before fertilizer search so we can save results)
            var dbPath = $"{searchId}.db";
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Creating DB at: {Path.GetFullPath(dbPath)}");

            // Calculate expected tally columns for schema check
            var columnNames = config!.GetColumnNames();
            var tallyColumns = columnNames.Skip(2).ToList(); // Skip 'seed' and 'score'
            var expectedColumnCount = 2 + tallyColumns.Count; // seed + score + tallies

            // If DB exists, salvage seeds before potentially recreating
            if (File.Exists(dbPath))
            {
                try
                {
                    using var checkConn = new DuckDBConnection($"Data Source={dbPath}");
                    checkConn.Open();

                    // Check if results table exists and get column count
                    using var checkCmd = checkConn.CreateCommand();
                    checkCmd.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'results'";
                    var actualColumnCount = Convert.ToInt32(checkCmd.ExecuteScalar());

                    if (actualColumnCount > 0 && actualColumnCount != expectedColumnCount)
                    {
                        _logCallback($"[{DateTime.Now:HH:mm:ss}] Schema mismatch! DB has {actualColumnCount} columns, need {expectedColumnCount}. Salvaging seeds...");

                        // Salvage seeds from old table to fertilizer DB before dropping
                        // Use INSERT INTO SELECT via ATTACH for zero C# memory usage
                        var salvageCount = 0;
                        try
                        {
                            var fertilizerFullPath = Path.GetFullPath(_fertilizerDbPath);
                            using var attachCmd = checkConn.CreateCommand();
                            attachCmd.CommandText = $"ATTACH '{fertilizerFullPath}' AS fertilizer_db";
                            attachCmd.ExecuteNonQuery();

                            using var salvageCmd = checkConn.CreateCommand();
                            salvageCmd.CommandText = "INSERT OR IGNORE INTO fertilizer_db.seeds (seed) SELECT seed FROM results";
                            salvageCount = salvageCmd.ExecuteNonQuery();

                            using var detachCmd = checkConn.CreateCommand();
                            detachCmd.CommandText = "DETACH fertilizer_db";
                            detachCmd.ExecuteNonQuery();
                        }
                        catch (Exception salvageEx)
                        {
                            _logCallback($"[{DateTime.Now:HH:mm:ss}] Salvage warning: {salvageEx.Message}");
                        }
                        _logCallback($"[{DateTime.Now:HH:mm:ss}] Salvaged {salvageCount} seeds to fertilizer DB");

                        // Drop old table so it gets recreated with correct schema
                        using var dropCmd = checkConn.CreateCommand();
                        dropCmd.CommandText = "DROP TABLE IF EXISTS results";
                        dropCmd.ExecuteNonQuery();
                        _logCallback($"[{DateTime.Now:HH:mm:ss}] Dropped old results table");
                    }
                    checkConn.Close();
                }
                catch (Exception ex)
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Could not check/salvage old DB: {ex.Message}");
                }
            }

            bgState.Connection = new DuckDBConnection($"Data Source={dbPath}");
            bgState.Connection.Open();

            // Create results table with tally columns
            using var createCmd = bgState.Connection.CreateCommand();
            var tallyColumnsDef = tallyColumns.Count > 0
                ? ", " + string.Join(", ", tallyColumns.Select((c, i) => $"tally{i} INTEGER"))
                : "";
            createCmd.CommandText = $@"
                CREATE TABLE IF NOT EXISTS results (
                    seed VARCHAR,
                    score INTEGER{tallyColumnsDef},
                    PRIMARY KEY (seed)
                )";
            createCmd.ExecuteNonQuery();

            // Create search_state table (same schema as BalatroSeedOracle)
            using var stateCmd = bgState.Connection.CreateCommand();
            stateCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS search_state (
                    id INTEGER PRIMARY KEY,
                    batch_size INTEGER,
                    last_completed_batch BIGINT,
                    updated_at TIMESTAMP
                )";
            stateCmd.ExecuteNonQuery();

            // Load persisted batch position (survives server restart!)
            // BUT if filter was updated, CLEAR the search_state in THIS DB and start fresh!
            if (filterWasUpdated)
            {
                using var clearCmd = bgState.Connection.CreateCommand();
                clearCmd.CommandText = "DELETE FROM search_state";
                clearCmd.ExecuteNonQuery();
                bgState.StartBatch = 0;
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Filter changed - cleared search_state, starting from batch 0");
            }
            else
            {
                using var loadCmd = bgState.Connection.CreateCommand();
                loadCmd.CommandText = "SELECT last_completed_batch FROM search_state WHERE id = 1";
                var savedBatch = loadCmd.ExecuteScalar();
                if (savedBatch != null && savedBatch != DBNull.Value)
                {
                    bgState.StartBatch = Convert.ToInt64(savedBatch);
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Restored batch position: {bgState.StartBatch}");
                }
            }

            // Allow user override of start batch (manual jump to any batch number)
            if (searchRequest?.StartBatch.HasValue == true)
            {
                bgState.StartBatch = searchRequest.StartBatch.Value;
                _logCallback($"[{DateTime.Now:HH:mm:ss}] USER OVERRIDE: Starting at batch {bgState.StartBatch}");

                // IMMEDIATELY save override to DuckDB so it persists!
                using var overrideSaveCmd = bgState.Connection.CreateCommand();
                overrideSaveCmd.CommandText = @"
                    INSERT INTO search_state (id, batch_size, last_completed_batch, updated_at)
                    VALUES (1, 4, ?, CURRENT_TIMESTAMP)
                    ON CONFLICT (id) DO UPDATE SET
                        last_completed_batch = excluded.last_completed_batch,
                        updated_at = excluded.updated_at";
                overrideSaveCmd.Parameters.Add(new DuckDBParameter(bgState.StartBatch));
                overrideSaveCmd.ExecuteNonQuery();
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Saved override batch position {bgState.StartBatch} to DB");
            }

            // Validate StartBatch is within range for 4-character seeds (26^4 = 456,976)
            const long maxBatches = 456_976; // 26^4
            if (bgState.StartBatch >= maxBatches)
            {
                _logCallback($"[{DateTime.Now:HH:mm:ss}] StartBatch {bgState.StartBatch} is beyond max {maxBatches} - resetting to 0");
                bgState.StartBatch = 0;

                // Clear the invalid batch position from DB
                using var clearCmd = bgState.Connection.CreateCommand();
                clearCmd.CommandText = "DELETE FROM search_state";
                clearCmd.ExecuteNonQuery();
            }

            // Create appender for saving results
            bgState.Appender = bgState.Connection.CreateAppender("results");

            // ========== FERTILIZER SEARCH ==========
            // Run this on EVERY search (new or continue) to get instant results from known good seeds
            // Uses DbList param to read directly from fertilizer.db - no permanent in-memory storage!
            var fertilizerDbFullPath = Path.GetFullPath(_fertilizerDbPath);
            var fertilizerCount = GetFertilizerCount();

            var results = new List<SearchResult>();

            if (fertilizerCount > 0 && File.Exists(fertilizerDbFullPath))
            {
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Starting fertilizer search with {fertilizerCount} seeds from {_fertilizerDbPath}...");

                var pileParams = new JsonSearchParams
                {
                    Threads = ThreadCount,
                    EnableDebug = false,
                    NoFancy = true,
                    Quiet = true,
                    DbList = fertilizerDbFullPath, // Use DuckDB directly instead of SeedList!
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

                _logCallback($"[{DateTime.Now:HH:mm:ss}] Fertilizer search: {results.Count} matched from {fertilizerCount} in pile");
            }

            var topResults = results.OrderByDescending(r => r.Score).Take(1000).ToList();

            // SAVE FERTILIZER RESULTS TO DB using SQL UPSERT (appender can't handle duplicates)
            var savedCount = 0;
            foreach (var result in topResults)
            {
                try
                {
                    using var upsertCmd = bgState.Connection!.CreateCommand();

                    // Build column names and values for upsert
                    var cols = new List<string> { "seed", "score" };
                    var vals = new List<string> { "?", "?" };
                    if (result.Tallies != null)
                    {
                        for (int i = 0; i < result.Tallies.Count; i++)
                        {
                            cols.Add($"tally{i}");
                            vals.Add("?");
                        }
                    }

                    upsertCmd.CommandText = $@"
                        INSERT INTO results ({string.Join(", ", cols)})
                        VALUES ({string.Join(", ", vals)})
                        ON CONFLICT (seed) DO UPDATE SET
                            score = excluded.score
                            {(result.Tallies != null ? ", " + string.Join(", ", Enumerable.Range(0, result.Tallies.Count).Select(i => $"tally{i} = excluded.tally{i}")) : "")}";

                    upsertCmd.Parameters.Add(new DuckDBParameter(result.Seed));
                    upsertCmd.Parameters.Add(new DuckDBParameter(result.Score));
                    if (result.Tallies != null)
                    {
                        foreach (var tallyVal in result.Tallies)
                        {
                            upsertCmd.Parameters.Add(new DuckDBParameter(tallyVal));
                        }
                    }

                    upsertCmd.ExecuteNonQuery();
                    savedCount++;
                    bgState.SeedsAdded++;
                }
                catch (Exception ex)
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Failed to upsert fertilizer result: {ex.Message}");
                }
            }
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Upserted {savedCount} fertilizer results to DB");

            // Also add fertilizer results to the fertilizer DB (they're good seeds!)
            foreach (var result in topResults)
            {
                AddSeedToFertilizer(result.Seed);
            }

            // Get pile size from DB
            var pileSize = GetFertilizerCount();

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

            // Mark as running BEFORE sending response
            bgState.IsRunning = true;

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
                        BatchSize = 4, // Use 4-character sequential search
                        StartBatch = (ulong)bgState.StartBatch,
                        EndBatch = 0, // No end limit
                        AutoCutoff = false,
                        Cutoff = smartCutoff, // Smart cutoff from fertilizer results
                        ProgressCallback = (completed, total, seedsSearched, seedsPerMs) =>
                        {
                            // Update progress state for GET /search to read
                            var newBatch = bgState.StartBatch + completed;
                            bgState.CurrentBatch = newBatch;
                            bgState.TotalBatches = total;
                            bgState.SeedsSearched = seedsSearched;
                            bgState.SeedsPerMs = seedsPerMs;

                            // POWER OUTAGE PROTECTION: Save batch position every callback
                            // DuckDB is fast enough to handle this - no need to throttle!
                            if (bgState.Connection != null)
                            {
                                try
                                {
                                    using var saveCmd = bgState.Connection.CreateCommand();
                                    saveCmd.CommandText = @"
                                        INSERT INTO search_state (id, batch_size, last_completed_batch, updated_at)
                                        VALUES (1, 4, ?, CURRENT_TIMESTAMP)
                                        ON CONFLICT (id) DO UPDATE SET
                                            last_completed_batch = excluded.last_completed_batch,
                                            updated_at = excluded.updated_at";
                                    saveCmd.Parameters.Add(new DuckDBParameter(newBatch));
                                    saveCmd.ExecuteNonQuery();
                                }
                                catch (Exception ex)
                                {
                                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Progress save warning: {ex.Message}");
                                }
                            }
                        }
                    }, (tally) => {
                        // Track seeds found regardless of save success
                        bgState.SeedsAdded++;

                        // Add to fertilizer DB (persists across restarts!)
                        AddSeedToFertilizer(tally.Seed);

                        _logCallback($"[{DateTime.Now:HH:mm:ss}] Found seed: {tally.Seed} (score: {tally.Score})");

                        // Skip DB save if search stopped or connection closed
                        if (!bgState.IsRunning || bgState.Connection == null) return;

                        // Lock around appender operations - multiple tally callbacks can fire concurrently!
                        lock (bgState)
                        {
                            if (!bgState.IsRunning || bgState.Connection == null) return;

                            try
                            {
                                // Lazily create appender (same pattern as BSO SearchInstance)
                                bgState.Appender ??= bgState.Connection.CreateAppender("results");

                                var row = bgState.Appender.CreateRow();
                                row.AppendValue(tally.Seed);
                                row.AppendValue(tally.Score);

                                if (tally.TallyColumns != null)
                                {
                                    foreach (var tallyVal in tally.TallyColumns)
                                    {
                                        row.AppendValue(tallyVal);
                                    }
                                }

                                row.EndRow();
                                // Don't flush here - flush happens in GetTopResults before SELECT
                            }
                            catch (Exception ex)
                            {
                                // Silently ignore "closed" errors during shutdown
                                if (!ex.Message.Contains("closed"))
                                {
                                    _logCallback($"[{DateTime.Now:HH:mm:ss}] DB save warning: {ex.Message}");
                                }
                            }
                        }
                    });
                    
                    bgState.Search = bgExecutor;
                    bgState.CurrentBatch = bgState.StartBatch;
                    bgState.SeedsAdded = 0; // Reset counter for this run

                    // Execute without awaiting completion - it will run in background
                    bgExecutor.Execute(awaitCompletion: false);

                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Background search started for {searchId} from batch {bgState.StartBatch}");

                    // Background search will continue running until cancelled or completed
                }
                catch (Exception ex)
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Background search error: {ex.Message}");
                    if (_currentSearch != null)
                    {
                        _currentSearch.IsRunning = false;
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

            // If THIS search is running, ask it to query its own connection (safe!)
            if (_currentSearchId == searchId && _currentSearch?.IsRunning == true)
            {
                results = _currentSearch.GetTopResults(1000);
            }

            // FALLBACK: If running search returned empty OR search not running, try the file
            if (results.Count == 0 && File.Exists(dbPath))
            {
                results = GetTopResultsFromDb(dbPath, 1000);
            }

            // Check if THIS search is running
            var isRunning = _currentSearchId == searchId && _currentSearch?.IsRunning == true;
            long currentBatch = 0;
            long totalBatches = 0;
            long seedsSearched = 0;
            double seedsPerMs = 0;
            long totalSeedsFound = results.Count; // Default to results count, but try to get actual total
            if (_currentSearchId == searchId && _currentSearch != null)
            {
                currentBatch = _currentSearch.CurrentBatch;
                totalBatches = _currentSearch.TotalBatches;
                seedsSearched = _currentSearch.SeedsSearched;
                seedsPerMs = _currentSearch.SeedsPerMs;
            }

            // Get actual total count from DB (not capped at 1000)
            if (_currentSearchId == searchId && _currentSearch?.Connection != null)
            {
                try
                {
                    using var countCmd = _currentSearch.Connection.CreateCommand();
                    countCmd.CommandText = "SELECT COUNT(*) FROM results";
                    var countResult = countCmd.ExecuteScalar();
                    if (countResult != null && countResult != DBNull.Value)
                    {
                        totalSeedsFound = Convert.ToInt64(countResult);
                    }
                }
                catch (Exception ex)
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Could not count results: {ex.Message}");
                }
            }

            // If no in-memory batch position, try to load from DuckDB (survives server restart)
            if (currentBatch == 0 && File.Exists(dbPath))
            {
                try
                {
                    using var batchConn = new DuckDBConnection($"Data Source={dbPath}");
                    batchConn.Open();
                    using var batchCmd = batchConn.CreateCommand();
                    batchCmd.CommandText = "SELECT last_completed_batch FROM search_state WHERE id = 1";
                    var savedBatch = batchCmd.ExecuteScalar();
                    if (savedBatch != null && savedBatch != DBNull.Value)
                    {
                        currentBatch = Convert.ToInt64(savedBatch);
                    }
                }
                catch (Exception ex)
                {
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Could not read batch from DB: {ex.Message}");
                }
            }

            // Determine status
            var status = isRunning ? "RUNNING" : "STOPPED";
            var columnNames = config!.GetColumnNames();

            // Log search status with useful info
            var speedStr = seedsPerMs >= 1000 ? $"{seedsPerMs / 1000:F1}M/s"
                : seedsPerMs > 0 ? $"{seedsPerMs * 1000:F0}/s" : "-";
            var searchedStr = seedsSearched >= 1000000 ? $"{seedsSearched / 1000000.0:F1}M"
                : seedsSearched > 0 ? $"{seedsSearched / 1000.0:F1}K" : "0";
            _logCallback($"[{DateTime.Now:HH:mm:ss}] GET /search: {status} | batch {currentBatch}/{456976} | {searchedStr} searched | {totalSeedsFound} found | {speedStr}");

            response.StatusCode = 200;
            await WriteJsonAsync(response, new
            {
                searchId = searchId,
                filterJaml = savedSearch.FilterJaml,
                deck = savedSearch.Deck,
                stake = savedSearch.Stake,
                results = results,
                total = totalSeedsFound, // Actual count from DB (not capped)
                columns = columnNames,
                status = status,
                currentBatch = currentBatch,
                totalBatches = totalBatches,
                seedsSearched = seedsSearched,
                seedsPerSecond = seedsPerMs * 1000, // Convert to per-second for UI
                seedsFound = totalSeedsFound, // Actual count from DB (not capped)
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

            if (_currentSearchId != searchId || _currentSearch == null)
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "background search not found" });
                return;
            }

            if (_currentSearch.IsRunning)
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "search already running" });
                return;
            }

            // Restart the search
            _currentSearch.IsRunning = true;
            _currentSearch.Search?.Execute(awaitCompletion: false);

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

            if (_currentSearchId != searchId || _currentSearch == null)
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "background search not found" });
                return;
            }

            if (!_currentSearch.IsRunning)
            {
                response.StatusCode = 400;
                await WriteJsonAsync(response, new { error = "search not running" });
                return;
            }

            // Stop the search - acquire lock to safely dispose appender
            lock (_currentSearch)
            {
                _currentSearch.IsRunning = false;
                if (_currentSearch.Appender != null)
                {
                    try { _currentSearch.Appender.Dispose(); } catch { }
                    _currentSearch.Appender = null;
                }
            }

            _currentSearch.Search?.Cancel();

            // Wait a moment for executor to stop
            await Task.Delay(400);

            // Save batch position to DuckDB for resume!
            try
            {
                if (_currentSearch.Connection != null)
                {
                    using var saveCmd = _currentSearch.Connection.CreateCommand();
                    saveCmd.CommandText = @"
                        INSERT INTO search_state (id, batch_size, last_completed_batch, updated_at)
                        VALUES (1, 4, ?, CURRENT_TIMESTAMP)
                        ON CONFLICT (id) DO UPDATE SET
                            last_completed_batch = excluded.last_completed_batch,
                            updated_at = excluded.updated_at";
                    saveCmd.Parameters.Add(new DuckDB.NET.Data.DuckDBParameter(_currentSearch.CurrentBatch));
                    saveCmd.ExecuteNonQuery();
                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Saved batch position {_currentSearch.CurrentBatch} to DB");

                    // Flush WAL to main DB file
                    using var checkpointCmd = _currentSearch.Connection.CreateCommand();
                    checkpointCmd.CommandText = "FORCE CHECKPOINT";
                    checkpointCmd.ExecuteNonQuery();

                    // Update StartBatch for in-memory resume
                    _currentSearch.StartBatch = _currentSearch.CurrentBatch;
                }
            }
            catch (Exception ex)
            {
                _logCallback($"[{DateTime.Now:HH:mm:ss}] Warning: Failed to save batch position: {ex.Message}");
            }

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Stopped search for {searchId} at batch {_currentSearch.CurrentBatch}");

            response.StatusCode = 200;
            await WriteJsonAsync(response, new {
                message = "search stopped",
                searchId = searchId,
                status = "stopped",
                currentBatch = _currentSearch.CurrentBatch
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

                    // Generate searchId EXACTLY like LoadSavedFilters does
                    string? searchId = null;
                    if (JamlConfigLoader.TryLoadFromJamlString(content, out var config, out _))
                    {
                        var filterName = ExtractFilterName(config!, content);
                        var deck = ExtractDeckFromJaml(content);
                        var stake = ExtractStakeFromJaml(content);
                        searchId = SanitizeSearchId($"{filterName}_{deck}_{stake}");
                    }

                    filters.Add(new
                    {
                        name = displayName,
                        filePath = fileName,
                        filterJaml = content,
                        searchId = searchId // Client uses this directly!
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

    [JsonPropertyName("startBatch")]
    public long? StartBatch { get; set; }
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
