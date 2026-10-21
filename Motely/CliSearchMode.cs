#nullable enable
using System.Diagnostics.CodeAnalysis;
using Motely.Filters;
using Motely.SeedProviders;

namespace Motely;

/// <summary>
/// Shared CLI wiring for list / keyword / random / aesthetic / sequential search modes (native + JAML).
/// </summary>
public static class CliSearchMode
{
    public readonly record struct Input(
        string? SeedsArgument,
        bool Replay,
        string? JamlPath,
        string? FilterId,
        IReadOnlyList<string>? JamlSeeds,
        IReadOnlyList<string> KeywordInputs,
        string? PaddingCharsOption,
        int? RandomCount,
        string? AestheticName,
        long? StartBatch,
        long? EndBatch,
        double? StartPercent,
        string? StartSeed,
        string? StopSeed,
        int? BatchCharacterCount
    );

    /// <summary>Default batch character count when the caller didn't pass one explicitly.</summary>
    public const int DefaultBatchCharacterCount = 3;

    /// <summary>What <see cref="TryNormalizeSeed"/> accepts, for error messages.</summary>
    public const string SeedRule = "1-8 characters of 1-9, A-Z";

    /// <summary>
    /// Normalizes a typed seed (trim, upper-case, 0 to O) and checks it is 1-8 characters of
    /// 1-9/A-Z, the only seeds the engine searches. Anything else is refused rather than hashed
    /// as a seed or dropped.
    /// </summary>
    public static bool TryNormalizeSeed(string input, [NotNullWhen(true)] out string? seed)
    {
        seed = MotelyGlobals.NormalizeSeed(input);
        if (
            seed.Length is >= 1 and <= MotelyGlobals.MaxSeedLength
            && seed.All(static c => Array.IndexOf(MotelyGlobals.SeedDigits, c) >= 0)
        )
            return true;
        seed = null;
        return false;
    }

    /// <summary>A seed the sequential sweep visits: valid, and the full 8 characters.</summary>
    private static bool TrySweepSeed(string input, [NotNullWhen(true)] out string? seed)
    {
        if (TryNormalizeSeed(input, out seed) && seed.Length == MotelyGlobals.MaxSeedLength)
            return true;
        seed = null;
        return false;
    }

