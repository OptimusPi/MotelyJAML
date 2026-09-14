using System.Runtime.InteropServices;
using System.Text.Json;
using System.Collections.Concurrent;
using McMaster.Extensions.CommandLineUtils;
using Motely;
using Motely.Analysis;
using Motely.CLI;
using Motely.DataLake;
using Motely.Enums;
using Motely.Filters;
using Motely.Filters.Jaml;
using Motely.Filters.Native;
using Motely.SeedProviders;

partial class Program
{
    private static readonly CancellationTokenSource _cts = new();
    private const int DefaultBatchCharCount = 4;

    private static List<string> BuildKeywordInputs(
        CommandOption<string> keywordOption,
        CommandOption<string> keywordsOption
    )
    {
        var keywordInputs = new List<string>();
        if (keywordOption.HasValue())
            keywordInputs.Add(keywordOption.ParsedValue.Trim().ToUpperInvariant());

        if (keywordsOption.HasValue())
        {
            keywordInputs.AddRange(
                keywordsOption
                    .ParsedValue.Split(
                        ',',
                        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                    )
                    .Select(static k => k.Trim().ToUpperInvariant())
            );
        }

        return keywordInputs;
    }

    static bool TryParseSeedOptions(
        CommandOption<string> startSeedOption,
        CommandOption<string> stopSeedOption,
        out string? startSeed,
        out string? stopSeed,
        out string? error
    )
    {
        startSeed = null;
        stopSeed = null;
        error = null;

        if (startSeedOption.HasValue())
        {
            if (!TryParseSeedString(startSeedOption.ParsedValue, out startSeed, out var err))
            {
                error = $"Error: --startSeed: {err}";
                return false;
            }
        }
        if (stopSeedOption.HasValue())
        {
            if (!TryParseSeedString(stopSeedOption.ParsedValue, out stopSeed, out var err))
            {
                error = $"Error: --stopSeed: {err}";
                return false;
            }
        }
        return true;
    }

    /// <summary>Validates an 8-character seed and returns it normalized (upper-case, 0→O).</summary>
    static bool TryParseSeedString(string input, out string? seed, out string? error)
    {
        seed = null;
        error = null;
        var normalized = MotelyGlobals.NormalizeSeed(input);
        // Motely's sequential search ranges over full 8-char seeds (11111111 → ZZZZZZZZ),
        // so --startSeed/--stopSeed must be exactly 8 chars. No padding: a short seed
        // would silently map to a different point than the user typed.
        if (normalized.Length != MotelyGlobals.MaxSeedLength)
        {
            error =
                $"'{input}' must be exactly {MotelyGlobals.MaxSeedLength} characters (1-9, A-Z).";
            return false;
        }
        foreach (char c in normalized)
        {
            if (!MotelyGlobals.SeedDigits.Contains(c))
            {
                // '0' was already normalized to 'O' above, so it can never reach here —
                // don't tell the user "no 0" for a char that can't be 0.
                error = $"'{input}' contains invalid character '{c}'. Valid: 1-9, A-Z.";
                return false;
            }
        }
        seed = normalized;
        return true;
    }

    static void RequestTermination()
    {
        _cts.Cancel();
    }

    static void OnTermination(PosixSignalContext _)
    {
        RequestTermination();
    }

