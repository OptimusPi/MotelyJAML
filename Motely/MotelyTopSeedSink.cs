using System;
using System.Collections.Generic;
using System.Linq;
using Motely.Filters.Jaml;

namespace Motely;

public static class MotelyTopSeedSink
{
    public sealed class Collector(int limit)
    {
        private readonly PriorityQueue<SavedSeedEntry, (int Score, long Sequence)> _queue = new();
        private long _sequence;

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
