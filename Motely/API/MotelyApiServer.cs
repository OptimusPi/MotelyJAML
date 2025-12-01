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

    private static readonly ConcurrentDictionary<string, SearchResult> _fertilizerCache = new();
    private static readonly ConcurrentDictionary<string, SavedSearch> _savedSearches = new();
    private static readonly ConcurrentDictionary<string, BackgroundSearchState> _backgroundSearches = new();

    public bool IsRunning => _listener?.IsListening ?? false;
    public string Url => $"http://{_host}:{_port}/";

    public MotelyApiServer(
        string host = "localhost",
        int port = 3141,
        Action<string>? logCallback = null
    )
    {
        _host = host;
        _port = port;
        _logCallback = logCallback ?? Console.WriteLine;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_listener != null)
            throw new InvalidOperationException("Server is already running");

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
            else if (request.HttpMethod == "POST" && path == "/analyze")
            {
                response.ContentType = "application/json";
                await HandleAnalyzeAsync(request, response);
            }
            else if (request.HttpMethod == "POST" && path == "/genie")
            {
                response.ContentType = "application/json";
                await HandleGenieAsync(request, response);
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
        response.ContentType = "text/html";
        response.StatusCode = 200;

        var html =
            @"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Motely Seed Search - Balatro Seed Oracle</title>
    <style>
        @font-face {
            font-family: 'm6x11';
            src: url('https://cdn.jsdelivr.net/gh/fonts/m6x11/m6x11.woff2') format('woff2');
        }
        
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body {
            font-family: 'm6x11', 'Courier New', monospace;
            background: #1a1818;
            color: #fff;
            padding: 10px;
            line-height: 1.4;
        }
        
        .container {
            max-width: 1400px;
            margin: 0 auto;
        }
        
        h1 {
            text-align: center;
            color: #ff4c40;
            font-size: 2.5em;
            margin-bottom: 5px;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.5);
        }
        
        .subtitle {
            text-align: center;
            color: #0093ff;
            margin-bottom: 20px;
            font-size: 1.1em;
        }
        
        .modal {
            background: #3e3d42;
            border-radius: 12px;
            padding: 20px;
            margin-bottom: 20px;
            box-shadow: 0 8px 16px rgba(0,0,0,0.4);
        }
        
        .tabs {
            display: flex;
            gap: 5px;
            margin-bottom: 15px;
            justify-content: center;
            flex-wrap: wrap;
        }
        
        .tab {
            background: #ff4c40;
            color: #fff;
            border: none;
            padding: 12px 24px;
            font-size: 1.1em;
            font-family: 'm6x11', 'Courier New', monospace;
            cursor: pointer;
            border-radius: 6px;
            transition: all 0.2s;
            font-weight: bold;
            text-transform: uppercase;
        }
        
        .tab:hover {
            background: #ff6c60;
            transform: translateY(-2px);
        }
        
        .tab.active {
            background: #cc3c30;
            box-shadow: inset 0 2px 4px rgba(0,0,0,0.3);
        }
        
        .tab-content {
            display: none;
            animation: fadeIn 0.3s;
        }
        
        .tab-content.active {
            display: block;
        }
        
        @keyframes fadeIn {
            from { opacity: 0; }
            to { opacity: 1; }
        }
        
        label {
            display: block;
            color: #fff;
            margin-bottom: 8px;
            font-weight: bold;
            font-size: 1.05em;
        }
        
        textarea, input, select {
            width: 100%;
            background: #1a1818;
            color: #fff;
            border: 2px solid #0093ff;
            padding: 12px;
            margin-bottom: 15px;
            font-family: 'm6x11', 'Courier New', monospace;
            border-radius: 6px;
            font-size: 1em;
        }
        
        textarea {
            min-height: 200px;
            resize: vertical;
        }
        
        textarea:focus, input:focus, select:focus {
            outline: none;
            border-color: #00c3ff;
        }
        
        .button-row {
            display: flex;
            gap: 10px;
            justify-content: center;
            flex-wrap: wrap;
            margin-top: 20px;
        }
        
        button {
            background: #ff4c40;
            color: #fff;
            border: none;
            padding: 14px 32px;
            font-size: 1.2em;
            font-family: 'm6x11', 'Courier New', monospace;
            font-weight: bold;
            cursor: pointer;
            border-radius: 6px;
            transition: all 0.2s;
            text-transform: uppercase;
        }
        
        button:hover:not(:disabled) {
            background: #ff6c60;
            transform: translateY(-2px);
        }
        
        button:active:not(:disabled) {
            background: #cc3c30;
            transform: translateY(0);
        }
        
        button:disabled {
            background: #666;
            cursor: not-allowed;
            opacity: 0.6;
        }
        
        .button-blue {
            background: #0093ff;
        }
        
        .button-blue:hover:not(:disabled) {
            background: #00a3ff;
        }
        
        .button-green {
            background: #00c851;
        }
        
        .button-green:hover:not(:disabled) {
            background: #00d861;
        }
        
        .button-orange {
            background: #ff9500;
        }
        
        .button-orange:hover:not(:disabled) {
            background: #ffa520;
        }
        
        .results-section {
            background: #3e3d42;
            border-radius: 12px;
            padding: 20px;
            margin-top: 20px;
        }
        
        .results-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 15px;
            flex-wrap: wrap;
            gap: 10px;
        }
        
        .results-title {
            color: #ff4c40;
            font-size: 1.5em;
        }
        
        .results-table {
            width: 100%;
            border-collapse: collapse;
            background: #1a1818;
            border-radius: 6px;
            overflow: hidden;
        }
        
        .results-table th {
            background: #ff4c40;
            color: #fff;
            padding: 12px;
            text-align: left;
            font-weight: bold;
        }
        
        .results-table td {
            padding: 10px 12px;
            border-bottom: 1px solid #3e3d42;
        }
        
        .results-table tr:hover {
            background: #2a2828;
            cursor: pointer;
        }
        
        .seed-cell {
            color: #0093ff;
            font-weight: bold;
        }
        
        .score-cell {
            color: #00ff88;
            font-weight: bold;
        }
        
        .info-text {
            color: #aaa;
            font-size: 0.95em;
            margin-top: 10px;
        }
        
        .loading {
            text-align: center;
            color: #0093ff;
            font-size: 1.2em;
            padding: 20px;
        }
        
        .error {
            background: #331a1a;
            color: #ff4c40;
            padding: 12px;
            border-radius: 6px;
            margin: 10px 0;
        }
        
        @media (max-width: 768px) {
            h1 { font-size: 1.8em; }
            .tab { padding: 10px 16px; font-size: 1em; }
            button { padding: 12px 20px; font-size: 1em; }
        }
    </style>
