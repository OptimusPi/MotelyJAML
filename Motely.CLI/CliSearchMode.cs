#nullable enable
using System.Diagnostics.CodeAnalysis;
using Motely.Filters;
using Motely.SeedProviders;

namespace Motely.CLI;

/// <summary>
/// Shared CLI wiring for list / keyword / random / aesthetic / sequential search modes (native + JAML).
/// </summary>
internal static class CliSearchMode
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
    private const int DefaultBatchCharacterCount = 4;

    public static bool TryApplySearchMode(
        IMotelySearchSettings settings,
        in Input input,
        Action<string>? writeWarning,
        [NotNullWhen(false)] out string? error,
        out IMotelySearchSettings updated
    )
    {
        updated = settings;
        error = null;

        bool hasSeedsArg = !string.IsNullOrWhiteSpace(input.SeedsArgument);
        bool hasReplayMode = input.Replay;

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
            var seedsValue = input.SeedsArgument!;
            if (
                seedsValue.Contains(Path.DirectorySeparatorChar)
                || seedsValue.Contains(Path.AltDirectorySeparatorChar)
                || Path.HasExtension(seedsValue)
            )
            {
                error = $"Error: --seeds takes inline seeds (A1B2C3D4,E5F6G7H8), not a file: '{seedsValue}'.";
                return false;
            }

            var inlineSeeds = ParseInlineSeeds(seedsValue);
            if (inlineSeeds.Count == 0)
            {
                error = "Error: --seeds requires at least one inline seed.";
                return false;
            }

            explicitSeeds = inlineSeeds.ToArray();
        }

        if (explicitSeeds != null)
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
