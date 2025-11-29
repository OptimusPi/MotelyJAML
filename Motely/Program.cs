using System;
using McMaster.Extensions.CommandLineUtils;
using Motely.Analysis;
using Motely.Executors;
using Motely.Filters;
using Motely.TUI;
using Motely.Utils;

namespace Motely
{
    partial class Program
    {
        static int Main(string[] args)
        {
            // If no args provided, launch TUI
            if (args.Length == 0)
            {
                return MotelyTUI.Run();
            }

            var app = new CommandLineApplication
            {
                Name = "Motely",
                Description = "Motely - Balatro Seed Searcher",
                OptionsComparison = StringComparison.OrdinalIgnoreCase,
            };

            app.HelpOption("-?|-h|--help");

            // Core options
            var jsonOption = app.Option<string>(
                "-j|--json <JSON>",
                "JSON config file (JsonItemFilters/)",
                CommandOptionType.SingleValue
            );
            var jamlOption = app.Option<string>(
                "--jaml <JAML>",
                "JAML config file (JamlFilters/) - Joker Ante Markup Language",
                CommandOptionType.SingleValue
            );
            var analyzeOption = app.Option<string>(
                "--analyze <SEED>",
                "Analyze a specific seed",
                CommandOptionType.SingleValue
            );
            var nativeOption = app.Option<string>(
                "-n|--native <FILTER>",
                "Run built-in native filter",
                CommandOptionType.SingleValue
            );
            var scoreOption = app.Option<string>(
                "--score <JSON>",
                "Add JSON scoring to native filter",
                CommandOptionType.SingleValue
            );
            var csvScoreOption = app.Option<string>(
                "--csvScore <TYPE>",
                "Enable CSV scoring output (native for built-in)",
                CommandOptionType.SingleValue
            );
            var timeOption = app.Option<int>(
                "--time <SECONDS>",
                "Progress report interval in seconds (default: 1200)",
                CommandOptionType.SingleValue
            );

            // Search parameters
            var threadsOption = app.Option<int>(
                "--threads <COUNT>",
                "Number of threads",
                CommandOptionType.SingleValue
            );
            var batchSizeOption = app.Option<int>(
                "--batchSize <CHARS>",
                "Batch size",
                CommandOptionType.SingleValue
            );
            var startBatchOption = app.Option<long>(
                "--startBatch <INDEX>",
                "Starting batch",
                CommandOptionType.SingleValue
            );
            var endBatchOption = app.Option<long>(
                "--endBatch <INDEX>",
                "Ending batch",
                CommandOptionType.SingleValue
            );
            var startPercentOption = app.Option<int>(
                "--startPercent <PCT>",
                "Starting percent (0-100)",
                CommandOptionType.SingleValue
            );
            var endPercentOption = app.Option<int>(
                "--endPercent <PCT>",
                "Ending percent (0-100)",
                CommandOptionType.SingleValue
            );

            // Input options
            var seedOption = app.Option<string>(
                "--seed <SEED>",
                "Specific seed",
                CommandOptionType.SingleValue
            );
            var wordlistOption = app.Option<string>(
                "--wordlist <WL>",
                "Wordlist file",
                CommandOptionType.SingleValue
            );
            var keywordOption = app.Option<string>(
                "--keyword <KEYWORD>",
                "Generate from keyword",
                CommandOptionType.SingleValue
            );
            var randomOption = app.Option<int>(
                "--random <COUNT>",
                "Test with random seeds",
                CommandOptionType.SingleValue
            );

            // Game options
            var deckOption = app.Option<string>(
                "--deck <DECK>",
                "Deck to use",
                CommandOptionType.SingleValue
            );
            var stakeOption = app.Option<string>(
                "--stake <STAKE>",
                "Stake to use",
                CommandOptionType.SingleValue
            );

            // JSON specific
            var cutoffOption = app.Option<string>(
                "--cutoff <SCORE>",
                "Min score threshold",
                CommandOptionType.SingleValue
            );

            // Output options
            var debugOption = app.Option(
                "--debug",
                "Enable debug output",
                CommandOptionType.NoValue
            );
            var noFancyOption = app.Option(
                "--nofancy",
                "Suppress fancy output",
                CommandOptionType.NoValue
            );
            var quietOption = app.Option(
                "--quiet",
                "Suppress all progress output (CSV only)",
                CommandOptionType.NoValue
            );

            // Set defaults for performance options
            threadsOption.DefaultValue = Environment.ProcessorCount;
            batchSizeOption.DefaultValue = 2;
            startBatchOption.DefaultValue = 0;
            endBatchOption.DefaultValue = 0;
            startPercentOption.DefaultValue = 0;
            endPercentOption.DefaultValue = 0;
            cutoffOption.DefaultValue = "0";
            deckOption.DefaultValue = "Red";
            stakeOption.DefaultValue = "White";
            timeOption.DefaultValue = 2; // 2 seconds

            app.OnExecute(() =>
            {
                // Analyze mode takes priority
                var analyzeSeed = analyzeOption.Value();
                if (!string.IsNullOrEmpty(analyzeSeed))
                {
                    return ExecuteAnalyze(analyzeSeed, deckOption.Value()!, stakeOption.Value()!);
                }

                // Build common parameters
                var parameters = new JsonSearchParams
                {
                    Threads = threadsOption.ParsedValue,
                    BatchSize = batchSizeOption.ParsedValue,
                    StartBatch = (ulong)startBatchOption.ParsedValue,
                    EndBatch = (ulong)endBatchOption.ParsedValue,
                    EnableDebug = debugOption.HasValue(),
                    NoFancy = noFancyOption.HasValue(),
                    Quiet = quietOption.HasValue(),
                    SpecificSeed = seedOption.Value(),
                    Wordlist = wordlistOption.Value(),
                    RandomSeeds = randomOption.HasValue() ? randomOption.ParsedValue : null,
                };

                // Validate batch size
                if (parameters.BatchSize < 1 || parameters.BatchSize >= 8)
                {
                    Console.WriteLine(
                        $"❌ Error: batchSize must be between 1 and 7 (got {parameters.BatchSize})"
                    );
                    Console.WriteLine(
                        $"   batchSize represents the number of seed digits to process in parallel."
                    );
                    Console.WriteLine(
                        $"   Valid range: 1-7 (batchSize=8 creates a single 2.25 trillion seed batch)"
                    );
                    Console.WriteLine($"   Recommended: 2-4 for optimal performance");
                    return 1;
                }

                // Calculate max batches for this batch size
                long maxBatches = (long)Math.Pow(35, 8 - parameters.BatchSize);

                // Convert percent to batch if specified
                if (startPercentOption.HasValue())
                {
                    int startPct = startPercentOption.ParsedValue;
                    if (startPct < 0 || startPct > 100)
                    {
                        Console.WriteLine($"❌ Error: startPercent must be 0-100 (got {startPct})");
                        return 1;
                    }
                    parameters.StartBatch = (ulong)(maxBatches * startPct / 100);
                    if (!parameters.Quiet)
                    {
                        Console.WriteLine(
                            $"📍 Starting at {startPct}% = batch {parameters.StartBatch:N0}"
                        );
                    }
                }

                if (endPercentOption.HasValue())
                {
                    int endPct = endPercentOption.ParsedValue;
                    if (endPct < 0 || endPct > 100)
                    {
                        Console.WriteLine($"❌ Error: endPercent must be 0-100 (got {endPct})");
                        return 1;
                    }
                    parameters.EndBatch = (ulong)(maxBatches * endPct / 100);
                    if (!parameters.Quiet)
                    {
                        if (endPct == 0)
                            Console.WriteLine($"📍 Ending at ∞ (no limit)");
                        else
                            Console.WriteLine(
                                $"📍 Ending at {endPct}% = batch {parameters.EndBatch:N0}"
                            );
                    }
                }
                else if (parameters.EndBatch == 0 && startPercentOption.HasValue())
                {
                    // User specified startPercent but no end - show infinity
                    if (!parameters.Quiet)
                    {
                        Console.WriteLine($"📍 Ending at ∞ (no limit)");
                    }
                }

                // Validate batch ranges
                if ((long)parameters.EndBatch > maxBatches)
                {
                    Console.WriteLine(
                        $"❌ endBatch too large: {parameters.EndBatch} (max for batchSize {parameters.BatchSize}: {maxBatches:N0})"
                    );
                    return 1;
                }
                if (parameters.StartBatch >= parameters.EndBatch && parameters.EndBatch != 0)
                {
                    Console.WriteLine(
                        $"❌ startBatch ({parameters.StartBatch}) must be less than endBatch ({parameters.EndBatch})"
                    );
                    return 1;
                }

                // Check which mode to run
                var nativeFilter = nativeOption.Value();
                if (!string.IsNullOrEmpty(nativeFilter))
                {
                    // Native filter mode
                    var scoreConfig = scoreOption.Value();

                    // Parse cutoff for native filters with scoring or CSV scoring
                    if (!string.IsNullOrEmpty(scoreConfig))
                    {
                        var cutoffStr = cutoffOption.Value() ?? "0";
                        parameters.AutoCutoff = cutoffStr.ToLowerInvariant() == "auto";
                        parameters.Cutoff = parameters.AutoCutoff
                            ? 1
                            : (int.TryParse(cutoffStr, out var c) ? c : 0);
                    }

                    var executor = new NativeFilterExecutor(nativeFilter, parameters, scoreConfig);
                    return executor.Execute();
                }
                else
                {
                    // Config file mode (JSON/JAML)
                    var cutoffStr = cutoffOption.Value() ?? "0";
                    bool autoCutoff = cutoffStr.ToLowerInvariant() == "auto";
                    parameters.Cutoff = autoCutoff
                        ? 0
                        : (int.TryParse(cutoffStr, out var c) ? c : 0);
                    parameters.AutoCutoff = autoCutoff;

                    // Determine which config format
                    string? configName = null;
                    string? configFormat = null;

                    if (jamlOption.HasValue())
                    {
                        configName = jamlOption.Value();
                        configFormat = "jaml";
                    }
                    else
                    {
                        configName = jsonOption.Value() ?? "standard";
                        configFormat = "json";
                    }

                    var executor = new JsonSearchExecutor(configName!, parameters, configFormat);
                    return executor.Execute();
                }
            });

            return app.Execute(args);
        }

        private static int ExecuteAnalyze(string seed, string deckName, string stakeName)
        {
            if (!Enum.TryParse<MotelyDeck>(deckName, true, out var deck))
            {
                Console.WriteLine($"❌ Invalid deck: {deckName}");
                return 1;
            }

            if (!Enum.TryParse<MotelyStake>(stakeName, true, out var stake))
            {
                Console.WriteLine($"❌ Invalid stake: {stakeName}");
                return 1;
            }

            Console.WriteLine($"🔍 Analyzing seed: '{seed}' with deck: {deck}, stake: {stake}");
            var analysis = MotelySeedAnalyzer.Analyze(
                new MotelySeedAnalysisConfig(seed, deck, stake)
            );
            Console.Write(analysis);
            return 0;
        }
    }
}
