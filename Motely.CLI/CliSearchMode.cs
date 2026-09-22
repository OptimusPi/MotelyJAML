#nullable enable
using System.Diagnostics.CodeAnalysis;
using Motely.DataLake;
using Motely.Filters;
using Motely.SeedProviders;

namespace Motely.CLI;

/// <summary>
/// Shared CLI wiring for list / keyword / random / aesthetic / sequential search modes (native + JAML).
/// </summary>
internal static class CliSearchMode
{
    public readonly record struct Input(
        string? SourcePath,
        string? SeedsArgument,
        bool Drown,
        bool Replay,
        string? JamlPath,
        string? ResultsRootPath,
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
    private const int DefaultBatchCharacterCount = 4;

    public static bool TryApplySearchMode(
        IMotelySearchSettings settings,
        in Input input,
        Action<string>? writeWarning,
        [NotNullWhen(false)] out string? error,
        out IMotelySearchSettings updated,
        out IDisposable? sourceLifetime
    )
    {
        updated = settings;
        error = null;
        sourceLifetime = null;

        bool hasSource = !string.IsNullOrWhiteSpace(input.SourcePath);
        bool hasSeedsArg = !string.IsNullOrWhiteSpace(input.SeedsArgument);
        bool hasDrownMode = input.Drown;
        bool hasReplayMode = input.Replay;

        if (hasSource && hasSeedsArg)
        {
            error = "Error: choose only one explicit seed input: --source or --seeds.";
            return false;
        }

        JamlAesthetic? explicitAesthetic = null;
        bool aestheticAll = false;
        if (!string.IsNullOrWhiteSpace(input.AestheticName))
        {
            var trimmed = input.AestheticName.Trim();
            if (JamlAestheticParser.IsAllToken(trimmed))
            {
                aestheticAll = true;
            }
            else if (!JamlAestheticParser.TryParse(trimmed, out var aesthetic))
            {
                error =
                    $"Error: unknown --aesthetic value '{trimmed}'. Known: {JamlAestheticParser.KnownJamlStringsDescription()}.";
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
                hasSource
                || hasSeedsArg
                || hasDrownMode
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

        bool hasSeedListMode = hasSource || hasSeedsArg;
        bool hasKeywordMode = input.KeywordInputs.Count > 0;

        int explicitSearchModeCount = 0;
        if (hasSeedListMode)
            explicitSearchModeCount++;
        if (hasDrownMode)
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
                "Error: choose only one search input mode: --source, --seeds, --drown, --replay, --keyword, --keywords, --random, or --aesthetic.";
            return false;
        }

        string[]? explicitSeeds = null;
        SeedSourceProvider? streamingProvider = null;

        bool drownFellBackToSequential = false;
        if (hasDrownMode)
        {
            // Cannonball: every seed ever saved — the lake root (every filter, deduped) plus
            // this JAML's own seeds: block, which is saved output the lake may predate.
            string lakeRoot = SeedLakeSink.LakeRoot(input.ResultsRootPath);
            bool hasJamlSeeds = input.JamlSeeds is { Count: > 0 };
            if (!Directory.Exists(lakeRoot) && !hasJamlSeeds)
            {
                drownFellBackToSequential = true;
            }
            else
            {
                try
                {
                    var drownProvider = SeedSourceProvider.FromLakeRoot(
                        lakeRoot,
                        hasJamlSeeds ? input.JamlSeeds : null
                    );
                    if (drownProvider.SeedCount == 0)
                    {
                        drownProvider.Dispose();
                        drownFellBackToSequential = true;
                    }
                    else
                    {
                        updated = updated.WithProviderSearch(drownProvider);
                        sourceLifetime = drownProvider;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    error = $"Error: could not read seed lake '{lakeRoot}': {ex.Message}";
                    return false;
                }
            }

            // Nothing saved anywhere yet — there is no haystack to drown in. That is not a
            // reason to refuse the run: the sequential sweep below is exactly what fills the
            // lake, so --drown degrades to it and says so, instead of telling the operator
            // to "run a search first" while they are running one.
            writeWarning?.Invoke(
                $"Note: nothing to drown in yet — the seed lake at '{lakeRoot}' holds no seeds"
                    + (input.JamlPath is not null ? " and the JAML has no seeds: block" : "")
                    + ". Running the default sequential sweep instead; every find lands in the lake for the next --drown."
            );
        }

        if (hasReplayMode)
        {
            // Replay / verify: only the seeds: block of this JAML file, nothing else.
            if (string.IsNullOrWhiteSpace(input.JamlPath))
            {
                error = "Error: --replay requires --jaml (it replays that file's seeds: block).";
                return false;
            }

            try
            {
                var replayProvider = new SeedSourceProvider(input.JamlPath!);
                if (replayProvider.SeedCount == 0)
                {
                    replayProvider.Dispose();
                    error = $"Error: '{input.JamlPath}' has no seeds: block to replay.";
                    return false;
                }
                updated = updated.WithProviderSearch(replayProvider);
                sourceLifetime = replayProvider;
            }
            catch (Exception ex)
            {
                error = $"Error: could not read seeds from '{input.JamlPath}': {ex.Message}";
                return false;
            }
            return true;
        }

        if (hasSource)
        {
            try
            {
                streamingProvider = new SeedSourceProvider(input.SourcePath!);
                if (streamingProvider.SeedCount == 0)
                {
                    streamingProvider.Dispose();
                    streamingProvider = null;
                    error = "Error: resolved source contained no seeds.";
                    return false;
                }
                sourceLifetime = streamingProvider;
            }
            catch (Exception ex)
            {
                error = $"Error: {ex.Message}";
                return false;
            }
        }
        else if (hasSeedsArg)
        {
            var seedsValue = input.SeedsArgument!;
            bool looksLikeSourcePath =
                seedsValue.Contains(Path.DirectorySeparatorChar)
                || seedsValue.Contains(Path.AltDirectorySeparatorChar)
                || Path.HasExtension(seedsValue);

            if (looksLikeSourcePath)
            {
                try
                {
                    streamingProvider = new SeedSourceProvider(seedsValue);
                    if (streamingProvider.SeedCount == 0)
                    {
                        streamingProvider.Dispose();
                        streamingProvider = null;
                        error = "Error: resolved seed source contained no seeds.";
                        return false;
                    }

                    writeWarning?.Invoke(
                        "Warning: --seeds <path> is deprecated; use --source <path>."
                    );
                    sourceLifetime = streamingProvider;
                }
                catch (Exception ex)
                {
                    error = $"Error: {ex.Message}";
                    return false;
                }
            }
            else
            {
                var inlineSeeds = ParseInlineSeeds(seedsValue);
                if (inlineSeeds.Count == 0)
                {
                    error = "Error: --seeds requires at least one inline seed.";
                    return false;
                }

                explicitSeeds = inlineSeeds.ToArray();
            }
        }

        if (streamingProvider != null)
        {
            updated = updated.WithProviderSearch(streamingProvider);
        }
        else if (explicitSeeds != null)
        {
            updated = new MotelySearchIntent(
                Mode: MotelySearchInputMode.SeedList,
                Seeds: explicitSeeds
            ).ApplyTo(updated);
        }
        else if (hasKeywordMode)
        {
            updated = new MotelySearchIntent(
                Mode: MotelySearchInputMode.Keyword,
                Keywords: [.. input.KeywordInputs],
                PaddingAlphabet: input.PaddingCharsOption
            ).ApplyTo(updated);
        }
        else if (input.RandomCount.HasValue)
        {
            updated = new MotelySearchIntent(
                Mode: MotelySearchInputMode.Random,
                RandomSeedCount: input.RandomCount.Value
            ).ApplyTo(updated);
        }
        else if (aestheticAll)
        {
            // --aesthetic all: concat every family (palindrome → … → nsfw). Same pad law as
            // single --aesthetic: full alphabet unless --padding. (Default --collect without
            // --aesthetic still uses digit pad + sequential fallback in Program.)
            updated = new MotelySearchIntent(
                Mode: MotelySearchInputMode.Aesthetic,
                Aesthetics: [.. JamlAestheticParser.AllAesthetics()],
                PaddingAlphabet: input.PaddingCharsOption
            ).ApplyTo(updated);
        }
        else if (explicitAesthetic.HasValue)
        {
            // --padding mixes with --aesthetic: free slots / keyword pads use that charset.
            // Default when omitted: full alphabet (explicit single-family hunt). Collect's
            // multi-family prepass defaults to digit pad separately in Program.
            updated = new MotelySearchIntent(
                Mode: MotelySearchInputMode.Aesthetic,
                Aesthetic: explicitAesthetic.Value,
                PaddingAlphabet: input.PaddingCharsOption
            ).ApplyTo(updated);
        }
        // The JAML seeds: replay and the sequential sweep are the *default* modes — they apply
        // only when the caller picked no explicit search input above. An explicit mode
        // (--keyword, --random, --aesthetic, --source, --seeds) already installed its provider;
        // reaching the block below would silently stomp it back to sequential.
        // --drown with nothing saved anywhere is the one explicit mode that degrades here.
        if (explicitSearchModeCount > 0 && !drownFellBackToSequential)
            return true;

        // Sequential is the default, always. A JAML `seeds:` block is saved *output* — the engine
        // writes it back after a run — so treating its presence as an instruction meant a filter
        // silently stopped sweeping the moment it had ever found anything. Replaying that list is
        // an explicit request with an existing door: `--source <file>.yaml` (or `.json`), which SeedSourceProvider
        // already reads (it regex-extracts the seeds: block). Nothing is lost by not guessing.
        {
            int batchCharacterCount = input.BatchCharacterCount ?? DefaultBatchCharacterCount;
            updated = new MotelySearchIntent(
                SequentialBatchCharacterCount: batchCharacterCount
            ).ApplyTo(updated);

            bool hasSeedRange =
                input.StartSeed is not null || input.StopSeed is not null;
            if (hasSeedRange)
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

                // A seed names the batch that contains it, in the engine's sweep order: the batch
                // digits are the seed's tail (SeedToBatchIndex), not a reading-order index.
                long startBatch = input.StartSeed is { } startSeed
                    ? SeedMath.SeedToBatchIndex(startSeed, batchCharacterCount)
                    : 0;
                long endBatchExclusive = input.StopSeed is { } stopSeed
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
                if (input.StartBatch.HasValue)
                    updated = updated.WithStartBatchIndex(input.StartBatch.Value);
                else if (input.StartPercent.HasValue)
                {
                    double pct = input.StartPercent.Value;
                    if (pct < 0 || pct > 100)
                    {
                        error = "Error: --startPercent must be between 0 and 100.";
                        return false;
                    }

                    int nonBatchChars = MotelyGlobals.MaxSeedLength - batchCharacterCount;
                    long maxBatch = (long)Math.Pow(MotelyGlobals.SeedDigits.Length, nonBatchChars);
                    long startBatch = (long)(maxBatch * (pct / 100.0));
                    if (startBatch < 0)
                        startBatch = 0;
                    if (maxBatch > 0 && startBatch >= maxBatch)
                        startBatch = maxBatch - 1;
                    updated = updated.WithStartBatchIndex(startBatch);
                }

                if (input.EndBatch.HasValue)
                    updated = updated.WithEndBatchIndex(input.EndBatch.Value);
            }
        }

        return true;
    }

    private static List<string> ParseInlineSeeds(string value)
    {
        var seeds = new List<string>();
        foreach (
            var part in value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            if (!string.IsNullOrWhiteSpace(part))
                seeds.Add(MotelyGlobals.NormalizeSeed(part));
        }
        return seeds;
    }
}