    public static bool TryApplySearchMode(
        IMotelySearchSettings settings,
        in Input input,
        [NotNullWhen(false)] out string? error,
        out IMotelySearchSettings updated
    )
    {
        updated = settings;
        error = null;

        bool hasSeedsArg = !string.IsNullOrWhiteSpace(input.SeedsArgument);
        bool hasReplayMode = input.Replay;

        MotelyAesthetic? explicitAesthetic = null;
        bool aestheticAll = false;
        if (!string.IsNullOrWhiteSpace(input.AestheticName))
        {
            var trimmed = input.AestheticName.Trim();
            if (MotelyAestheticParser.IsAllToken(trimmed))
            {
                aestheticAll = true;
            }
            else if (!MotelyAestheticParser.TryParse(trimmed, out var aesthetic))
            {
                error =
                    $"Error: unknown --aesthetic value '{trimmed}'. Known: {MotelyAestheticParser.KnownJamlStringsDescription()}.";
                return false;
            }
            else
            {
                explicitAesthetic = aesthetic;
            }
        }

        bool hasAestheticMode = explicitAesthetic.HasValue || aestheticAll;

        bool hasSeedIndexOptions =
            input.StartSeed is not null || input.StopSeed is not null;
        if (hasSeedIndexOptions)
        {
            if (
                hasSeedsArg
                || hasReplayMode
                || input.KeywordInputs.Count > 0
                || input.RandomCount.HasValue
                || hasAestheticMode
            )
            {
                error = "Error: --startSeed/--stopSeed apply only to default sequential search.";
                return false;
            }
        }

        bool hasKeywordMode = input.KeywordInputs.Count > 0;

        // --padding with no seed character in it reached the engine as an empty alphabet, and a
        // short keyword without one tripped the engine's own guard: both died with a stack trace.
        char[]? paddingChars = null;
        if (input.PaddingCharsOption is not null)
        {
            paddingChars = MotelyGlobals.ParsePaddingChars(input.PaddingCharsOption);
            if (paddingChars is null)
            {
                error =
                    $"Error: --padding '{input.PaddingCharsOption}' has no seed characters (1-9, A-Z).";
                return false;
            }
        }

        var keywords = new List<string>(input.KeywordInputs.Count);
        foreach (var raw in input.KeywordInputs)
        {
            if (!TryNormalizeSeed(raw, out var keyword))
            {
                error = $"Error: keyword '{raw}' is not part of a seed ({SeedRule}).";
                return false;
            }
            if (keyword.Length <= 2 && paddingChars is null)
            {
                error =
                    $"Error: keyword '{keyword}' is {keyword.Length} character(s); keywords that short need --padding (e.g. --padding 123456789).";
                return false;
            }
            keywords.Add(keyword);
        }

        int explicitSearchModeCount = 0;
        if (hasSeedsArg)
            explicitSearchModeCount++;
        if (hasReplayMode)
            explicitSearchModeCount++;
        if (hasKeywordMode)
            explicitSearchModeCount++;
        if (input.RandomCount.HasValue)
            explicitSearchModeCount++;
        if (hasAestheticMode)
            explicitSearchModeCount++;

        if (explicitSearchModeCount > 1)
        {
            error =
                "Error: choose only one search input mode: --seeds, --replay, --keyword, --keywords, --random, or --aesthetic.";
            return false;
        }

        string[]? explicitSeeds = null;

        if (hasReplayMode)
        {
            if (string.IsNullOrWhiteSpace(input.JamlPath))
            {
                error = "Error: --replay requires --jaml (it replays that file's seeds: block).";
                return false;
            }
            if (input.JamlSeeds is not { Count: > 0 })
            {
                error = $"Error: '{input.JamlPath}' has no seeds: block to replay.";
                return false;
            }
            explicitSeeds = [.. input.JamlSeeds.Select(static s => MotelyGlobals.NormalizeSeed(s))];
        }
        else if (hasSeedsArg)
        {
            if (!TryResolveSeeds(input.SeedsArgument!, out explicitSeeds, out error))
                return false;
        }

        if (explicitSeeds != null)
        {
            updated = updated.WithSeedList(explicitSeeds);
        }
        else if (hasKeywordMode)
        {
            // An unparseable --padding was refused above, so this is the full alphabet or the set.
            updated = updated.WithKeywordSearch([.. keywords], paddingChars);
        }
        else if (input.RandomCount.HasValue)
        {
            updated = updated.WithRandomSearch(input.RandomCount.Value);
        }
        else if (aestheticAll)
        {
            // --aesthetic all: concat every family (palindrome → … → nsfw). Same pad law as
            // single --aesthetic: full alphabet unless --padding. (Default --collect without
            // --aesthetic still uses digit pad + sequential fallback in Program.)
            updated = updated.WithAllAesthetics(
                MotelyAestheticParser.AllAesthetics(),
                Padding(input.PaddingCharsOption)
            );
        }
        else if (explicitAesthetic.HasValue)
        {
            // --padding mixes with --aesthetic: free slots / keyword pads use that charset.
            // Default when omitted: full alphabet (explicit single-family search). Collect's
            // multi-family prepass defaults to digit pad separately in Program.
            updated = updated.WithAestheticSearch(
                explicitAesthetic.Value,
                Padding(input.PaddingCharsOption)
            );
        }
        // The JAML seeds: replay and the sequential sweep are the *default* modes — they apply
        // only when the caller picked no explicit search input above. An explicit mode
        // (--keyword, --random, --aesthetic, --seeds, --replay) already installed its provider;
        // reaching the block below would silently stomp it back to sequential.
        if (explicitSearchModeCount > 0)
            return true;

        // Sequential is the default, always. A JAML `seeds:` block is saved *output* — the engine
        // writes it back after a run — so treating its presence as an instruction meant a filter
        // silently stopped sweeping the moment it had ever found anything. Replaying that list is
        // the explicit `--replay`. Nothing is lost by not guessing.
        {
            int batchCharacterCount = input.BatchCharacterCount ?? DefaultBatchCharacterCount;
            if (batchCharacterCount is < 1 or >= MotelyGlobals.MaxSeedLength)
            {
                error = $"Error: --batchCharCount must be 1-{MotelyGlobals.MaxSeedLength - 1}.";
                return false;
            }
            updated = updated.WithBatchCharacterCount(batchCharacterCount).WithSequentialSearch();

            if (hasSeedIndexOptions)
            {
                if (
                    input.StartBatch.HasValue
                    || input.EndBatch.HasValue
                    || input.StartPercent.HasValue
                )
                {
                    error =
                        "Error: do not combine --startSeed/--stopSeed with --startBatch, --endBatch, or --startPercent.";
                    return false;
                }

                // The sweep only visits 8-character seeds, and SeedToBatchIndex reads every
                // character as a digit: anything else would land on a wrong (or negative) batch.
                string? startSeed = null, stopSeed = null;
                if (input.StartSeed is { } rawStart && !TrySweepSeed(rawStart, out startSeed))
                {
                    error = $"Error: --startSeed '{rawStart}' is not an 8-character seed of 1-9, A-Z.";
                    return false;
                }
                if (input.StopSeed is { } rawStop && !TrySweepSeed(rawStop, out stopSeed))
                {
                    error = $"Error: --stopSeed '{rawStop}' is not an 8-character seed of 1-9, A-Z.";
                    return false;
                }

                // A seed names the batch that contains it, in the engine's sweep order: the batch
                // digits are the seed's tail (SeedToBatchIndex), not a reading-order index.
                long startBatch = startSeed is not null
                    ? SeedMath.SeedToBatchIndex(startSeed, batchCharacterCount)
                    : 0;
                long endBatchExclusive = stopSeed is not null
                    ? SeedMath.SeedToBatchIndex(stopSeed, batchCharacterCount) + 1
                    : long.MaxValue;
                if (startBatch >= endBatchExclusive)
                {
                    error =
                        $"Error: --stopSeed {input.StopSeed} is in batch {endBatchExclusive - 1}, before --startSeed {input.StartSeed} in batch {startBatch} (sweep order, not alphabetical).";
                    return false;
                }
                updated = updated.WithStartBatchIndex(startBatch).WithEndBatchIndex(endBatchExclusive);
            }
            else
            {
                long maxBatch = MotelyGlobals.SequentialBatchCount(batchCharacterCount);
                long startBatch = 0;

                if (input.StartBatch.HasValue)
                {
                    // A negative index crashed a worker (IndexOutOfRange, stack trace); one past
                    // the end searched nothing and exited 0 as if it had finished.
                    startBatch = input.StartBatch.Value;
                    if (startBatch < 0 || startBatch >= maxBatch)
                    {
                        error =
                            $"Error: --startBatch must be 0..{maxBatch - 1} (batchCharCount {batchCharacterCount}).";
                        return false;
                    }
                    updated = updated.WithStartBatchIndex(startBatch);
                }
                else if (input.StartPercent.HasValue)
                {
                    double pct = input.StartPercent.Value;
                    // Negated so NaN fails too.
                    if (!(pct >= 0 && pct <= 100))
                    {
                        error = "Error: --startPercent must be between 0 and 100.";
                        return false;
                    }

                    startBatch = (long)(maxBatch * (pct / 100.0));
                    if (startBatch < 0)
                        startBatch = 0;
                    if (maxBatch > 0 && startBatch >= maxBatch)
                        startBatch = maxBatch - 1;
                    updated = updated.WithStartBatchIndex(startBatch);
                }

                if (input.EndBatch.HasValue)
                {
                    // An end at or before the start searched nothing and still exited 0.
                    if (input.EndBatch.Value <= startBatch)
                    {
                        error =
                            $"Error: --endBatch {input.EndBatch.Value} must be greater than the start batch {startBatch}.";
                        return false;
                    }
                    updated = updated.WithEndBatchIndex(input.EndBatch.Value);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// What --seeds names: a seed file (an earlier run's CSV, a .txt, a filter's export), "-" for
    /// stdin, or inline comma-separated seeds. Repeats are searched once.
    /// </summary>
    public static bool TryResolveSeeds(
        string value,
        [NotNullWhen(true)] out string[]? seeds,
        [NotNullWhen(false)] out string? error
    )
    {
        seeds = null;
        List<string> list;
        if (value.Trim() == "-")
            list = ReadSeedFile(Console.In, "stdin");
        else if (File.Exists(value))
        {
            using var reader = File.OpenText(value);
            list = ReadSeedFile(reader, value);
        }
        else if (
            value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || Path.HasExtension(value)
        )
        {
            error = $"Error: --seeds: no seed file at {Path.GetFullPath(value)}.";
            return false;
        }
        else if (!TryParseInlineSeeds(value, out list, out error))
            return false;

        seeds = [.. list.Distinct()];
        if (seeds.Length == 0)
        {
            error = $"Error: --seeds: no seeds in '{value}'.";
            seeds = null;
            return false;
        }
        error = null;
        return true;
    }

    /// <summary>
    /// The first column of each line: motely's own CSV (its seed,score header is skipped), a plain
    /// list, or a spreadsheet export (quotes and a formula's leading '=' stripped). A line that is
    /// still not a seed is reported on stderr and skipped, so one bad cell doesn't sink the run.
    /// </summary>
    private static List<string> ReadSeedFile(TextReader reader, string name)
    {
        List<string> seeds = [];
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            int comma = line.IndexOf(',');
            var cell = (comma < 0 ? line : line[..comma]).Trim().Trim('"').TrimStart('=').Trim();
            if (cell.Length == 0 || (lineNumber == 1 && cell.Equals("seed", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (TryNormalizeSeed(cell, out var seed))
                seeds.Add(seed);
            else
                Console.Error.WriteLine($"Warning: {name} line {lineNumber}: '{cell}' is not a seed ({SeedRule}); skipped.");
        }
        return seeds;
    }

    private static bool TryParseInlineSeeds(
        string value,
        out List<string> seeds,
        [NotNullWhen(false)] out string? error
    )
    {
        seeds = [];
        error = null;
        foreach (
            var part in value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;
            if (!TryNormalizeSeed(part, out var seed))
            {
                error = $"Error: --seeds: '{part}' is not a seed ({SeedRule}).";
                return false;
            }
            seeds.Add(seed);
        }
        return true;
    }

    /// <summary>The --padding charset, or null for the full alphabet.</summary>
    public static char[]? Padding(string? option) =>
        string.IsNullOrWhiteSpace(option) ? null : MotelyGlobals.ParsePaddingChars(option);

    /// <summary>Every listed aesthetic family, one after another, as one seed provider.</summary>
    public static IMotelySearchSettings WithAllAesthetics(
        this IMotelySearchSettings settings,
        IReadOnlyList<MotelyAesthetic> aesthetics,
        char[]? padding
    ) =>
        settings.WithProviderSearch(
            new MotelySeedListProvider(
                aesthetics.SelectMany(a => MotelyAesthetics.EnumerateSeeds(a, padding)),
                aesthetics.Sum(a => MotelyAesthetics.GetSeedCount(a, padding))
            )
        );
}