    static int Main(string[] args)
    {
        // .NET 10: runtime no longer provides default SIGTERM/SIGINT handlers (see
        // https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/sigterm-signal-handler).
        // Register handlers so Ctrl+C and termination signals cancel the search gracefully.
        using var _sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnTermination);
        using var _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnTermination);
        using var _sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnTermination);

        Console.CancelKeyPress += (ctx, e) =>
        {
            e.Cancel = true;
            RequestTermination();
        };

        var escCts = new CancellationTokenSource();
        var escThread = new Thread(() =>
            ConsoleKeyMonitor.Run(RequestTermination, PrintLatestProgressOnDemand, escCts.Token)
        )
        {
            IsBackground = true,
            Name = "Console Key Listener",
        };
        escThread.Start();

        var app = new CommandLineApplication
        {
            Name = "Motely",
            Description = "Motely - Balatro Seed Searcher",
            OptionsComparison = StringComparison.OrdinalIgnoreCase,
        };
        app.HelpOption("-?|-h|--help");

        var jamlOption = app.Option<string>(
            "--jaml <PATH>",
            "JAML config file (terse one-liners allowed)",
            CommandOptionType.SingleValue
        );
        var jsonOption = app.Option<string>(
            "--json <PATH>",
            "JSON config file (same filter bag as JAML)",
            CommandOptionType.SingleValue
        );
        var yamlOption = app.Option<string>(
            "--yaml <PATH>",
            "YAML 1.2 config file (same filter bag; no JAML terse lines)",
            CommandOptionType.SingleValue
        );
        var analyzeOption = app.Option<string>(
            "--analyze <SEED[,SEED...]>",
            "Analyze one or more seeds (comma-separated) as human-readable text, using the "
                + "legacy text-block analyzer (NOT JAMLyzer — see --glossary).",
            CommandOptionType.SingleValue
        );
        var glossaryOption = app.Option(
            "--glossary",
            "Print what JAML and JAMLyzer mean, then exit.",
            CommandOptionType.NoValue
        );
        var deckOption = app.Option<string>(
            "--deck <NAME>",
            "Deck name (Red, Blue, Yellow, Green, Black, Magic, Nebula, Checkered, Zodiac, Painted, Anaglyph, Plasma, Erratic)",
            CommandOptionType.SingleValue
        );
        var stakeOption = app.Option<string>(
            "--stake <STAKE>",
            "Stake name for analysis/search (default: White)",
            CommandOptionType.SingleValue
        );
        var threadsOption = app.Option<int>(
            "--threads <N>",
            "Thread count",
            CommandOptionType.SingleValue
        );
        var batchCharCountOption = app.Option<int>(
            "--batchCharCount <N>",
            "Sequential default search only (1–7, default 4). Ignored for --keyword/--random/--aesthetic/--source list modes.",
            CommandOptionType.SingleValue
        );
        var startBatchOption = app.Option<long>(
            "--startBatch <N>",
            "Starting batch index",
            CommandOptionType.SingleValue
        );
        var endBatchOption = app.Option<long>(
            "--endBatch <N>",
            "Ending batch index",
            CommandOptionType.SingleValue
        );
        var startPercentOption = app.Option<double>(
            "--startPercent <PCT>",
            "Sequential search: start at this percent of batch space (0–100). Ignored if --startBatch is set.",
            CommandOptionType.SingleValue
        );
        var startSeedOption = app.Option<string>(
            "--startSeed <SEED>",
            "Sequential: first seed to search (e.g. 11111111 … ZZZZZZZZ). Mutually exclusive with --startBatch/--endBatch/--startPercent.",
            CommandOptionType.SingleValue
        );
        var stopSeedOption = app.Option<string>(
            "--stopSeed <SEED>",
            "Sequential: last seed to search (inclusive, e.g. ZZZZZZZZ). Omit for full range after --startSeed.",
            CommandOptionType.SingleValue
        );
        var randomOption = app.Option<int>(
            "--random <N>",
            "Random seed count",
            CommandOptionType.SingleValue
        );
        var aestheticOption = app.Option<string>(
            "--aesthetic <NAME>",
            $"Search seeds from an aesthetic provider ({JamlAestheticParser.KnownJamlStringsDescription()}). 'all' concatenates every family in order",
            CommandOptionType.SingleValue
        );
        var collectOption = app.Option<long>(
            "--collect <N>",
            $"Collect up to N matching seeds and stop (SIMD batches may deliver a few over). Sweeps every aesthetic first ({JamlAestheticParser.KnownJamlStringsDescription()}), then sequential if still short. Replaces --findone (use --collect 1).",
            CommandOptionType.SingleValue
        );
        var sourceOption = app.Option<string>(
            "--source <NAME_OR_PATH>",
            "Seed source: .csv/.txt/.parquet/.duckdb/.db file (DuckDB reads them all), local or http(s)/s3. Seeds ride the first column.",
            CommandOptionType.SingleValue
        );
        var drownOption = app.Option(
            "--drown",
            "Cannonball into the seed lake: re-search EVERY seed ever saved — all filters' *.duckdb lakes plus any CSV/TXT under <results-path>, plus this JAML's own seeds: block — deduped. With nothing saved anywhere yet it runs the normal sequential sweep (which fills the lake).",
            CommandOptionType.NoValue
        );
        var replayOption = app.Option(
            "--replay",
            "Replay only the seeds: block of the given --jaml file — verify what's already saved, nothing more.",
            CommandOptionType.NoValue
        );
        var verifySeedsOption = app.Option(
            "--verify-seeds",
            "Alias for --replay.",
            CommandOptionType.NoValue
        );
        var resultsPathOption = app.Option<string>(
            "--results-path <PATH>",
            "Root folder of the seed lake (default: Seeds; env MOTELY_DATALAKE_PATH).",
            CommandOptionType.SingleValue
        );
        var seedsOption = app.Option<string>(
            "--seeds <LIST>",
            "Inline comma-separated seeds",
            CommandOptionType.SingleValue
        );
        var cutoffOption = app.Option<string>(
            "--cutoff <VALUE>",
            "Minimum score to print, 'auto' to learn one from an initial sequential sample, or 'off' to print every scored match.",
            CommandOptionType.SingleValue
        );
        var cutoffSampleOption = app.Option<double>(
            "--cutoff-sample <PCT>",
            "With --cutoff auto on a full sequential sweep: percent of batch space to sample before selecting and replaying a fixed score floor (default: 1).",
            CommandOptionType.SingleValue
        );
        var cutoffSampleBatchesOption = app.Option<long>(
            "--cutoff-sample-batches <N>",
            "With --cutoff auto: sample exactly N sequential batches before selecting and replaying a fixed score floor. Overrides --cutoff-sample.",
            CommandOptionType.SingleValue
        );
        var cutoffTargetOption = app.Option<long>(
            "--cutoff-target <N>",
            "With --cutoff auto: choose the lowest score projected to print at most N matches across the full sweep (default: 1000).",
            CommandOptionType.SingleValue
        );
        var keywordOption = app.Option<string>(
            "--keyword <WORD>",
            "Search seeds containing this keyword (pads to 8 chars with all valid chars)",
            CommandOptionType.SingleValue
        );
        var keywordsOption = app.Option<string>(
            "--keywords <WORDS>",
            "Comma-separated keywords, each padded to 8 chars (e.g. \"OW,OH,BOOB\")",
            CommandOptionType.SingleValue
        );
        var paddingOption = app.Option<string>(
            "--padding <CHARS>",
            "Restrict free-slot / pad chars for --keyword/--keywords and --aesthetic (e.g. \"123456789\" digits-only — words stay visible). Collect's aesthetic prepass defaults to 123456789 when this flag is omitted.",
            CommandOptionType.SingleValue
        );
        var nativeOption = app.Option<string>(
            "--native <NAME>",
            "Run a native C# filter by name (e.g. PerkeoObservatory, Observatory, Trickeoglyph, NaturalNegatives, ...). Seed-input flags match JAML: --source, --seeds, --keyword(s), --random, --aesthetic, or default sequential (--startBatch/--endBatch/--startPercent or --startSeed/--stopSeed).",
            CommandOptionType.SingleValue
        );
        var nativeRandomCountOption = app.Option<int>(
            "--native-random <N>",
            "Random seed count for --native mode",
            CommandOptionType.SingleValue
        );
        var quietOption = app.Option(
            "-q|--quiet|--no-progress",
            "Suppress per-batch progress lines and the startup preamble on stderr (stdout results unaffected).",
            CommandOptionType.NoValue
        );
        var estimateOption = app.Option(
            "--estimate",
            "Print how rare the filter is and how long it should take, then exit without searching. Computed from the game's odds, so it returns immediately.",
            CommandOptionType.NoValue
        );
        threadsOption.DefaultValue = Environment.ProcessorCount;
        // No DefaultValue here (unlike threadsOption above): CommandOption.HasValue() reports
        // true forever once a DefaultValue is set, so it could never again distinguish "user
        // typed --batchCharCount" from "didn't" — which is exactly the signal this needs to
        // decide whether to override a JAML's saved seeds: list. Default of 4 is applied at
        // each read site instead (DefaultBatchCharCount below).

        app.OnExecuteAsync(async _ =>
        {
            if (glossaryOption.HasValue())
            {
                Console.WriteLine(MotelyGlossary.Render());
                return 0;
            }

            if (args.Length == 0)
            {
                app.ShowHelp();
                return 0;
            }

            // --analyze mode — supports single seed or comma-separated batch.
            if (analyzeOption.HasValue())
            {
                var seedTokens = analyzeOption.ParsedValue.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );

                var analyzeDeck = deckOption.HasValue() ? deckOption.ParsedValue : "Erratic";
                var analyzeStake = stakeOption.HasValue() ? stakeOption.ParsedValue : "White";

                if (seedTokens.Length == 1)
                    return ExecuteAnalyze(seedTokens[0], analyzeDeck, analyzeStake);

                return ExecuteAnalyzeBatch(seedTokens, analyzeDeck, analyzeStake);
            }

            // --native mode — run a hardcoded C# filter by name
            if (nativeOption.HasValue())
                return await RunNativeMode();

            return await RunJamlMode();

            // A local function, not a method: it captures the CommandOption objects themselves, so
            // HasValue() keeps meaning "the user typed this" (see the DefaultValue note above).
            // Reading values into a parameter list would quietly destroy that distinction.
            async Task<int> RunNativeMode()
            {
                // --replay replays a JAML's seeds: block; there is no such block in native mode.
                if (replayOption.HasValue() || verifySeedsOption.HasValue())
                {
                    Console.Error.WriteLine(
                        "Error: --replay/--verify-seeds requires --jaml, --json, or --yaml."
                    );
                    return 1;
                }

                var nDeck = deckOption.HasValue()
                    ? Enum.Parse<MotelyDeck>(deckOption.ParsedValue, true)
                    : MotelyDeck.Red;
                var nStake = stakeOption.HasValue()
                    ? Enum.Parse<MotelyStake>(stakeOption.ParsedValue, true)
                    : MotelyStake.White;
                int nThreads = threadsOption.HasValue()
                    ? threadsOption.ParsedValue
                    : Environment.ProcessorCount;
                int nBatch = batchCharCountOption.HasValue()
                    ? batchCharCountOption.ParsedValue
                    : DefaultBatchCharCount;

                if (
                    !MotelyNativeFilterNames.TryParse(
                        nativeOption.ParsedValue,
                        out var nativeFilter
                    )
                )
                {
                    Console.Error.WriteLine(
                        $"Error: unknown native filter '{nativeOption.ParsedValue}'. Known: {string.Join(", ", MotelyNativeFilterNames.DisplayNames)}"
                    );
                    return 1;
                }

                IMotelySearchSettings nSettings = MotelyNativeFilterFactory.CreateSettings(
                    nativeFilter
                );

                nSettings = nSettings.WithDeck(nDeck).WithStake(nStake).WithThreadCount(nThreads);

                if (
                    !TryParseSeedOptions(
                        startSeedOption,
                        stopSeedOption,
                        out var nStartSeed,
                        out var nStopSeed,
                        out var seedOptError
                    )
                )
                {
                    Console.Error.WriteLine(seedOptError);
                    return 1;
                }

                if (
                    !CliSearchMode.TryApplySearchMode(
                        nSettings,
                        new CliSearchMode.Input(
                            SourcePath: sourceOption.HasValue() ? sourceOption.ParsedValue : null,
                            SeedsArgument: seedsOption.HasValue() ? seedsOption.ParsedValue : null,
                            Drown: drownOption.HasValue(),
                            Replay: false,
                            JamlPath: null,
                            ResultsRootPath: resultsPathOption.HasValue()
                                ? resultsPathOption.ParsedValue
                                : null,
                            FilterId: null,
                            JamlSeeds: null,
                            KeywordInputs: BuildKeywordInputs(keywordOption, keywordsOption),
                            PaddingCharsOption: paddingOption.HasValue()
                                ? paddingOption.ParsedValue
                                : null,
                            RandomCount: nativeRandomCountOption.HasValue()
                                ? nativeRandomCountOption.ParsedValue
                                : null,
                            AestheticName: aestheticOption.HasValue()
                                ? aestheticOption.ParsedValue
                                : null,
                            StartBatch: startBatchOption.HasValue()
                                ? startBatchOption.ParsedValue
                                : null,
                            EndBatch: endBatchOption.HasValue() ? endBatchOption.ParsedValue : null,
                            StartPercent: startPercentOption.HasValue()
                                ? startPercentOption.ParsedValue
                                : null,
                            StartSeed: nStartSeed,
                            StopSeed: nStopSeed,
                            BatchCharacterCount: nBatch
                        ),
                        msg => Console.Error.WriteLine(msg),
                        out var nSearchModeError,
                        out nSettings,
                        out var nSourceLifetime
                    )
                )
                {
                    Console.Error.WriteLine(nSearchModeError);
                    return 1;
                }

                using var _nSourceLifetime = nSourceLifetime;

                // Always attach a progress callback so 'p' hotkey has fresh data;
                // quiet mode just swaps in the silent capture variant.
                nSettings = nSettings
                    .WithSeedMatchCallback(StickyProgress.WriteResultLine)
                    .WithProgressCallback(
                        quietOption.HasValue() ? CaptureProgress : WriteProgressLineToStderr
                    );

                if (!quietOption.HasValue())
                    Console.Error.WriteLine(
                        $"Motely native: {nativeOption.ParsedValue} | {nDeck} {nStake} | threads={nThreads} | batchCharCount={nBatch} (sequential only)"
                    );
                using var nSearch = nSettings.Start(_cts.Token);
                await nSearch.WaitForCompletionAsync(_cts.Token);
                PrintSummary(nSearch, nBatch, _cts.Token.IsCancellationRequested);
                return _cts.Token.IsCancellationRequested ? 1 : 0;
            }

            // --jaml mode — the main path: load the filter, build the search, run the passes.
            async Task<int> RunJamlMode()
            {
                int formatFlags =
                    (jamlOption.HasValue() ? 1 : 0)
                    + (jsonOption.HasValue() ? 1 : 0)
                    + (yamlOption.HasValue() ? 1 : 0);
                if (formatFlags == 0)
                {
                    Console.Error.WriteLine(
                        "Error: --jaml <path>, --json <path>, --yaml <path>, or --native <name> required."
                    );
                    return 1;
                }
                if (formatFlags > 1)
                {
                    Console.Error.WriteLine("Error: pick one of --jaml, --json, --yaml.");
                    return 1;
                }

                // One YAML loader for all three flags; JSON is read as YAML.
                string docPath =
                    jsonOption.HasValue() ? jsonOption.ParsedValue
                    : yamlOption.HasValue() ? yamlOption.ParsedValue
                    : jamlOption.ParsedValue;

                if (
                    !JamlFileLoader.TryLoadFromPath(
                        docPath,
                        out var config,
                        out var loadError
                    )
                )
                {
                    Console.Error.WriteLine($"Error: {loadError}");
                    return 1;
                }

                var deck = config.Deck;
                var stake = config.Stake;
                bool drown = drownOption.HasValue();
                if (drown)
                {
                    // --drown with nothing saved anywhere (no lake files, no seeds: block) has
                    // no haystack; CliSearchMode degrades it to the sequential sweep. Decide
                    // that here too, so the space label and banner describe the run that
                    // actually happens rather than "the entire seed lake".
                    string drownRoot = SeedLakeSink.LakeRoot(
                        resultsPathOption.HasValue() ? resultsPathOption.ParsedValue : null
                    );
                    if (!SeedSourceProvider.HasLakeFiles(drownRoot) && config.Seeds.Count == 0)
                    {
                        drown = false;
                        Console.Error.WriteLine(
                            $"Note: nothing to drown in yet — the seed lake at '{drownRoot}' holds no seeds and the JAML has no seeds: block. Running the default sequential sweep instead; every find lands in the lake for the next --drown."
                        );
                    }
                }
                bool replay = replayOption.HasValue() || verifySeedsOption.HasValue();
                int threads = threadsOption.HasValue()
                    ? threadsOption.ParsedValue
                    : Environment.ProcessorCount;
                int batchCharCount = batchCharCountOption.HasValue()
                    ? batchCharCountOption.ParsedValue
                    : DefaultBatchCharCount;
                // Only non-null when the user actually typed --batchCharCount — an explicit request
                // for the real sequential sweep, which should override a JAML's saved seeds: list.
                int? explicitBatchCharCount = batchCharCountOption.HasValue() ? batchCharCount : null;

                // Default is auto: without --cutoff we self-tune the score gate instead of
                // emitting every seed. An explicit integer turns auto off and pins the gate.
                // One shared gate implementation with the TUI (MotelyScoreCutoff).
                MotelyScoreCutoff cutoff = MotelyScoreCutoff.Auto();
                if (cutoffOption.HasValue())
                {
                    var cutoffValue = cutoffOption.ParsedValue.Trim();
                    if (string.Equals(cutoffValue, "auto", StringComparison.OrdinalIgnoreCase))
                    {
                        cutoff = MotelyScoreCutoff.Auto();
                    }
                    else if (int.TryParse(cutoffValue, out var cutoffFixedValue))
                    {
                        cutoff = MotelyScoreCutoff.Fixed(cutoffFixedValue);
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: --cutoff must be an integer or 'auto'.");
                        return 1;
                    }
                }

                int engineCutoff = cutoff.EngineCutoff;
                JamlSearchPlan plan;
                try
                {
                    // Push fixed --cutoff into the engine so low-scoring seeds are dropped at
                    // the scorer (no callback spam, no per-seed string concat). Auto still needs
                    // the caller-side running-max below since the engine threshold is static.
                    plan = JamlSearchBuilder.CreatePlan(config, engineCutoff);
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return 1;
                }

                IMotelySearchSettings settings = plan
                    .Settings.WithDeck(deck)
                    .WithStake(stake)
                    .WithThreadCount(threads);

                if (
                    !TryParseSeedOptions(
                        startSeedOption,
                        stopSeedOption,
                        out var jStartSeed,
                        out var jStopSeed,
                        out var jSeedOptError
                    )
                )
                {
                    Console.Error.WriteLine(jSeedOptError);
                    return 1;
                }

                // --collect is parsed here rather than beside the branches that consume it: the
                // block below has to know whether this run stops after N matches or sweeps, and by
                // the time those branches run they have already rewritten the settings.
                long collectLimit = 0;
                if (collectOption.HasValue())
                {
                    collectLimit = collectOption.ParsedValue;
                    if (collectLimit < 1)
                    {
                        Console.Error.WriteLine("--collect N requires N >= 1.");
                        return 1;
                    }
                }

                // Naming an explicit sequential range (--startBatch/--endBatch/--startPercent/
                // --startSeed/--stopSeed) says you want the sweep itself, so --collect skips the
                // aesthetic pass entirely rather than answering a different question than you asked.
                bool collectSequentialOnly =
                    startBatchOption.HasValue()
                    || endBatchOption.HasValue()
                    || startPercentOption.HasValue()
                    || startSeedOption.HasValue()
                    || stopSeedOption.HasValue();

                bool namedExplicitSeedInput =
                    keywordOption.HasValue()
                    || keywordsOption.HasValue()
                    || aestheticOption.HasValue()
                    || sourceOption.HasValue()
                    || seedsOption.HasValue()
                    || randomOption.HasValue()
                    || drown
                    || replay;

                double cutoffSamplePercent = cutoffSampleOption.HasValue()
                    ? cutoffSampleOption.ParsedValue
                    : 1.0;
                if (cutoffSamplePercent <= 0 || cutoffSamplePercent > 100)
                {
                    Console.Error.WriteLine("--cutoff-sample must be greater than 0 and at most 100.");
                    return 1;
                }

                long cutoffTarget = cutoffTargetOption.HasValue()
                    ? cutoffTargetOption.ParsedValue
                    : 1000;
                if (cutoffTarget < 1)
                {
                    Console.Error.WriteLine("--cutoff-target requires N >= 1.");
                    return 1;
                }

                if (cutoffSampleBatchesOption.HasValue() && cutoffSampleBatchesOption.ParsedValue < 1)
                {
                    Console.Error.WriteLine("--cutoff-sample-batches requires N >= 1.");
                    return 1;
                }

                // Only the untouched sequential sweep can state its size. Every other mode draws
                // from a list whose length is not known until it loads, or from a slice this block
                // would have to re-derive from batch indices — so they name the mode instead of
                // inventing a count, and the report drops the odds lines rather than guessing.
                JamlSearchSpace searchSpace =
                    namedExplicitSeedInput
                        ? new JamlSearchSpace(
                            -1,
                            drown ? "the entire seed lake"
                            : replay ? "the JAML seeds: block"
                            : "an explicitly named seed set"
                        )
                    : collectSequentialOnly ? new JamlSearchSpace(-1, "a narrowed sequential range")
                    : collectLimit > 0 ? new JamlSearchSpace(-1, "every aesthetic, then sequential")
                    : new JamlSearchSpace(
                        JamlRarityReport.FullSequentialSeedSpace,
                        "full sequential sweep"
                    );

                // stderr, not stdout: `--jaml x -q > seeds.txt` must produce seeds and nothing else.
                // --estimate prints even under --quiet, since printing this is the whole request.
                if (!quietOption.HasValue())
                {

                    foreach (
                        string line in JamlRarityReport.Render(
                            JamlRarityEstimator.Estimate(config),
                            searchSpace,
                            config.SimdCostPerSeed(),
                            config.EstimateFilterCrunches(),
                            collectLimit
                        )
                    )
                    {
                        Console.Error.WriteLine(line);
                    }
                }

                // Nothing disposable exists yet, so this exit unwinds cleanly. It costs one probe
                // batch — under a second — which is what turns the estimate into a number.
                if (estimateOption.HasValue())
                    return _cts.Token.IsCancellationRequested ? 1 : 0;

                if (
                    !CliSearchMode.TryApplySearchMode(
                        settings,
                        new CliSearchMode.Input(
                            SourcePath: sourceOption.HasValue() ? sourceOption.ParsedValue : null,
                            SeedsArgument: seedsOption.HasValue() ? seedsOption.ParsedValue : null,
                            Drown: drown,
                            Replay: replay,
                            JamlPath: docPath,
                            ResultsRootPath: resultsPathOption.HasValue()
                                ? resultsPathOption.ParsedValue
                                : null,
                            FilterId: config.Id,
                            JamlSeeds: config.Seeds,
                            KeywordInputs: BuildKeywordInputs(keywordOption, keywordsOption),
                            PaddingCharsOption: paddingOption.HasValue()
                                ? paddingOption.ParsedValue
                                : null,
                            RandomCount: randomOption.HasValue() ? randomOption.ParsedValue : null,
                            AestheticName: aestheticOption.HasValue()
                                ? aestheticOption.ParsedValue
                                : null,
                            StartBatch: startBatchOption.HasValue()
                                ? startBatchOption.ParsedValue
                                : null,
                            EndBatch: endBatchOption.HasValue() ? endBatchOption.ParsedValue : null,
                            StartPercent: startPercentOption.HasValue()
                                ? startPercentOption.ParsedValue
                                : null,
                            StartSeed: jStartSeed,
                            StopSeed: jStopSeed,
                            BatchCharacterCount: explicitBatchCharCount
                        ),
                        msg => Console.Error.WriteLine(msg),
                        out var jamlSearchModeError,
                        out settings,
                        out var jamlSourceLifetime
                    )
                )
                {
                    Console.Error.WriteLine(jamlSearchModeError);
                    return 1;
                }

                using var _jamlSourceLifetime = jamlSourceLifetime;

                // Auto sampling deliberately stays a CLI policy: the engine still only receives
                // ordinary sequential ranges and fixed score floors. It applies only where this
                // invocation owns the complete range, so a provider or user-selected slice is
                // never silently sampled and replayed.
                bool autoSampleSequential =
                    cutoff.IsAuto
                    && !namedExplicitSeedInput
                    && !collectSequentialOnly
                    && collectLimit == 0;

                bool cancelled = false;
                IMotelySearch search;

                async Task<bool> RunPass(IMotelySearch pass)
                {
                    try
                    {
                        await pass.WaitForCompletionAsync(_cts.Token);
                        return false;
                    }
                    catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
                    {
                        return true;
                    }
                }

                if (autoSampleSequential)
                {
                    long totalBatches = (long)Math.Pow(
                        MotelyGlobals.SeedDigits.Length,
                        MotelyGlobals.MaxSeedLength - batchCharCount
                    );
                    long sampleBatches = cutoffSampleBatchesOption.HasValue()
                        ? Math.Min(cutoffSampleBatchesOption.ParsedValue, totalBatches)
                        : Math.Clamp(
                            (long)Math.Ceiling(totalBatches * (cutoffSamplePercent / 100.0)),
                            1,
                            totalBatches
                        );
                    var scores = new ConcurrentDictionary<int, long>();
                    long sampledMatches = 0;

                    settings = settings
                        .WithEndBatchIndex(sampleBatches)
                        .WithAutoScoreCutoff(false)
                        .WithScoredResultCallback(tally =>
                        {
                            scores.AddOrUpdate(tally.Score, 1, static (_, count) => count + 1);
                            Interlocked.Increment(ref sampledMatches);
                        });

                    if (!quietOption.HasValue())
                    {
                        Console.Error.WriteLine(
                            $"Auto cutoff: sampling {sampleBatches:N0} / {totalBatches:N0} batches ({100.0 * sampleBatches / totalBatches:F3}%) to choose a projected output floor."
                        );
                    }

                    search = settings.Start(_cts.Token);
                    cancelled = await RunPass(search);
                    if (cancelled)
                        return 1;

                    long cumulative = 0;
                    int? selectedFloor = null;
                    long selectedSampledMatches = 0;
                    foreach (var (score, count) in scores.OrderByDescending(pair => pair.Key))
                    {
                        cumulative += count;
                        // Avoid floating point drift on trillion-seed runs: projected count <= target.
                        if (cumulative <= cutoffTarget * (double)sampleBatches / totalBatches)
                        {
                            selectedFloor = score;
                            selectedSampledMatches = cumulative;
                        }
                    }

                    if (selectedFloor is null)
                    {
                        Console.Error.WriteLine(
                            $"Auto cutoff: sample produced {sampledMatches:N0} scored matches but none fit the target of {cutoffTarget:N0}; using the best sampled score."
                        );
                        selectedFloor = scores.Count == 0 ? 0 : scores.Keys.Max();
                        selectedSampledMatches = scores.TryGetValue(selectedFloor.Value, out var count)
                            ? count
                            : 0;
                    }

                    cutoff = MotelyScoreCutoff.Fixed(selectedFloor.Value);
                    settings = settings
                        .WithStartBatchIndex(0)
                        .WithEndBatchIndex(totalBatches)
                        .WithAutoScoreCutoff(false);

                    if (!quietOption.HasValue())
                    {
                        long projected = (long)Math.Ceiling(
                            selectedSampledMatches * (double)totalBatches / sampleBatches
                        );
                        Console.Error.WriteLine(
                            $"Auto cutoff: {sampledMatches:N0} sampled matches; selected score >= {selectedFloor.Value}, projected {projected:N0} output matches (target {cutoffTarget:N0}). Replaying the sample, then continuing the full sweep."
                        );
                    }
                }

                string? lakeRoot = resultsPathOption.HasValue()
                    ? resultsPathOption.ParsedValue
                    : null;

                using var consoleSink = new ConsoleResultSink(plan.TallyLabels);
                using var persistSink = new CompositeMotelyResultSink(
                    [
                        new SeedLakeSink(lakeRoot, config.Id, plan.TallyLabels),
                        new ScoredResultsCsvSink(lakeRoot, config.Id, plan.TallyLabels),
                    ]
                );
                var saveSeedsCollector = new MotelyTopSeedSink.Collector(int.MaxValue);

                // Always attach a progress callback so 'p' hotkey stays current;
                // quiet mode swaps in the silent capture variant.
                settings = settings
                    .WithProgressCallback(
                        quietOption.HasValue() ? CaptureProgress : WriteProgressLineToStderr
                    )
                    .WithBatchBoundaryCallback(persistSink.Flush)
                    .WithAutoScoreCutoff(cutoff.IsAuto)
                    .WithScoredResultCallback(tally =>
                    {
                        if (!cutoff.ShouldEmit(tally.Score))
                            return;

                        persistSink.OnScored(in tally);
                        consoleSink.OnScored(in tally);
                        saveSeedsCollector.Consider(tally.Seed, tally.Score);
                    });

                if (!quietOption.HasValue())
                {
                    Console.Error.WriteLine(
                        $"Motely: {config.Name ?? docPath} | {deck} {stake} | threads={threads} | batchCharCount={batchCharCount} {(drown ? "| drown=entire seed lake" : replay ? "| replay=JAML seeds: block" : "(sequential only)")}"
                    );
                }

                if (collectLimit > 0 && collectSequentialOnly)
                {
                    settings = new MotelySearchIntent(
                        SequentialBatchCharacterCount: batchCharCount,
                        StopAfterMatches: collectLimit
                    ).ApplyTo(settings);
                    search = settings.Start(_cts.Token);
                    cancelled = await RunPass(search);
                }
                else if (collectLimit > 0)
                {
                    // CliSearchMode already installed keyword / aesthetic / source / random /
                    // drown / inline-seeds providers onto settings. --collect must StopAfter that
                    // intent — stomping it with the multi-aesthetic prepass is the pigeonhole
                    // (CUM hunt silently became "pretty seeds" and wiped operator seed lists).
                    // JAML seeds: alone still takes the default aesthetic collect path.
                    if (namedExplicitSeedInput)
                    {
                        settings = settings.StopAfter(collectLimit);
                        search = settings.Start(_cts.Token);
                        cancelled = await RunPass(search);
                    }
                    else
                    {
                        // Default collect: every aesthetic first (digit-pad free slots), then sequential.
                        // Full-alphabet free slots are not a "tiny corner". Override pad with --padding.
                        var aesthetics =
                            aestheticOption.HasValue()
                            && JamlAestheticParser.TryParse(
                                aestheticOption.ParsedValue.Trim(),
                                out var onlyOne
                            )
                                ? new[] { onlyOne }
                                : Enum.GetValues<JamlAesthetic>();
                        char[] collectPad = paddingOption.HasValue()
                            ? MotelyGlobals.ParsePaddingChars(paddingOption.ParsedValue)
                                ?? JamlAesthetics.QuickPaddingChars
                            : JamlAesthetics.QuickPaddingChars;
                        settings = new MotelySearchIntent(
                            Mode: MotelySearchInputMode.Aesthetic,
                            Aesthetics: aesthetics,
                            PaddingAlphabet: new string(collectPad),
                            StopAfterMatches: collectLimit
                        ).ApplyTo(settings);

                        var aestheticPass = settings.Start(_cts.Token);
                        cancelled = await RunPass(aestheticPass);

                        long remaining = collectLimit - aestheticPass.MatchingSeeds;
                        if (!cancelled && remaining > 0)
                        {
                            aestheticPass.Dispose();
                            if (!quietOption.HasValue())
                            {
                                if (aestheticPass.MatchingSeeds == 0)
                                    Console.Error.WriteLine(
                                        "No aesthetic seed matched — falling back to the sequential sweep."
                                    );
                                else
                                    Console.Error.WriteLine(
                                        $"Collected {aestheticPass.MatchingSeeds}/{collectLimit} from aesthetics — sequential for the rest."
                                    );
                            }

                            settings = new MotelySearchIntent(
                                SequentialBatchCharacterCount: batchCharCount,
                                StopAfterMatches: remaining
                            ).ApplyTo(settings);
                            aestheticPass = settings.Start(_cts.Token);
                            cancelled = await RunPass(aestheticPass);
                        }
                        else if (!cancelled && !quietOption.HasValue())
                        {
                            Console.Error.WriteLine(
                                $"Collected {aestheticPass.MatchingSeeds} from aesthetics — no sequential sweep needed."
                            );
                        }

                        search = aestheticPass;
                    }
                }
                else
                {
                    search = settings.Start(_cts.Token);
                    cancelled = await RunPass(search);
                }

                using var _search = search;

                cancelled |= _cts.Token.IsCancellationRequested;
                {
                    var seedsToSave = saveSeedsCollector.GetSeeds();

                    if (JamlFileLoader.TrySaveSeeds(docPath, seedsToSave, out var saveError))
                        Console.Error.WriteLine(
                            $"Saved {seedsToSave.Count:N0} seed(s) into top-level seeds: in {docPath}"
                        );
                    else
                        Console.Error.WriteLine(
                            $"Warning: could not save seeds back into JAML: {saveError}"
                        );
                }

                PrintSummary(search, batchCharCount, cancelled);
                return cancelled ? 1 : 0;
            }
        });

        try
        {
            return app.Execute(args);
        }
        catch (CommandParsingException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            escCts.Cancel();
        }
    }

    // ── Summary ──

    static void PrintSummary(IMotelySearch search, int batchCharCount, bool cancelled)
    {
        StickyProgress.Clear();
        Console.Out.Flush();
        Console.WriteLine();
        Console.WriteLine(cancelled ? "STOPPED" : "COMPLETED");
        var elapsed = TimeSpan.FromMilliseconds(search.ElapsedMs);
        long seeds = search.TotalSeedsSearched;
        long matches = search.MatchingSeeds;

        // Three separate numbers, never divided into each other: seeds looked at, wall-clock the
        // run took, and throughput — the sum of each thread's own seeds ÷ its own running time,
        // so idle/waiting threads don't dilute it. A StopAfter run quit on purpose mid-batch;
        // its seeds and rate are still real, it just also gets a "found" line.
        if (search.StoppedOnMatchLimit)
            Console.WriteLine($"  Found: {matches:N0} seed(s) (StopAfter; SIMD/thread overshoot ok)");
        Console.WriteLine($"  Seeds: {seeds:N0} searched, {matches:N0} matched");
        Console.WriteLine($"  Time:  {elapsed:hh\\:mm\\:ss\\.fff}");
        Console.WriteLine($"  Speed: {JamlRarityReport.Speed(search.SeedsPerSecond)}");
        if (search.IsSequentialBatchSearch)
        {
            long max = search.TotalBatchCount;
            double pct = max > 0 ? (double)search.CompletedBatchCount * 100.0 / max : 0;
            Console.WriteLine($"  Batch: {search.CompletedBatchCount:N0} / {max:N0} ({pct:F4}%)");
            if (cancelled)
            {
                long nextBatch = search.ResumeBatchIndex;
                Console.WriteLine($"  Resume: --startBatch {nextBatch}");
                if (nextBatch >= 0 && nextBatch < max)
                {
                    string minSeedInBatch = SeedMath.BatchIndexToFirstSeed(
                        nextBatch,
                        batchCharCount
                    );
                    Console.WriteLine($"  Resume: --startSeed {minSeedInBatch}");
                }
            }
        }
    }

    // ── Analyze ──

    static int ExecuteAnalyze(string seed, string deckName, string stakeName) =>
        ExecuteAnalyzeBatch([seed], deckName, stakeName);

    static int ExecuteAnalyzeBatch(string[] seeds, string deckName, string stakeName)
    {
        if (!Enum.TryParse<MotelyDeck>(deckName, true, out var d))
        {
            Console.Error.WriteLine($"Error: invalid deck '{deckName}'.");
            return 1;
        }
        if (!Enum.TryParse<MotelyStake>(stakeName, true, out var s))
        {
            Console.Error.WriteLine($"Error: invalid stake '{stakeName}'.");
            return 1;
        }

        foreach (var rawSeed in seeds)
        {
            var seed = MotelyGlobals.NormalizeSeed(rawSeed);

            var analysis = MotelyUnitTestAnalyzer.Analyze(new(seed, d, s));
            if (!string.IsNullOrEmpty(analysis.Error))
            {
                Console.Error.WriteLine($"[ERROR] {seed}: {analysis.Error}");
                return 1;
            }

            Console.WriteLine($"=== {seed} | {d} {s} ===");
            Console.Write(analysis);
            Console.WriteLine();
        }

        return 0;
    }

    // Cached latest progress so 'p' key can print on demand even under --quiet.
    static MotelyProgress? _latestProgress;
    static int _lastProgressPercent = -1;

    static void WriteProgressLineToStderr(MotelyProgress p)
    {
        _latestProgress = p;
        int pct = (int)p.PercentComplete;
        if (pct <= _lastProgressPercent)
            return;
        _lastProgressPercent = pct;
        FormatProgressToStderr(p);
    }

    static void CaptureProgress(MotelyProgress p) => _latestProgress = p;

    static void PrintLatestProgressOnDemand()
    {
        if (_latestProgress is { } p)
            FormatProgressToStderr(p);
    }

    static void FormatProgressToStderr(MotelyProgress p)
    {
        // Same formatter as the rarity projection and the final summary, so the three agree.
        string speed = JamlRarityReport.Speed(p.SeedsPerMillisecond * 1000.0);
        string eta =
            p.EstimatedTimeRemainingMilliseconds is long etaMs && etaMs > 0
                ? $" | ETA {FormatEtaMs(etaMs)}"
                : "";
        string elapsed = TimeSpan
            .FromMilliseconds(p.ElapsedMilliseconds)
            .ToString(@"hh\:mm\:ss\.f");
        StickyProgress.Update(
            $"Progress: {p.PercentComplete:F1}% | {p.SeedsSearched:N0} searched | {p.MatchingSeeds:N0} matches | {speed}{eta} | {elapsed}"
        );
    }

    static string FormatEtaMs(long milliseconds)
    {
        var rem = TimeSpan.FromMilliseconds(milliseconds);
        return rem.TotalHours >= 24 ? rem.ToString(@"d\.hh\:mm\:ss") : rem.ToString(@"hh\:mm\:ss");
    }
}
