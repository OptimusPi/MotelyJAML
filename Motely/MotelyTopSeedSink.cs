using System;
using System.Collections.Generic;
using System.Linq;
using Motely.Filters.Jaml;

namespace Motely;

// Seeds flow from a SOURCE (the search/provider) to a SINK. Scored seeds are offered to the
// Collector below, which keeps only the top-N by score when given a real limit (push the new
// high score on, evict the lowest off the bottom once the count exceeds it) — or all of them,
// unbounded, when the caller passes int.MaxValue. Survivors get written to their final
// destination — a JAML `seeds:` block on disk.
//
// The pure text rewrite + collector both live here so the CLI (file IO) and Motely.Wasm (browser
// File System Access) share one tested core.
public static class MotelyTopSeedSink
{
    /// <summary>
    /// Bounded top-N-by-score collector. A min-heap keyed on (score, sequence): every offered seed
    /// is enqueued, and once the count exceeds <paramref name="limit"/> the lowest-scoring entry is
    /// dropped. Ties break by insertion order (earlier wins). Not thread-safe; one per search.
    /// </summary>
    public sealed class Collector(int limit)
    {
        private readonly PriorityQueue<SavedSeedEntry, (int Score, long Sequence)> _queue = new();
        private long _sequence;

        // Scored results arrive on every worker thread — the engine invokes result callbacks with
        // no serialization. PriorityQueue is not thread-safe, so concurrent Enqueue can corrupt the
        // heap rather than merely lose an entry, and this queue is what gets written back into the
        // filter's seeds: block. The lock lives here so a caller cannot forget it, and it is only
        // ever taken when a seed actually matches.
        private readonly object _gate = new();

        public void Consider(string seed, int score)
        {
            lock (_gate)
            {
                _queue.Enqueue(new(seed, score, _sequence), (score, _sequence));
                _sequence++;

                if (_queue.Count > limit)
                    _queue.Dequeue();
            }
        }

        public IReadOnlyList<string> GetSeeds()
        {
            lock (_gate)
            {
                return _queue
                    .UnorderedItems.Select(static item => item.Element)
                    .OrderByDescending(static item => item.Score)
                    .ThenBy(static item => item.Sequence)
                    .Select(static item => item.Seed)
                    .Distinct(StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray();
            }
        }
    }

    private readonly record struct SavedSeedEntry(string Seed, int Score, long Sequence);

    /// <summary>
    /// Pure text transform: merge the given seeds into the top-level <c>seeds:</c> block of a
    /// JAML document (appending the block if absent). Seeds already in the document are a seed
    /// provider the user curated — they stay, in front, in their original order; new finds go
    /// after them. No IO, no validation. The original newline style is preserved. Seeds are
    /// normalized (<see cref="MotelyGlobals.NormalizeSeed"/>) and de-duped — no count cap; the
    /// caller's Collector (if any) already decided how many survive.
    /// </summary>
    public static string RewriteSeedsBlock(string jamlText, IReadOnlyList<string> seeds)
    {
        string normalizedNewline = jamlText.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var originalHasTrailingNewline = jamlText.EndsWith("\n", StringComparison.Ordinal);
        var lines = jamlText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        int seedsStart = FindTopLevelSeedsLine(lines);
        int seedsEndExclusive =
            seedsStart >= 0 ? FindNextTopLevelKeyLine(lines, seedsStart + 1) : -1;
        var existingSeeds =
            seedsStart >= 0
                ? ExtractExistingSeeds(lines, seedsStart, seedsEndExclusive)
                : (IReadOnlyList<string>)[];

        var normalizedSeeds = existingSeeds
            .Concat(seeds)
            .Select(static seed => MotelyGlobals.NormalizeSeed(seed))
            .Where(static seed => !string.IsNullOrWhiteSpace(seed))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var replacementLines = BuildSeedsBlockLines(normalizedSeeds);

        if (seedsStart >= 0)
        {
            lines.RemoveRange(seedsStart, seedsEndExclusive - seedsStart);
            lines.InsertRange(seedsStart, replacementLines);
        }
        else
        {
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            if (lines.Count > 0)
                lines.Add(string.Empty);

            lines.AddRange(replacementLines);
        }

        var updated = string.Join(normalizedNewline, lines);
        if (originalHasTrailingNewline || lines.Count > 0)
            updated += normalizedNewline;

        return updated;
    }

    /// <summary>
    /// Rewrite the <c>seeds:</c> block then confirm the result still loads as valid JAML. Returns
    /// false with <paramref name="error"/> set if the rewritten document does not parse — so a bad
    /// write is caught before it ever touches disk.
    /// </summary>
    public static bool TryRewriteAndValidate(
        string jamlText,
        IReadOnlyList<string> seeds,
        out string newText,
        out string? error
    )
    {
        newText = RewriteSeedsBlock(jamlText, seeds);
        if (!JamlConfigLoader.TryLoad(newText, out _, out var loadError))
        {
            error = loadError ?? "Updated JAML did not validate.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Pull the seeds already present in a <c>seeds:</c> region, in document order. Handles both
    /// the block form (<c>- SEED</c> items) and the inline form (<c>seeds: [A, B]</c>), tolerating
    /// quotes and trailing <c>#</c> comments (a '#' can never be part of a seed — the seed
    /// alphabet is alphanumeric).
    /// </summary>
    private static List<string> ExtractExistingSeeds(
        IReadOnlyList<string> lines,
        int seedsStart,
        int seedsEndExclusive
    )
    {
        var seeds = new List<string>();

        var firstLine = lines[seedsStart];
        var inlineValue = firstLine[(firstLine.IndexOf(':') + 1)..];
        int commentIndex = inlineValue.IndexOf('#');
        if (commentIndex >= 0)
            inlineValue = inlineValue[..commentIndex];
        inlineValue = inlineValue.Trim().TrimStart('[').TrimEnd(']');
        foreach (var part in inlineValue.Split(',', StringSplitOptions.TrimEntries))
        {
            var seed = part.Trim('"', '\'');
            if (seed.Length > 0)
                seeds.Add(seed);
        }

        for (int i = seedsStart + 1; i < seedsEndExclusive; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                continue;

            var item = trimmed[2..];
            commentIndex = item.IndexOf('#');
            if (commentIndex >= 0)
                item = item[..commentIndex];
            var seed = item.Trim().Trim('"', '\'');
            if (seed.Length > 0)
                seeds.Add(seed);
        }

        return seeds;
    }

    private static List<string> BuildSeedsBlockLines(IReadOnlyList<string> seeds)
    {
        if (seeds.Count == 0)
            return ["seeds: []"];

        var lines = new List<string>(seeds.Count + 1) { "seeds:" };
        lines.AddRange(seeds.Select(static seed => $"  - {seed}"));
        return lines;
    }

    private static int FindTopLevelSeedsLine(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (!TryGetTopLevelKey(lines[i], out var key))
                continue;

            if (string.Equals(key, "seeds", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int FindNextTopLevelKeyLine(IReadOnlyList<string> lines, int startIndex)
    {
        for (int i = startIndex; i < lines.Count; i++)
        {
            if (TryGetTopLevelKey(lines[i], out _))
                return i;
        }

        return lines.Count;
    }

    private static bool TryGetTopLevelKey(string line, out string? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;
        if (char.IsWhiteSpace(line[0]))
            return false;

        var trimmed = line.Trim();
        if (
            trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith("-", StringComparison.Ordinal)
        )
            return false;

        int colonIndex = trimmed.IndexOf(':');
        if (colonIndex <= 0)
            return false;

        key = trimmed[..colonIndex].Trim();
        return key.Length > 0;
    }
}