</head>
<body>
    <div class=""container"">
        <h1>MOTELY SEED SEARCH</h1>
        <div class=""subtitle"">Balatro Seed Oracle - Find Your Perfect Run</div>

        <div class=""modal"">
            <div class=""tabs"">
                <button class=""tab active"" onclick=""switchTab('genie')"">Genie</button>
                <button class=""tab"" onclick=""switchTab('jaml')"">JAML Editor</button>
                <button class=""tab"" onclick=""switchTab('analyze')"">Analyze</button>
            </div>

            <div id=""genie-tab"" class=""tab-content active"">
                <label>Describe your ideal seed in plain English:</label>
                <textarea id=""geniePrompt"" placeholder=""Example: Find seeds with negative Perkeo in antes 1-3, plus Blueprint and Observatory""></textarea>
                <button onclick=""generateJAML()"" class=""button-blue"">Generate JAML</button>
                <div id=""genieStatus""></div>
            </div>

            <div id=""jaml-tab"" class=""tab-content"">
                <label>Filter (JAML Format):</label>
                <textarea id=""filterJaml"">must:
  - voucher: Telescope
    antes: [1, 2]
  - voucher: Observatory
    antes: [2, 3]
  - soulJoker: Perkeo
    edition: Negative
    antes: [2, 3]
should:
  - joker: Blueprint
    antes: [1, 2, 3]
    score: 100
