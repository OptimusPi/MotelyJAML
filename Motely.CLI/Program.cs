using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.InteropServices;
using Motely;
using Motely.Analysis;
using Motely.CLI;
using Motely.Enums;

// motely: search a YAML filter, or analyze seeds.
// stdout carries results only (CSV rows, or --analyze text); everything else goes to stderr.
// Exit codes: 0 finished, 1 bad input, 130 interrupted.

const int DefaultBatchCharCount = CliSearchMode.DefaultBatchCharacterCount;
const int ExitInterrupted = 130;

using var cts = new CancellationTokenSource();

// The first signal stops the search cleanly (summary, resume line, --save); a second one is left
// to the runtime and ends the process on the spot.
void Interrupt(PosixSignalContext context)
{
    if (cts.IsCancellationRequested)
        return;
    context.Cancel = true;
    cts.Cancel();
}

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Interrupt);
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Interrupt);
using var sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Interrupt);

Option<string> yamlOption = new("--yaml")
{
    Description = "Filter file (YAML or JSON).",
};
Option<string> analyzeOption = new("--analyze")
{
    Description = "Print each comma-separated seed's antes: boss, voucher, tags, shop and packs.",
};
Option<MotelyDeck?> deckOption = new("--deck")
{
    Description = "Search: replaces the filter's deck. Analyze: the deck (default Red).",
};
Option<MotelyStake?> stakeOption = new("--stake")
{
    Description = "Search: replaces the filter's stake. Analyze: the stake (default White).",
};
Option<int?> threadsOption = new("--threads") { Description = "Worker threads (default: every core)." };
Option<string> seedsOption = new("--seeds") { Description = "Search exactly these comma-separated seeds." };
Option<bool> replayOption = new("--replay") { Description = "Search the filter file's own seeds: block." };
Option<string> keywordOption = new("--keyword") { Description = "Search seeds containing this word." };
Option<string> keywordsOption = new("--keywords")
{
    Description = "Search seeds containing any of these comma-separated words.",
};
Option<int?> randomOption = new("--random") { Description = "Search N random seeds." };
Option<string> aestheticOption = new("--aesthetic")
{
    Description =
        $"Search one aesthetic seed family: {MotelyAestheticParser.KnownJamlStringsDescription()} ('all' runs every family in turn).",
};
Option<string> paddingOption = new("--padding")
{
    Description = "Characters that fill the free slots of --keyword/--keywords/--aesthetic seeds (default: all of 1-9 and A-Z).",
};
Option<int?> batchCharCountOption = new("--batchCharCount")
{
    Description = $"Sequential: trailing characters swept per batch (default {DefaultBatchCharCount}).",
};
Option<long?> startBatchOption = new("--startBatch") { Description = "Sequential: first batch index." };
Option<long?> endBatchOption = new("--endBatch") { Description = "Sequential: end batch index, exclusive." };
Option<double?> startPercentOption = new("--startPercent")
{
    Description = "Sequential: start this far into the batch space.",
};
Option<string> startSeedOption = new("--startSeed")
{
    Description = "Sequential: start at the batch holding this seed.",
};
Option<string> stopSeedOption = new("--stopSeed")
{
    Description = "Sequential: end after the batch holding this seed.",
};
Option<int?> collectOption = new("--collect")
{
    Description = "Stop after N matches. Each thread finishes the batch it is in, so more than N can print.",
};
Option<string> cutoffOption = new("--cutoff")
{
    Description =
        "A score: print only seeds scoring at least that. 'auto' (default): print each seed that ties or beats the best score so far. 'off': every match prints.",
};
Option<bool> saveOption = new("--save")
{
    Description = "Merge the printed seeds into the filter file's seeds: block (its existing seeds stay first).",
};
Option<bool> quietOption = new("--quiet", "-q") { Description = "No startup line and no progress line on stderr." };

RootCommand rootCommand = new("Balatro seed search.")
{
    yamlOption,
    analyzeOption,
    deckOption,
    stakeOption,
    threadsOption,
    seedsOption,
    replayOption,
    keywordOption,
    keywordsOption,
    randomOption,
    aestheticOption,
    paddingOption,
    batchCharCountOption,
    startBatchOption,
    endBatchOption,
    startPercentOption,
    startSeedOption,
    stopSeedOption,
    collectOption,
    cutoffOption,
    saveOption,
    quietOption,
};

rootCommand.SetAction(
    async (parseResult, cancellationToken) =>
    {
        if (parseResult.GetValue(analyzeOption) is { } seedList)
            return Analyze(parseResult, seedList);
        if (parseResult.GetValue(yamlOption) is { } filterPath)
            return await SearchAsync(parseResult, filterPath, cancellationToken);
        if (args.Length > 0)
        {
            Console.Error.WriteLine("Error: give --yaml <PATH> to search, or --analyze <SEEDS>.");
            return 1;
        }
        return await rootCommand.Parse(["--help"]).InvokeAsync(cancellationToken: cancellationToken);
    }
);

