using System;
using System.Collections.Generic;

namespace Motely;

public static class SeedMath
{
    private const string CHARS = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly Dictionary<char, int> CharToVal = new();

    static SeedMath()
    {
        for (int i = 0; i < CHARS.Length; i++)
        {
            CharToVal[CHARS[i]] = i;
        }
    }

    public static long SeedToTotalIndex(string seed)
    {
        seed = seed.ToUpperInvariant();
        long total = 0;
        long multiplier = 1;

        for (int i = seed.Length - 1; i >= 0; i--)
        {
            char c = seed[i];
            if (!CharToVal.TryGetValue(c, out int val))
                throw new ArgumentException($"Invalid seed character: {c}");

            total += (val + 1) * multiplier;
            multiplier *= 35;
        }
        return total;
    }

    public static string TotalIndexToSeed(long index)
    {
        if (index == 0)
            return "";
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        char[] buffer = new char[15];
        int pos = 15;
        long current = index;

        while (current > 0)
        {
            current--;
            int val = (int)(current % 35);
            buffer[--pos] = CHARS[val];
            current /= 35;
        }

        return new string(buffer, pos, 15 - pos);
    }

    public static long SeedToSearchIndex(string seed)
    {
        return SeedToTotalIndex(seed) - GetFirstSeedOfLength(seed.Length);
    }

    public static string SearchIndexToSeed(long index, int length)
    {
        return TotalIndexToSeed(index + GetFirstSeedOfLength(length));
    }

    public static long GetFirstSeedOfLength(int length)
    {
        if (length <= 0)
            return 0;
        long sum = 0;
        long pow = 1;
        for (int i = 0; i < length; i++)
        {
            sum += pow;
            if (i < length - 1)
                pow *= 35;
        }
        return sum;
    }

    public static string TotalIndexToSeed(long index, int length) =>
        SearchIndexToSeed(index, length);

    public static long SeedToBatchIndex(string seed, int batchSize)
    {
        int prefixLen = 8 - batchSize;
        if (prefixLen <= 0)
            return 0;

        long index = 0;
        for (int i = seed.Length - 1; i >= batchSize; i--)
            index =
                index * MotelyGlobals.SeedDigits.Length
                + MotelyGlobals.SeedDigits.IndexOf(seed[i]);
        return index;
    }

    public static string BatchIndexToSeedPrefix(long batchIndex, int batchSize)
    {
        int prefixLen = 8 - batchSize;
        if (prefixLen <= 0)
            return "";
        return SearchIndexToSeed(batchIndex, prefixLen);
    }

    public static string BatchIndexToFirstSeed(long batchIndex, int batchSize)
    {
        if (batchSize is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        string digits = BatchIndexToSeedPrefix(batchIndex, batchSize);
        if (digits.Length != 8 - batchSize)
            throw new ArgumentOutOfRangeException(
                nameof(batchIndex),
                $"Batch {batchIndex} is outside the {Math.Pow(35, 8 - batchSize):N0} batches of a {batchSize}-character batch."
            );

        char[] seed = new char[8];
        for (int i = 0; i < batchSize; i++)
            seed[i] = MotelyGlobals.SeedDigits[0];
        for (int i = 0; i < digits.Length; i++)
            seed[7 - i] = digits[i];
        return new string(seed);
    }

    public static long MaxSearchIndexInclusive(int length)
    {
        if (length <= 0)
            return -1;
        long p = 1;
        for (int i = 0; i < length; i++)
            checked
            {
                p *= 35;
            }
        return p - 1;
    }

    public static (long StartBatchIndex, long EndBatchIndexExclusive) SearchIndexRangeToBatchRange(
        long startSearchIndexInclusive,
        long stopSearchIndexInclusive,
        int batchCharCount
    )
    {
        if (batchCharCount is < 1 or > 7)
            throw new ArgumentOutOfRangeException(nameof(batchCharCount));

        long maxIdx = MaxSearchIndexInclusive(8);
        if (
            startSearchIndexInclusive < 0
            || stopSearchIndexInclusive < startSearchIndexInclusive
            || stopSearchIndexInclusive > maxIdx
        )
        {
            throw new ArgumentOutOfRangeException(
                $"{nameof(startSearchIndexInclusive)}..{nameof(stopSearchIndexInclusive)}",
                $"Must satisfy 0 <= start <= stop <= {maxIdx}."
            );
        }

        long batchSpan = 1;
        for (int i = 0; i < batchCharCount; i++)
            batchSpan *= 35;

        long startBatch = startSearchIndexInclusive / batchSpan;
        long stopBatch = stopSearchIndexInclusive / batchSpan;
        return (startBatch, stopBatch + 1);
    }
}