deck: Red
stake: White</textarea>
                <label>Seed Count:</label>
                <select id=""seedCount"">
                    <option value=""1000000"">1 Million</option>
                    <option value=""10000000"">10 Million</option>
                    <option value=""100000000"">100 Million</option>
                    <option value=""1000000000"">1 Billion</option>
                </select>
                <div class=""info-text"">Types: joker, soulJoker, voucher, tarot, planet, spectral, playingCard, boss, tag</div>
            </div>

            <div id=""analyze-tab"" class=""tab-content"">
                <div id=""analyzeContent"">
                    <p class=""info-text"">Click a seed from the results below to analyze it, or enter a seed manually:</p>
                    <label>Seed:</label>
                    <input type=""text"" id=""analyzeSeed"" placeholder=""ABCD1234"" />
                    <button onclick=""analyzeSeedManual()"" class=""button-green"">Analyze Seed</button>
                </div>
                <div id=""analyzeResults""></div>
            </div>
        </div>

        <div class=""button-row"">
            <button id=""searchBtn"" onclick=""searchSeeds()"" class=""button-blue"">Search</button>
            <button id=""shareBtn"" onclick=""shareSearch()"" class=""button-orange"" disabled>Share Search</button>
        </div>

        <div class=""results-section"">
            <div class=""results-header"">
                <div class=""results-title"" id=""resultsTitle"">Results</div>
                <button onclick=""exportToCSV()"" class=""button-green"">Export CSV</button>
            </div>
            <div id=""resultsContainer""></div>
        </div>
    </div>

    <script>
        let currentSearchId = null;
        let pollInterval = null;
        let searchResults = [];
        let searchColumns = ['seed', 'score'];
        let currentDeck = 'Red';
        let currentStake = 'White';

        function switchTab(tabName) {
            document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
            document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
            
            event.target.classList.add('active');
            document.getElementById(tabName + '-tab').classList.add('active');
        }

        async function generateJAML() {
            const prompt = document.getElementById('geniePrompt').value.trim();
            const statusDiv = document.getElementById('genieStatus');

            if (!prompt) {
                statusDiv.innerHTML = '<div class=""error"">Please enter a description!</div>';
                return;
            }

            statusDiv.innerHTML = '<div class=""loading"">Genie is thinking...</div>';

            try {
                const response = await fetch('/genie', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ prompt })
                });

                const data = await response.json();

                if (!response.ok) {
                    statusDiv.innerHTML = `<div class=""error"">Genie error: ${data.error || 'Failed to generate JAML'}</div>`;
                    return;
                }

                const jaml = data.jaml;

                document.getElementById('filterJaml').value = jaml;
                switchTab('jaml');
                document.querySelector('.tab:nth-child(2)').click();

                statusDiv.innerHTML = '<div style=""color: #00ff88;"">JAML generated! Switched to editor.</div>';
            } catch (error) {
                statusDiv.innerHTML = `<div class=""error"">Genie error: ${error.message}</div>`;
            }
        }

        async function searchSeeds() {
            const filterJaml = document.getElementById('filterJaml').value;
            const seedCount = parseInt(document.getElementById('seedCount').value);
            const searchBtn = document.getElementById('searchBtn');
            const resultsContainer = document.getElementById('resultsContainer');

            if (!filterJaml.trim()) {
                resultsContainer.innerHTML = '<div class=""error"">Please enter a filter!</div>';
                return;
            }

            searchBtn.disabled = true;
            searchBtn.textContent = 'Searching...';
            resultsContainer.innerHTML = '<div class=""loading"">Searching fertilizer cache and launching background search...</div>';

            try {
                const response = await fetch('/search', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ filterJaml, seedCount })
                });

                const data = await response.json();

                if (!response.ok) {
                    resultsContainer.innerHTML = `<div class=""error"">Error: ${data.error || 'Search failed'}</div>`;
                    searchBtn.disabled = false;
                    searchBtn.textContent = 'Search';
                    return;
                }

                currentSearchId = data.searchId;
                document.getElementById('shareBtn').disabled = false;

                displayResults(data);

                startPolling();

            } catch (error) {
                resultsContainer.innerHTML = `<div class=""error"">Error: ${error.message}</div>`;
            } finally {
                searchBtn.disabled = false;
                searchBtn.textContent = 'Search';
            }
        }

        function startPolling() {
            if (pollInterval) clearInterval(pollInterval);
            
            let pollCount = 0;
            const maxPolls = 60;
            
            pollInterval = setInterval(async () => {
                if (!currentSearchId || pollCount >= maxPolls) {
                    clearInterval(pollInterval);
                    return;
                }
                
                pollCount++;
                
                try {
                    const response = await fetch(`/search?id=${currentSearchId}`);
                    const data = await response.json();
                    
                    if (response.ok) {
                        displayResults(data);
                        
                        if (!data.isBackgroundRunning) {
                            clearInterval(pollInterval);
                            document.getElementById('resultsTitle').textContent = 
                                `Results (${data.total} found, cache: ${data.cacheSize}) Done`;
                        }
                    }
                } catch (error) {
                    console.error('Poll error:', error);
                }
            }, 5000);
        }

        function displayResults(data) {
            searchResults = data.results || [];
            searchColumns = data.columns || ['seed', 'score'];
            
            const resultsContainer = document.getElementById('resultsContainer');
            const isRunning = data.isBackgroundRunning ? 'Running...' : 'Done';
            
            document.getElementById('resultsTitle').textContent = 
                `Results (${data.total} found, cache: ${data.cacheSize}) ${isRunning}`;

            if (searchResults.length === 0) {
                resultsContainer.innerHTML = '<div class=""info-text"">No results yet. Background search running...</div>';
                return;
            }

            let html = '<table class=""results-table""><thead><tr>';
            html += '<th>#</th>';
            searchColumns.forEach(col => {
                html += `<th>${col}</th>`;
            });
            html += '</tr></thead><tbody>';

            searchResults.forEach((result, i) => {
                html += `<tr onclick=""selectSeed('${result.seed}')"">`;
                html += `<td>${i + 1}</td>`;
                html += `<td class=""seed-cell"">${result.seed}</td>`;
                html += `<td class=""score-cell"">${result.score}</td>`;
                (result.tallies || []).forEach(tally => {
                    html += `<td>${tally}</td>`;
                });
                html += '</tr>';
            });

            html += '</tbody></table>';
            resultsContainer.innerHTML = html;
        }

        function selectSeed(seed) {
            document.getElementById('analyzeSeed').value = seed;
            switchTab('analyze');
            document.querySelector('.tab:nth-child(3)').click();
            analyzeSeedManual();
        }

        async function analyzeSeedManual() {
            const seed = document.getElementById('analyzeSeed').value.trim();
            const resultsDiv = document.getElementById('analyzeResults');

            if (!seed) {
                resultsDiv.innerHTML = '<div class=""error"">Please enter a seed!</div>';
                return;
            }

            resultsDiv.innerHTML = '<div class=""loading"">Analyzing seed...</div>';

            try {
                const response = await fetch('/analyze', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ seed, deck: currentDeck, stake: currentStake })
                });

                const data = await response.json();

                if (!response.ok) {
                    resultsDiv.innerHTML = `<div class=""error"">Error: ${data.error || 'Analysis failed'}</div>`;
                    return;
                }

                const blueprintUrl = `https://miaklwalker.github.io/balatro-planner/?seed=${seed}`;
                
                resultsDiv.innerHTML = `
                    <pre style=""background: #1a1818; padding: 15px; border-radius: 6px; overflow-x: auto;"">${data.analysis}</pre>
                    <div class=""button-row"" style=""margin-top: 15px;"">
                        <button onclick=""window.open('${blueprintUrl}', '_blank')"" class=""button-blue"">Open in Blueprint</button>
                        <button onclick=""copySeed('${seed}')"" class=""button-green"">Copy Seed</button>
                    </div>
                `;
            } catch (error) {
                resultsDiv.innerHTML = `<div class=""error"">Error: ${error.message}</div>`;
            }
        }

        function copySeed(seed) {
            navigator.clipboard.writeText(seed);
            alert(`Seed ${seed} copied to clipboard!`);
        }

        function shareSearch() {
            if (!currentSearchId) return;
            
            const shareUrl = `${window.location.origin}${window.location.pathname}?search=${currentSearchId}`;
            navigator.clipboard.writeText(`Search for Balatro seeds with me! ${shareUrl}`);
            alert('Share link copied to clipboard!');
        }

        function exportToCSV() {
            if (searchResults.length === 0) {
                alert('No results to export!');
                return;
            }

            let csv = searchColumns.join(',') + '\n';
            searchResults.forEach(result => {
                const row = [result.seed, result.score];
                if (result.tallies) {
                    result.tallies.forEach(t => row.push(t));
                }
                csv += row.join(',') + '\n';
            });

            const blob = new Blob([csv], { type: 'text/csv' });
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `motely-results-${new Date().toISOString().slice(0,19).replace(/:/g,'-')}.csv`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
        }

        window.addEventListener('DOMContentLoaded', () => {
            const urlParams = new URLSearchParams(window.location.search);
            const searchId = urlParams.get('search');
            
            if (searchId) {
                currentSearchId = searchId;
                document.getElementById('shareBtn').disabled = false;
                
                (async () => {
                    const response = await fetch(`/search?id=${searchId}`);
                    if (response.ok) {
                        const data = await response.json();
                        displayResults(data);
                        startPolling();
                    }
                })();
            }
        });
    </script>
