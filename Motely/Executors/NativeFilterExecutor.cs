using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Motely.Filters;
using Motely.Utils;

namespace Motely.Executors
{
    /// <summary>
    /// Executes built-in native filters (--native parameter)
    /// Handles: PerkeoObservatory, Trickeoglyph, NegativeCopy, etc.
    /// </summary>
    public class NativeFilterExecutor
    {
        private readonly string _filterName;
        private readonly string? _scoreConfig;
        private readonly JsonSearchParams _params;
        private bool _cancelled = false;
        private List<string>? _searchSeeds = null;

        public NativeFilterExecutor(
            string filterName,
            JsonSearchParams parameters,
            string? scoreConfig = null
        )
        {
            _filterName = filterName;
            _scoreConfig = scoreConfig;
            _params = parameters;
        }

        public int Execute()
        {
            DebugLogger.IsEnabled = _params.EnableDebug;
            FancyConsole.IsEnabled = !_params.NoFancy;
            // Ensure tally colors respect --nofancy
            TallyColorizer.ColorEnabled = !_params.NoFancy;

            string normalizedFilterName = _filterName
                .ToLower(System.Globalization.CultureInfo.CurrentCulture)
                .Trim();

            // Progress callback - only used in silent mode or when fancy console is disabled
            // Otherwise FancyConsole handles progress display at the bottom line
            Action<long, long, long, double>? progressCallback = null;

            DateTime lastProgressUpdate = DateTime.UtcNow;
            DateTime progressStartTime = DateTime.UtcNow;
            progressCallback = (completed, total, seedsSearched, seedsPerMs) =>
            {
                var now = DateTime.UtcNow;
                var timeSinceLastUpdate = (now - lastProgressUpdate).TotalMilliseconds;

                lastProgressUpdate = now;

                var elapsedMS = (now - progressStartTime).TotalMilliseconds;
                string timeLeftFormatted = "calculating...";
                if (total > 0 && completed > 0)
                {
                    double portionFinished = (double)completed / total;
                    double timeLeft = elapsedMS / portionFinished - elapsedMS;
                    TimeSpan timeLeftSpan = TimeSpan.FromMilliseconds(
                        Math.Min(timeLeft, TimeSpan.MaxValue.TotalMilliseconds)
                    );
                    if (timeLeftSpan.Days == 0)
                        timeLeftFormatted = $"{timeLeftSpan:hh\\:mm\\:ss}";
                    else
                        timeLeftFormatted = $"{timeLeftSpan:d\\:hh\\:mm\\:ss}";
                }
                double pct = total > 0 ? Math.Clamp(((double)completed / total) * 100, 0, 100) : 0;
                string[] spinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧"];
                var spinner = spinnerFrames[(int)(elapsedMS / 250) % spinnerFrames.Length];
                string progressLine =
                    $"{spinner} {pct:F2}% | {timeLeftFormatted} remaining | {Math.Round(seedsPerMs)} seeds/ms";
                Console.Write($"\r{progressLine}                    \r{progressLine}");
            };

            // Create the appropriate filter
            IMotelySearch search;
            try
            {
                search = CreateFilterSearch(normalizedFilterName, progressCallback);
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"❌ {ex.Message}");
                return 1;
            }

            Console.WriteLine(
                $"🔍 Running native filter: {_filterName}"
                    + (
                        !string.IsNullOrEmpty(_params.SpecificSeed)
                            ? $" on seed: {_params.SpecificSeed}"
                            : ""
                    )
                    + (!string.IsNullOrEmpty(_scoreConfig) ? $" with scoring: {_scoreConfig}" : "")
            );

            // Help identify non-determinism
            DebugLogger.Log($"Thread count: {_params.Threads}");
            DebugLogger.Log($"Batch size: {_params.BatchSize}");
            DebugLogger.Log($"Start batch: {_params.StartBatch}");
            DebugLogger.Log($"End batch: {_params.EndBatch}");

            // Setup cancellation handler
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _cancelled = true;
                Console.WriteLine("\n🛑 Stopping search...");
                // Don't dispose here - let it finish gracefully
            };

            var searchStopwatch = Stopwatch.StartNew();

            // Add debug output for batch range processing
            if (_params.StartBatch > 0 || _params.EndBatch > 0)
            {
                Console.WriteLine(
                    $"   Processing batches: {_params.StartBatch} to {_params.EndBatch}"
                );
                Console.WriteLine($"   Seeds per batch: {Math.Pow(35, _params.BatchSize):N0}");
                Console.WriteLine(
                    $"   Total seeds to search: {((_params.EndBatch - _params.StartBatch + 1) * Math.Pow(35, _params.BatchSize)):N0}"
                );
            }

            search.Start();

            // Wait for completion using polling instead of blocking
            while (search.Status != MotelySearchStatus.Completed && !_cancelled)
            {
                System.Threading.Thread.Sleep(314);
            }

            // Stop the search gracefully (if cancelled)
            if (_cancelled)
            {
                search.Dispose();
            }

            // Give threads a moment to finish printing any final results
            System.Threading.Thread.Sleep(314);