var parsed = rootCommand.Parse(args);
if (parsed.Action is ParseErrorAction parseError)
    parseError.ShowHelp = false;

// The signal handlers above own Ctrl+C, so the library's own termination handling stays off.
return await parsed.InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null }, cts.Token);

int Analyze(ParseResult parseResult, string seedList)
{
    var deck = parseResult.GetValue(deckOption) ?? MotelyDeck.Red;
    var stake = parseResult.GetValue(stakeOption) ?? MotelyStake.White;

    int failed = 0;
    foreach (var raw in seedList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var seed = MotelyGlobals.NormalizeSeed(raw);
        var analysis = MotelyUnitTestAnalyzer.Analyze(new(seed, deck, stake));
        if (!string.IsNullOrEmpty(analysis.Error))
        {
            Console.Error.WriteLine($"Error: {seed}: {analysis.Error}");
            failed++;
            continue;
        }
        Console.WriteLine($"=== {seed} | {deck} {stake} ===");
        Console.Write(analysis);
        Console.WriteLine();
    }
    return failed == 0 ? 0 : 1;
}

async Task<int> SearchAsync(ParseResult parseResult, string filterPath, CancellationToken cancellationToken)
{
    var path = filterPath;
    var config = YamlConfigLoader.FromFile(path);

    string cutoffText = parseResult.GetValue(cutoffOption) ?? "auto";
    if (!MotelyScoreCutoff.TryParse(cutoffText, out var cutoff, out var cutoffError))
    {
        Console.Error.WriteLine($"Error: --cutoff: {cutoffError}");
        return 1;
    }

    JamlSearchPlan plan;
    try
    {
        // A fixed floor goes into the engine, so low scores are dropped before any callback.
        plan = MotelySearchBuilder.CreatePlan(config, cutoff.EngineCutoff);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    var deck = parseResult.GetValue(deckOption) ?? config.Deck;
    var stake = parseResult.GetValue(stakeOption) ?? config.Stake;
    int threads = parseResult.GetValue(threadsOption) ?? Environment.ProcessorCount;
    int? batchCharCountGiven = parseResult.GetValue(batchCharCountOption);
    int batchCharCount = batchCharCountGiven ?? DefaultBatchCharCount;
    int? collect = parseResult.GetValue(collectOption);
    bool replay = parseResult.GetValue(replayOption);
    string? seedsArgument = parseResult.GetValue(seedsOption);
    int? randomCount = parseResult.GetValue(randomOption);
    string? aestheticName = parseResult.GetValue(aestheticOption);
    string? startSeed = parseResult.GetValue(startSeedOption);
    string? stopSeed = parseResult.GetValue(stopSeedOption);

    List<string> keywords = [];
    if (parseResult.GetValue(keywordOption) is { } keyword)
        keywords.Add(keyword);
    if (parseResult.GetValue(keywordsOption) is { } keywordList)
        keywords.AddRange(keywordList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    if (
        !CliSearchMode.TryApplySearchMode(
            plan.Settings.WithDeck(deck).WithStake(stake).WithThreadCount(threads),
            new CliSearchMode.Input(
                SeedsArgument: seedsArgument,
                Replay: replay,
                JamlPath: path,
                FilterId: config.Id,
                JamlSeeds: config.Seeds,
                KeywordInputs: keywords,
                // Unset padding is spelled out as the whole alphabet: that is what a bare keyword
                // means, short ones included, and the space aesthetics default to.
                PaddingCharsOption: parseResult.GetValue(paddingOption) ?? new string(MotelyGlobals.SeedDigits),
                RandomCount: randomCount,
                AestheticName: aestheticName,
                StartBatch: parseResult.GetValue(startBatchOption),
                EndBatch: parseResult.GetValue(endBatchOption),
                StartPercent: parseResult.GetValue(startPercentOption),
                StartSeed: startSeed is null ? null : MotelyGlobals.NormalizeSeed(startSeed),
                StopSeed: stopSeed is null ? null : MotelyGlobals.NormalizeSeed(stopSeed),
                BatchCharacterCount: batchCharCountGiven
            ),
            static warning => Console.Error.WriteLine(warning),
            out var modeError,
            out var settings
        )
    )
    {
        Console.Error.WriteLine(modeError);
        return 1;
    }

    bool quiet = parseResult.GetValue(quietOption);
    using var sink = new ConsoleResultSink(plan.TallyLabels);
    var saved = parseResult.GetValue(saveOption) ? new MotelyTopSeedSink.Collector(int.MaxValue) : null;

    // A filter with no clauses has no score provider, and then finds arrive only on the
    // seed-match channel. With a provider they arrive on both, so listen to exactly one.
    settings = settings.SeedScoreDesc is not null
        ? settings
            .WithAutoScoreCutoff(cutoff.IsAuto)
            .WithScoredResultCallback(result =>
            {
                if (!cutoff.ShouldEmit(result.Score))
                    return;
                sink.OnScored(in result);
                saved?.Consider(result.Seed, result.Score);
            })
        : settings.WithSeedMatchCallback(line =>
        {
            sink.OnSeed(line);
            int comma = line.IndexOf(',');
            saved?.Consider(comma < 0 ? line : line[..comma], 0);
        });

    if (!quiet)
    {
        int lastPercent = -1;
        settings = settings.WithProgressCallback(progress =>
        {
            int percent = (int)progress.PercentComplete;
            if (percent <= Volatile.Read(ref lastPercent))
                return;
            Volatile.Write(ref lastPercent, percent);
            WriteProgress(progress);
        });
    }

    if (collect is { } limit)
        settings = settings.StopAfter(limit);

    if (!quiet)
    {
        string input =
            replay ? $"replay {config.Seeds?.Count ?? 0:N0} saved seeds"
            : seedsArgument is not null ? "listed seeds"
            : keywords.Count > 0 ? $"keywords {string.Join(",", keywords)}"
            : randomCount is { } random ? $"{random:N0} random seeds"
            : aestheticName is not null ? $"aesthetic {aestheticName.Trim().ToLowerInvariant()}"
            : $"sequential, batchCharCount {batchCharCount}";
        Console.Error.WriteLine(
            $"motely: {config.Name ?? path} | {deck} {stake} | threads {threads} | {input} | cutoff {cutoffText}"
                + (collect is { } n ? $" | stop after {n:N0}" : "")
        );
    }

    using var search = settings.Start(cancellationToken);
    bool interrupted = false;
    try
    {
        await search.WaitForCompletionAsync(cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        interrupted = true;
    }

    if (saved is not null)
    {
        var seeds = saved.GetSeeds();
        if (MotelyTopSeedSink.TryRewriteAndValidate(File.ReadAllText(path), seeds, out var updated, out var saveError))
        {
            File.WriteAllText(path, updated);
            Console.Error.WriteLine($"Saved {seeds.Count:N0} seed(s) into {path}");
        }
        else
            Console.Error.WriteLine($"Error: could not save seeds into {path}: {saveError}");
    }

    WriteSummary(search, batchCharCount, interrupted);
    return interrupted ? ExitInterrupted : 0;
}

static void WriteProgress(MotelyProgress progress)
{
    string eta = progress.EstimatedTimeRemainingMilliseconds is > 0 and var etaMs
        ? $" | ETA {FormatDuration(TimeSpan.FromMilliseconds(etaMs))}"
        : "";
    StickyProgress.Update(
        $"{progress.PercentComplete:F1}% | {progress.SeedsSearched:N0} searched | {progress.MatchingSeeds:N0} matched"
            + $" | {progress.SeedsPerMillisecond * 1000.0:N0} seeds/s{eta}"
            + $" | {FormatDuration(TimeSpan.FromMilliseconds(progress.ElapsedMilliseconds))}"
    );
}

static void WriteSummary(IMotelySearch search, int batchCharCount, bool interrupted)
{
    StickyProgress.Clear();
    var log = Console.Error;
    log.WriteLine();
    log.WriteLine(
        interrupted ? "Interrupted."
        : search.StoppedOnMatchLimit ? "Stopped at --collect."
        : "Finished."
    );
    // Seeds, wall-clock and throughput are three separate measurements; none is derived from
    // another (throughput sums each thread's own rate, so idle threads don't dilute it).
    log.WriteLine($"  Seeds: {search.TotalSeedsSearched:N0} searched, {search.MatchingSeeds:N0} matched");
    log.WriteLine($"  Time:  {FormatDuration(TimeSpan.FromMilliseconds(search.ElapsedMs))}");
    log.WriteLine($"  Speed: {search.SeedsPerSecond:N0} seeds/s");

    if (!search.IsSequentialBatchSearch)
        return;
    long total = search.TotalBatchCount;
    double percent = total > 0 ? search.CompletedBatchCount * 100.0 / total : 0;
    log.WriteLine($"  Batch: {search.CompletedBatchCount:N0} / {total:N0} ({percent:F4}%)");

    long resume = search.ResumeBatchIndex;
    if (interrupted && resume >= 0 && resume < total)
        log.WriteLine(
            $"  Resume: --startBatch {resume}  (or --startSeed {SeedMath.BatchIndexToFirstSeed(resume, batchCharCount)})"
        );
}

// Past a day, minutes and seconds are noise: "3 days 4 hours".
static string FormatDuration(TimeSpan span) =>
    span.TotalHours >= 24
        ? $"{span.Days} {(span.Days == 1 ? "day" : "days")} {span.Hours} {(span.Hours == 1 ? "hour" : "hours")}"
        : span.ToString(@"hh\:mm\:ss\.f");