</body>
</html>";

        var buffer = System.Text.Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }

    private async Task HandleSearchAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
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

        var filterName = config.GetType().Name;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var searchId = $"{filterName}_{timestamp}";

        _savedSearches[searchId] = new SavedSearch
        {
            Id = searchId,
            FilterJaml = filterJaml,
            Deck = "Red",
            Stake = "White",
            Timestamp = timestamp
        };

        var filterJson = JsonSerializer.Serialize(config);
        string? tempFilterFile = null;

        try
        {
            tempFilterFile = Path.Combine(Path.GetTempPath(), $"motely_filter_{Guid.NewGuid()}.json");
            await File.WriteAllTextAsync(tempFilterFile, filterJson);

            var cacheSeeds = _fertilizerCache.Keys.ToList();
            var results = new List<SearchResult>();

            if (cacheSeeds.Count > 0)
            {
                var cacheParams = new JsonSearchParams
                {
                    Threads = Environment.ProcessorCount,
                    BatchSize = 36,
                    StartBatch = 0,
                    EndBatch = 0,
                    EnableDebug = false,
                    NoFancy = true,
                    Quiet = true,
                    SpecificSeed = null,
                    Wordlist = cacheSeeds,
                    RandomSeeds = 0,
                    Cutoff = 0,
                    AutoCutoff = true,
                };

                Action<MotelySeedScoreTally> cacheCallback = (tally) =>
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

                var cacheExecutor = new JsonSearchExecutor(tempFilterFile, cacheParams, "json", cacheCallback);
                cacheExecutor.Execute();

                _logCallback($"[{DateTime.Now:HH:mm:ss}] Cache search: {results.Count} seeds matched from {cacheSeeds.Count} cached");
            }

            var topResults = results.OrderByDescending(r => r.Score).Take(1000).ToList();

            foreach (var result in topResults)
            {
                _fertilizerCache.TryAdd(result.Seed, result);
            }

            response.StatusCode = 200;
            await WriteJsonAsync(response, new
            {
                searchId = searchId,
                results = topResults,
                total = results.Count,
                columns = config.GetColumnNames(),
                cacheSize = _fertilizerCache.Count
            });

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Immediate response sent with {topResults.Count} results");

            var bgFilterFile = Path.Combine(Path.GetTempPath(), $"motely_bg_{searchId}.json");
            File.Copy(tempFilterFile, bgFilterFile, true);

            _ = Task.Run(async () =>
            {
                _backgroundSearches[searchId] = new BackgroundSearchState { IsRunning = true, SeedsAdded = 0 };

                try
                {
                    var bgResults = new List<SearchResult>();

                    var bgParams = new JsonSearchParams
                    {
                        Threads = Environment.ProcessorCount,
                        BatchSize = 36,
                        StartBatch = 0,
                        EndBatch = 0,
                        EnableDebug = false,
                        NoFancy = true,
                        Quiet = true,
                        SpecificSeed = null,
                        Wordlist = null,
                        RandomSeeds = (int)Math.Min(seedCount, int.MaxValue),
                        Cutoff = 0,
                        AutoCutoff = true,
                    };

                    Action<MotelySeedScoreTally> bgCallback = (tally) =>
                    {
                        lock (bgResults)
                        {
                            bgResults.Add(new SearchResult
                            {
                                Seed = tally.Seed,
                                Score = tally.Score,
                                Tallies = tally.TallyColumns
                            });
                        }
                    };

                    var bgExecutor = new JsonSearchExecutor(bgFilterFile, bgParams, "json", bgCallback);
                    bgExecutor.Execute();

                    var bgTopResults = bgResults.OrderByDescending(r => r.Score).Take(1000).ToList();
                    foreach (var result in bgTopResults)
                    {
                        _fertilizerCache.TryAdd(result.Seed, result);
                    }

                    if (_backgroundSearches.TryGetValue(searchId, out var state))
                    {
                        state.SeedsAdded = bgTopResults.Count;
                        state.IsRunning = false;
                    }

                    _logCallback($"[{DateTime.Now:HH:mm:ss}] Background search completed for {searchId}: {bgTopResults.Count} seeds added to cache");

                    try { File.Delete(bgFilterFile); } catch { }
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
        finally
        {
            if (tempFilterFile != null && File.Exists(tempFilterFile))
            {
                try { File.Delete(tempFilterFile); } catch { }
            }
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

            var cacheSeeds = _fertilizerCache.Keys.ToList();
            
            if (cacheSeeds.Count == 0)
            {
                await WriteJsonAsync(response, new
                {
                    searchId = searchId,
                    results = new List<SearchResult>(),
                    total = 0,
                    columns = config.GetColumnNames(),
                    isBackgroundRunning = _backgroundSearches.TryGetValue(searchId, out var bg) && bg.IsRunning
                });
                return;
            }

            var filterJson = JsonSerializer.Serialize(config);
            string? tempFilterFile = null;

            try
            {
                tempFilterFile = Path.Combine(Path.GetTempPath(), $"motely_filter_{Guid.NewGuid()}.json");
                await File.WriteAllTextAsync(tempFilterFile, filterJson);

                var results = new List<SearchResult>();

                var parameters = new JsonSearchParams
                {
                    Threads = Environment.ProcessorCount,
                    BatchSize = 36,
                    StartBatch = 0,
                    EndBatch = 0,
                    EnableDebug = false,
                    NoFancy = true,
                    Quiet = true,
                    SpecificSeed = null,
                    Wordlist = cacheSeeds,
                    RandomSeeds = 0,
                    Cutoff = 0,
                    AutoCutoff = true,
                };

                Action<MotelySeedScoreTally> resultCallback = (tally) =>
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

                var executor = new JsonSearchExecutor(tempFilterFile, parameters, "json", resultCallback);
                executor.Execute();

                response.StatusCode = 200;
                await WriteJsonAsync(response, new
                {
                    searchId = searchId,
                    results = results.OrderByDescending(r => r.Score).Take(1000).ToList(),
                    total = results.Count,
                    columns = config.GetColumnNames(),
                    cacheSize = _fertilizerCache.Count,
                    isBackgroundRunning = _backgroundSearches.TryGetValue(searchId, out var bgState) && bgState.IsRunning
                });
            }
            finally
            {
                if (tempFilterFile != null && File.Exists(tempFilterFile))
                {
                    try { File.Delete(tempFilterFile); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] GET Search failed: {ex.Message}");
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

    private async Task HandleGenieAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();

        var genieRequest = JsonSerializer.Deserialize<GenieRequest>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        if (genieRequest == null || string.IsNullOrWhiteSpace(genieRequest.Prompt))
        {
            response.StatusCode = 400;
            await WriteJsonAsync(response, new { error = "prompt is required" });
            return;
        }

        try
        {
            var jaml = JamlGenie.GenerateJaml(genieRequest.Prompt);

            response.StatusCode = 200;
            await WriteJsonAsync(response, new { jaml });

            _logCallback($"[{DateTime.Now:HH:mm:ss}] Genie generated JAML for: {genieRequest.Prompt.Substring(0, Math.Min(50, genieRequest.Prompt.Length))}...");
        }
        catch (Exception ex)
        {
            _logCallback($"[{DateTime.Now:HH:mm:ss}] Genie failed: {ex.Message}");
            response.StatusCode = 500;
            await WriteJsonAsync(response, new { error = ex.Message });
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

public class GenieRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";
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

    // Mapping for common alternate names/variations
    private static readonly Dictionary<string, string> NameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Soul jokers
        { "legendary", "soulJoker" },
        { "soul", "Soul" },

        // Common variations
        { "Neg", "Negative" },
        { "Poly", "Polychrome" },
        { "Holo", "Holo" },

        // Joker variations
        { "stencil", "JokerStencil" },
        { "four fingers", "FourFingers" },
        { "4 fingers", "FourFingers" },
        { "ceremonial dagger", "CeremonialDagger" },
        { "marble joker", "MarbleJoker" },
        { "loyalty card", "LoyaltyCard" },
        { "steel joker", "SteelJoker" },
        { "space joker", "SpaceJoker" },
        { "sixth sense", "SixthSense" },
        { "card sharp", "CardSharp" },
        { "midas mask", "MidasMask" },
        { "gift card", "GiftCard" },
        { "turtle bean", "TurtleBean" },
        { "to the moon", "ToTheMoon" },
        { "stone joker", "StoneJoker" },
        { "lucky cat", "LuckyCat" },
        { "diet cola", "DietCola" },
        { "trading card", "TradingCard" },
        { "flash card", "FlashCard" },
        { "spare trousers", "SpareTrousers" },
        { "mr bones", "MrBones" },
        { "sock and buskin", "SockAndBuskin" },
        { "smeared joker", "SmearedJoker" },
        { "rough gem", "RoughGem" },
        { "onyx agate", "OnyxAgate" },
        { "glass joker", "GlassJoker" },
        { "flower pot", "FlowerPot" },
        { "merry andy", "MerryAndy" },
        { "oops all 6s", "OopsAll6s" },
        { "the idol", "TheIdol" },
        { "seeing double", "SeeingDouble" },
        { "invisible joker", "InvisibleJoker" },
        { "drivers license", "DriversLicense" },
        { "driver's license", "DriversLicense" },
        { "burnt joker", "BurntJoker" },
        { "ancient joker", "AncientJoker" },
        { "baseball card", "BaseballCard" },
        { "wee joker", "WeeJoker" },
        { "hit the road", "HitTheRoad" },
        { "the duo", "TheDuo" },
        { "the trio", "TheTrio" },
        { "the family", "TheFamily" },
        { "the order", "TheOrder" },
        { "the tribe", "TheTribe" },

        // Common jokers with spaces
        { "greedy joker", "GreedyJoker" },
        { "lusty joker", "LustyJoker" },
        { "wrathful joker", "WrathfulJoker" },
        { "gluttonous joker", "GluttonousJoker" },
        { "jolly joker", "JollyJoker" },
        { "zany joker", "ZanyJoker" },
        { "mad joker", "MadJoker" },
        { "crazy joker", "CrazyJoker" },
        { "droll joker", "DrollJoker" },
        { "sly joker", "SlyJoker" },
        { "wily joker", "WilyJoker" },
        { "clever joker", "CleverJoker" },
        { "devious joker", "DeviousJoker" },
        { "crafty joker", "CraftyJoker" },
        { "half joker", "HalfJoker" },
        { "credit card", "CreditCard" },
        { "mystic summit", "MysticSummit" },
        { "eight ball", "EightBall" },
        { "8 ball", "EightBall" },
        { "raised fist", "RaisedFist" },
        { "chaos the clown", "ChaostheClown" },
        { "scary face", "ScaryFace" },
        { "abstract joker", "AbstractJoker" },
        { "delayed gratification", "DelayedGratification" },
        { "gros michel", "GrosMichel" },
        { "even steven", "EvenSteven" },
        { "odd todd", "OddTodd" },
        { "business card", "BusinessCard" },
        { "ride the bus", "RideTheBus" },
        { "ice cream", "IceCream" },
        { "blue joker", "BlueJoker" },
        { "faceless joker", "FacelessJoker" },
        { "green joker", "GreenJoker" },
        { "to do list", "ToDoList" },
        { "todo list", "ToDoList" },
        { "red card", "RedCard" },
        { "square joker", "SquareJoker" },
        { "riff raff", "RiffRaff" },
        { "reserved parking", "ReservedParking" },
        { "mail in rebate", "MailInRebate" },
        { "fortune teller", "FortuneTeller" },
        { "golden joker", "GoldenJoker" },
        { "walkie talkie", "WalkieTalkie" },
        { "smiley face", "SmileyFace" },
        { "golden ticket", "GoldenTicket" },
        { "hanging chad", "HangingChad" },
        { "shoot the moon", "ShootTheMoon" },

        // Vouchers with spaces
        { "overstock plus", "OverstockPlus" },
        { "clearance sale", "ClearanceSale" },
        { "glow up", "GlowUp" },
        { "reroll surplus", "RerollSurplus" },
        { "reroll glut", "RerollGlut" },
        { "crystal ball", "CrystalBall" },
        { "omen globe", "OmenGlobe" },
        { "nacho tong", "NachoTong" },
        { "tarot merchant", "TarotMerchant" },
        { "tarot tycoon", "TarotTycoon" },
        { "planet merchant", "PlanetMerchant" },
        { "planet tycoon", "PlanetTycoon" },
        { "seed money", "SeedMoney" },
        { "money tree", "MoneyTree" },
        { "magic trick", "MagicTrick" },
        { "directors cut", "DirectorsCut" },
        { "director's cut", "DirectorsCut" },
        { "paint brush", "PaintBrush" },

        // Tags with spaces
        { "uncommon tag", "UncommonTag" },
        { "rare tag", "RareTag" },
        { "negative tag", "NegativeTag" },
        { "foil tag", "FoilTag" },
        { "holographic tag", "HolographicTag" },
        { "polychrome tag", "PolychromeTag" },
        { "investment tag", "InvestmentTag" },
        { "voucher tag", "VoucherTag" },
        { "boss tag", "BossTag" },
        { "standard tag", "StandardTag" },
        { "charm tag", "CharmTag" },
        { "meteor tag", "MeteorTag" },
        { "buffoon tag", "BuffoonTag" },
        { "handy tag", "HandyTag" },
        { "garbage tag", "GarbageTag" },
        { "ethereal tag", "EtherealTag" },
        { "coupon tag", "CouponTag" },
        { "double tag", "DoubleTag" },
        { "juggle tag", "JuggleTag" },
        { "d6 tag", "D6Tag" },
        { "topup tag", "TopupTag" },
        { "top up tag", "TopupTag" },
        { "speed tag", "SpeedTag" },
        { "orbital tag", "OrbitalTag" },
        { "economy tag", "EconomyTag" },

        // Tarot cards with spaces
        { "the fool", "TheFool" },
        { "the magician", "TheMagician" },
        { "the high priestess", "TheHighPriestess" },
        { "high priestess", "TheHighPriestess" },
        { "the empress", "TheEmpress" },
        { "the emperor", "TheEmperor" },
        { "the hierophant", "TheHierophant" },
        { "the lovers", "TheLovers" },
        { "the chariot", "TheChariot" },
        { "the hermit", "TheHermit" },
        { "the wheel of fortune", "TheWheelOfFortune" },
        { "wheel of fortune", "TheWheelOfFortune" },
        { "the hanged man", "TheHangedMan" },
        { "hanged man", "TheHangedMan" },
        { "the devil", "TheDevil" },
        { "the tower", "TheTower" },
        { "the star", "TheStar" },
        { "the moon", "TheMoon" },
        { "the sun", "TheSun" },
        { "the world", "TheWorld" },

        // Spectrals with spaces
        { "deja vu", "DejaVu" },
        { "black hole", "BlackHole" },

        // Bosses with spaces
        { "amber acorn", "AmberAcorn" },
        { "cerulean bell", "CeruleanBell" },
        { "crimson heart", "CrimsonHeart" },
        { "verdant leaf", "VerdantLeaf" },
        { "violet vessel", "VioletVessel" },
        { "the arm", "TheArm" },
        { "the club", "TheClub" },
        { "the eye", "TheEye" },
        { "the fish", "TheFish" },
        { "the flint", "TheFlint" },
        { "the goad", "TheGoad" },
        { "the head", "TheHead" },
        { "the hook", "TheHook" },
        { "the house", "TheHouse" },
        { "the manacle", "TheManacle" },
        { "the mark", "TheMark" },
        { "the mouth", "TheMouth" },
        { "the needle", "TheNeedle" },
        { "the ox", "TheOx" },
        { "the pillar", "ThePillar" },
        { "the plant", "ThePlant" },
        { "the psychic", "ThePsychic" },
        { "the serpent", "TheSerpent" },
        { "the tooth", "TheTooth" },
        { "the wall", "TheWall" },
        { "the water", "TheWater" },
        { "the wheel", "TheWheel" },
        { "the window", "TheWindow" },

        // Planet X
        { "planet x", "PlanetX" },
    };

    public static string GenerateJaml(string prompt)
    {
        var lowerPrompt = prompt.ToLowerInvariant();
        var clauses = new List<JamlClause>();
        var deck = "Red";
        var stake = "White";

        // Extract deck
        foreach (var d in Decks)
        {
            if (lowerPrompt.Contains(d.ToLowerInvariant() + " deck") ||
                lowerPrompt.Contains(d.ToLowerInvariant() + " stake"))
            {
                if (Decks.Contains(d))
                    deck = d;
            }
        }

        // Also check for just deck names at word boundaries
        foreach (var d in Decks)
        {
            var pattern = $@"\b{d.ToLowerInvariant()}\b";
            if (System.Text.RegularExpressions.Regex.IsMatch(lowerPrompt, pattern) && d != "Red" && d != "Blue" && d != "Green" && d != "Black")
            {
                deck = d;
                break;
            }
        }

        // Extract stake
        foreach (var s in Stakes)
        {
            if (lowerPrompt.Contains(s.ToLowerInvariant() + " stake"))
            {
                stake = s;
                break;
            }
        }

        // Extract antes patterns
        var defaultAntes = ExtractAntes(lowerPrompt) ?? new List<int> { 1, 2, 3 };

        // Extract edition preference
        string? globalEdition = null;
        foreach (var edition in Editions)
        {
            if (lowerPrompt.Contains(edition.ToLowerInvariant()))
            {
                globalEdition = edition;
                break;
            }
        }

        // Process the prompt word by word looking for items
        var words = System.Text.RegularExpressions.Regex.Split(prompt, @"[\s,;]+");

        // First, try to find multi-word matches using aliases
        var processedPrompt = prompt;
        foreach (var alias in NameAliases.OrderByDescending(a => a.Key.Length))
        {
            if (processedPrompt.IndexOf(alias.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                processedPrompt = System.Text.RegularExpressions.Regex.Replace(
                    processedPrompt,
                    System.Text.RegularExpressions.Regex.Escape(alias.Key),
                    alias.Value,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
            }
        }

        // Re-split after alias replacement
        words = System.Text.RegularExpressions.Regex.Split(processedPrompt, @"[\s,;]+");

        foreach (var word in words)
        {
            var cleanWord = word.Trim();
            if (string.IsNullOrEmpty(cleanWord)) continue;

            // Check for name aliases first
            if (NameAliases.TryGetValue(cleanWord, out var aliased))
            {
                cleanWord = aliased;
            }

            // Check soul jokers
            if (SoulJokers.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "soulJoker",
                    Value = cleanWord,
                    Edition = globalEdition,
                    Antes = defaultAntes
                });
                continue;
            }

            // Check regular jokers
            if (AllJokers.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "joker",
                    Value = cleanWord,
                    Edition = globalEdition,
                    Antes = defaultAntes
                });
                continue;
            }

            // Check vouchers
            if (Vouchers.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "voucher",
                    Value = cleanWord,
                    Antes = defaultAntes
                });
                continue;
            }

            // Check tags
            if (Tags.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "tag",
                    Value = cleanWord,
                    Antes = defaultAntes
                });
                continue;
            }

            // Check tarots
            if (Tarots.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "tarot",
                    Value = cleanWord,
                    Antes = defaultAntes
                });
                continue;
            }

            // Check spectrals
            if (Spectrals.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "spectral",
                    Value = cleanWord,
                    Antes = defaultAntes
                });
                continue;
            }

            // Check planets
            if (Planets.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "planet",
                    Value = cleanWord,
                    Antes = defaultAntes
                });
                continue;
            }

            // Check bosses
            if (Bosses.Contains(cleanWord))
            {
                clauses.Add(new JamlClause
                {
                    Type = "boss",
                    Value = cleanWord,
                    Antes = defaultAntes
                });
                continue;
            }
        }

        // If no items found, provide a helpful default
        if (clauses.Count == 0)
        {
            return GenerateHelpfulDefault(prompt, deck, stake);
        }

        // Build the JAML output
        return BuildJaml(clauses, deck, stake, lowerPrompt);
    }

    private static List<int>? ExtractAntes(string prompt)
    {
        // Look for patterns like "ante 1", "antes 1-3", "ante 1, 2, 3", "early" (1-2), "late" (6-8)
        var antes = new List<int>();

        // Check for "early" / "early game"
        if (prompt.Contains("early"))
        {
            return new List<int> { 1, 2, 3 };
        }

        // Check for "mid" / "mid game"
        if (prompt.Contains("mid"))
        {
            return new List<int> { 3, 4, 5 };
        }

        // Check for "late" / "late game"
        if (prompt.Contains("late"))
        {
            return new List<int> { 6, 7, 8 };
        }

        // Look for "ante X" or "antes X"
        var anteMatch = System.Text.RegularExpressions.Regex.Match(prompt, @"antes?\s*(\d)(?:\s*[-,to]\s*(\d))?");
        if (anteMatch.Success)
        {
            var start = int.Parse(anteMatch.Groups[1].Value);
            var end = anteMatch.Groups[2].Success ? int.Parse(anteMatch.Groups[2].Value) : start;

            for (var i = start; i <= end; i++)
            {
                if (i >= 1 && i <= 8) antes.Add(i);
            }

            if (antes.Count > 0) return antes;
        }

        // Look for explicit list like "1, 2, 3" or "1-3"
        var rangeMatch = System.Text.RegularExpressions.Regex.Match(prompt, @"\b(\d)\s*-\s*(\d)\b");
        if (rangeMatch.Success)
        {
            var start = int.Parse(rangeMatch.Groups[1].Value);
            var end = int.Parse(rangeMatch.Groups[2].Value);

            for (var i = start; i <= end; i++)
            {
                if (i >= 1 && i <= 8) antes.Add(i);
            }

            if (antes.Count > 0) return antes;
        }

        return null; // Use default
    }

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