            searchStopwatch.Stop();
            PrintSummary(search, searchStopwatch.Elapsed);

            return 0;
        }

        private IMotelySearch CreateFilterSearch(
            string filterName,
            Action<long, long, long, double>? progressCallback
        )
        {
            var seeds = LoadSeeds();
            var filterDesc = GetFilterDescriptor(filterName);

            return filterDesc switch
            {
                NaNSeedFilterDesc d => BuildSearch(d, progressCallback, seeds),
                PerkeoObservatoryFilterDesc d => BuildSearch(d, progressCallback, seeds),
                ObservatoryDesc d => BuildSearch(d, progressCallback, seeds),
                PassthroughFilterDesc d => BuildSearch(d, progressCallback, seeds),
                TrickeoglyphFilterDesc d => BuildSearch(d, progressCallback, seeds),
                NegativeCopyFilterDesc d => BuildSearch(d, progressCallback, seeds),
                NegativeTagFilterDesc d => BuildSearch(d, progressCallback, seeds),
                FilledSoulFilterDesc d => BuildSearch(d, progressCallback, seeds),
                ErraticFinderDesc d => BuildSearch(d, progressCallback, seeds),
                _ => throw new ArgumentException($"Unknown filter type: {filterDesc.GetType()}"),
            };
        }

        // Single method that builds the search with ALL the common settings
        private IMotelySearch BuildSearch<TFilter>(
            IMotelySeedFilterDesc<TFilter> filterDesc,
            Action<long, long, long, double>? progressCallback,
            List<string>? seeds
        )
            where TFilter : struct, IMotelySeedFilter
        {
            var settings = new MotelySearchSettings<TFilter>(filterDesc)
                .WithThreadCount(_params.Threads)
                .WithBatchCharacterCount(_params.BatchSize);

            if (progressCallback != null)
            {
                settings = settings.WithProgressCallback(progressCallback);
            }

            settings = ApplyScoring(settings);

            // Set batch boundaries
            settings = settings.WithStartBatchIndex((long)_params.StartBatch);

            // Set end batch boundary (if user specified --endBatch, add 1 to make it inclusive)
            if (_params.EndBatch > 0)
            {
                settings = settings.WithEndBatchIndex((long)_params.EndBatch + 1);
            }
            // If endBatch=0, don't set end boundary (infinite search until Ctrl+C)

            if (_params.RandomSeeds.HasValue)
                return settings.WithRandomSearch(_params.RandomSeeds.Value).Start();
            else if (seeds != null && seeds.Count > 0)
                return settings.WithListSearch(seeds).Start();
            else
                return settings.WithSequentialSearch().Start();
        }

        private object GetFilterDescriptor(string filterName)
        {
            var normalizedName = filterName
                .ToLower(System.Globalization.CultureInfo.CurrentCulture)
                .Trim();
            DebugLogger.Log($"Loading filter descriptor for: {normalizedName}");
            return normalizedName switch
            {
                "nanseed" => new NaNSeedFilterDesc(),
                "perkeoobservatory" => new PerkeoObservatoryFilterDesc(),
                "erraticfinder" => new ErraticFinderDesc(),
                "observatory" => new ObservatoryDesc(),
                "passthrough" => new PassthroughFilterDesc(), // for testing chaining but im leaving it cuz im lazy and cuz it might be useful some day?
                "trickeoglyph" => new TrickeoglyphFilterDesc(),
                "negativecopy" => new NegativeCopyFilterDesc(),
                "negativetag" => new NegativeTagFilterDesc(),
                "filledsoul" => new FilledSoulFilterDesc(),
                _ => throw new ArgumentException($"Unknown filter: {filterName}"),
            };
        }

        private MotelySearchSettings<T> ApplyScoring<T>(MotelySearchSettings<T> settings)
            where T : struct, IMotelySeedFilter
        {
            if (string.IsNullOrEmpty(_scoreConfig))
                return settings;

            // Load the JSON config for scoring
            var config = LoadScoringConfig(_scoreConfig);

            // Print CSV header
            PrintResultsHeader(config);

            // Create scoring provider with callbacks
            Action<MotelySeedScoreTally> onResultFound = (score) =>
            {
                Console.WriteLine(
                    TallyColorizer.FormatResultLine(score.Seed, score.Score, score.TallyColumns)
                );
            };

            // Use cutoff from params if provided
            int cutoff = _params.Cutoff;
            bool autoCutoff = _params.AutoCutoff;

            var scoreDesc = new MotelyJsonSeedScoreDesc(config, cutoff, autoCutoff, onResultFound);

            return settings.WithSeedScoreProvider(scoreDesc).WithCsvOutput(true);
        }

        private MotelyJsonConfig LoadScoringConfig(string configPath)
        {
            // If rooted path, load based on extension
            if (Path.IsPathRooted(configPath) && File.Exists(configPath))
            {
                if (configPath.EndsWith(".jaml", StringComparison.OrdinalIgnoreCase))
                {
                    if (!JamlConfigLoader.TryLoadFromJaml(configPath, out var jamlConfig, out var error))
                        throw new InvalidOperationException($"JAML loading failed: {error}");
                    return jamlConfig!;
                }
                if (!MotelyJsonConfig.TryLoadFromJsonFile(configPath, out var jsonConfig))
                    throw new InvalidOperationException($"JSON loading failed: {configPath}");
                return jsonConfig;
            }

            // Try .jaml first (prefer JAML over JSON) - case-insensitive search
            string jamlFileName = configPath.EndsWith(".jaml", StringComparison.OrdinalIgnoreCase) ? configPath : configPath + ".jaml";
            string jamlDir = "JamlFilters";

            // Case-insensitive file search
            if (Directory.Exists(jamlDir))
            {
                var matchingJamlFiles = Directory.GetFiles(jamlDir, "*.jaml", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetFileName(f).Equals(jamlFileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingJamlFiles.Count > 0)
                {
                    string jamlPath = matchingJamlFiles[0];
                    if (!JamlConfigLoader.TryLoadFromJaml(jamlPath, out var jamlConfig, out var error))
                        throw new InvalidOperationException($"JAML loading failed: {error}");
                    return jamlConfig!;
                }
            }

            // Fall back to .json - case-insensitive search
            string jsonFileName = configPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? configPath : configPath + ".json";
            string jsonDir = "JsonFilters";

            if (Directory.Exists(jsonDir))
            {
                var matchingJsonFiles = Directory.GetFiles(jsonDir, "*.json", SearchOption.TopDirectoryOnly)
                    .Where(f => Path.GetFileName(f).Equals(jsonFileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingJsonFiles.Count > 0)
                {
                    string jsonPath = matchingJsonFiles[0];
                    if (!MotelyJsonConfig.TryLoadFromJsonFile(jsonPath, out var jsonConfig))
                        throw new InvalidOperationException($"JSON loading failed: {jsonPath}");
                    return jsonConfig;
                }
            }

            throw new FileNotFoundException(
                $"Scoring config not found in {jamlDir} or {jsonDir} (searched for: {jamlFileName} or {jsonFileName})"
            );
        }

        private void PrintResultsHeader(MotelyJsonConfig config)
        {
            Console.WriteLine($"# Deck: {config.Deck}, Stake: {config.Stake}");

            // ONE SOURCE OF TRUTH: Use GetColumnNames()
            var columnNames = config.GetColumnNames();
            var quotedColumns = columnNames.Select(name => $"\"{name}\"");
            Console.WriteLine(string.Join(",", quotedColumns));
        }

        private List<string>? LoadSeeds()
        {
            if (!string.IsNullOrEmpty(_params.SpecificSeed))
            {
                _searchSeeds = new List<string> { _params.SpecificSeed };
                return _searchSeeds;
            }

            if (!string.IsNullOrEmpty(_params.Wordlist))
            {
                var wordlistPath = $"WordLists/{_params.Wordlist}.txt";
                if (!File.Exists(wordlistPath))
                {
                    throw new FileNotFoundException($"Wordlist file not found: {wordlistPath}");
                }
                _searchSeeds = File.ReadAllLines(wordlistPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();
                return _searchSeeds;
            }

            return null;
        }

        private void PrintSummary(IMotelySearch search, TimeSpan duration)
        {
            Console.WriteLine(
                _cancelled ? "\n✅ Search stopped gracefully" : "\n✅ Search completed"
            );

            // Calculate actual seeds searched
            ulong totalSeedsSearched;

            if (_searchSeeds != null)
            {
                // Wordlist or specific seed mode - use actual seed count
                totalSeedsSearched = (ulong)_searchSeeds.Count;
            }
            else if (_params.RandomSeeds.HasValue)
            {
                // Random mode - use the actual random seed count
                totalSeedsSearched = (ulong)_params.RandomSeeds.Value;
            }
            else
            {
                // Sequential batch mode - calculate from batches
                ulong seedsPerBatch = (ulong)Math.Pow(35, _params.BatchSize);
                totalSeedsSearched = (ulong)search.CompletedBatchCount * seedsPerBatch;
            }

            // Calculate the actual last batch processed
            var lastBatch =
                _params.StartBatch > 0
                    ? (long)_params.StartBatch + search.CompletedBatchCount - 1
                    : search.CompletedBatchCount;

            Console.WriteLine($"   Batches completed: {search.CompletedBatchCount}");
            Console.WriteLine($"   Last batch: {lastBatch}");
            Console.WriteLine($"   Seeds searched: {totalSeedsSearched:N0}");
            Console.WriteLine($"   Seeds passed filter: {search.FilteredSeeds}");
            Console.WriteLine($"   Seeds passed cutoff: {search.MatchingSeeds}");

            if (duration.TotalMilliseconds >= 1)
            {
                var speed = (double)totalSeedsSearched / duration.TotalMilliseconds;
                Console.WriteLine($"   Duration: {duration:hh\\:mm\\:ss\\.fff}");
                Console.WriteLine($"   Speed: {speed:N0} seeds/ms");
            }
        }
    }
}
