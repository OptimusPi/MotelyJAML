using System.Numerics;

namespace Motely.SeedProviders;

/// <summary>
/// Seed-space shapes Motely can enumerate instead of a full sweep.
/// Enumeration and classification: <see cref="MotelyAesthetics"/>.
/// </summary>
public enum MotelyAesthetic
{
    Palindrome,
    /// <summary>ABAxBxxx letter skeleton (A,B free pad).</summary>
    Psychosis,
    Mirror,
    Repeater,
    Runs,
    Step,
    Leet,
    Gross,
    Funny,
    Balatro,
    Nsfw,
    /// <summary>Every 8-digit window of π (O plays 0, which the seed alphabet lacks).</summary>
    Pi,
    /// <summary>180° ambigrams: the seed reads the same upside down (SWIMS, MOW, NOON).</summary>
    Spin,
    /// <summary>Each character is the sum of the two before it, mod the alphabet.</summary>
    Fibonacci,
}

/// <summary>
/// Generation and counting of <see cref="MotelyAesthetic"/> seed spaces over Motely's alphabet
/// and length rules. Palindrome/Psychosis/Mirror/Repeater/Runs/Step live here; keyword-backed aesthetics
/// (<see cref="MotelyAesthetic.Gross"/>, <see cref="MotelyAesthetic.Funny"/>,
/// <see cref="MotelyAesthetic.Balatro"/>, <see cref="MotelyAesthetic.Nsfw"/>) delegate to
/// <see cref="MotelySeedKeywordSequences"/>.
/// <para>
/// <b>Padding alphabet:</b> free / generated positions use <paramref name="paddingAlphabet"/> when
/// provided (CLI <c>--padding</c>). Null keeps the full <see cref="MotelyGlobals.SeedDigits"/> space
/// (historical full aesthetic). Collect's aesthetic prepass defaults to
/// <see cref="QuickPaddingChars"/> so words stay visible and the stream is searchable.
/// </para>
/// </summary>
public static class MotelyAesthetics
{
    /// <summary>
    /// Digit-only pad: free slots stay numeric so letter patterns (psychosis ABA…, keyword words)
    /// stay readable. ~orders of magnitude smaller than full <see cref="MotelyGlobals.SeedDigits"/>.
    /// </summary>
    public static readonly char[] QuickPaddingChars = "123456789".ToCharArray();

    /// <summary>Returns how many seeds <paramref name="aesthetic"/> enumerates.</summary>
    /// <param name="paddingAlphabet">
    /// Optional charset for free/generated positions. Null = full seed alphabet.
    /// </param>
    public static long GetSeedCount(MotelyAesthetic aesthetic, char[]? paddingAlphabet = null) =>
        aesthetic switch
        {
            MotelyAesthetic.Palindrome => PalindromeAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Psychosis => PsychosisAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Mirror => MirrorAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Repeater => RepeaterAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Runs => RunsAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Step => StepAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Pi => PiAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Spin => SpinAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Fibonacci => FibonacciAestheticSeeds.GetSeedCount(paddingAlphabet),
            MotelyAesthetic.Gross
                or MotelyAesthetic.Funny
                or MotelyAesthetic.Balatro
                or MotelyAesthetic.Leet
                or MotelyAesthetic.Nsfw => MotelySeedKeywordSequences.GetAestheticSeedCount(
                aesthetic,
                paddingAlphabet
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(aesthetic)),
        };

    /// <summary>Deterministic enumeration; order matches historical full-alphabet providers when pad is null.</summary>
    public static IEnumerable<string> EnumerateSeeds(
        MotelyAesthetic aesthetic,
        char[]? paddingAlphabet = null
    ) =>
        aesthetic switch
        {
            MotelyAesthetic.Palindrome => PalindromeAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Psychosis => PsychosisAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Mirror => MirrorAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Repeater => RepeaterAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Runs => RunsAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Step => StepAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Pi => PiAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Spin => SpinAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Fibonacci => FibonacciAestheticSeeds.Enumerate(paddingAlphabet),
            MotelyAesthetic.Gross
                or MotelyAesthetic.Funny
                or MotelyAesthetic.Balatro
                or MotelyAesthetic.Leet
                or MotelyAesthetic.Nsfw => MotelySeedKeywordSequences.EnumerateAestheticSeeds(
                aesthetic,
                paddingAlphabet
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(aesthetic)),
        };

    /// <summary>Resolve pad: null → full seed digits; empty after filter is invalid (caller uses full).</summary>
    internal static char[] AlphabetOrFull(char[]? paddingAlphabet) =>
        paddingAlphabet is { Length: > 0 } ? paddingAlphabet : MotelyGlobals.SeedDigits;
}

/// <summary>
/// Four-character runs: each valid character repeated four times, padded at every possible offset
/// in an eight-character seed. A seed with a longer run may occur more than once, matching the
/// existing keyword-provider semantics.
/// </summary>
file static class RunsAestheticSeeds
{
    private static readonly string[] RunKeywords = [
        .. MotelyGlobals.SeedDigits.Select(static c => new string(c, 4)),
    ];

    public static long GetSeedCount(char[]? paddingAlphabet) =>
        MotelyGlobals.GetPaddedSeedCountForKeywordsLong(RunKeywords, paddingAlphabet);

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet) =>
        MotelyGlobals.GeneratePaddedSeedsForKeywords(RunKeywords, paddingAlphabet);
}

/// <summary>Palindrome seeds: mirror-generated halves over the pad alphabet, lengths 1..<see cref="MotelyGlobals.MaxSeedLength"/>.</summary>
file static class PalindromeAestheticSeeds
{
    public static long GetSeedCount(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        checked
        {
            long total = 0;
            int seedDigitCount = alphabet.Length;

            for (int len = 1; len <= MotelyGlobals.MaxSeedLength; len++)
            {
                int halfLen = (len + 1) / 2;
                long countForLength = 1;

                for (int i = 0; i < halfLen; i++)
                    countForLength *= seedDigitCount;

                total += countForLength;
            }

            return total;
        }
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        for (int len = 1; len <= MotelyGlobals.MaxSeedLength; len++)
        {
            foreach (var palindrome in OfLength(len, alphabet))
                yield return palindrome;
        }
    }

    private static IEnumerable<string> OfLength(int length, char[] alphabet)
    {
        if (length == 1)
        {
            for (int i = 0; i < alphabet.Length; i++)
                yield return alphabet[i].ToString();
            yield break;
        }

        int halfLen = (length + 1) / 2;
        foreach (var palindrome in GenerateRecursive(new char[length], 0, halfLen, length, alphabet))
            yield return palindrome;
    }

    private static IEnumerable<string> GenerateRecursive(
        char[] buffer,
        int pos,
        int halfLen,
        int totalLen,
        char[] alphabet
    )
    {
        if (pos >= halfLen)
        {
            for (int i = 0; i < halfLen; i++)
                buffer[totalLen - 1 - i] = buffer[i];
            yield return new string(buffer, 0, totalLen);
            yield break;
        }

        for (int i = 0; i < alphabet.Length; i++)
        {
            buffer[pos] = alphabet[i];
            foreach (var result in GenerateRecursive(buffer, pos + 1, halfLen, totalLen, alphabet))
                yield return result;
        }
    }
}

/// <summary>
/// Psychosis seeds: pattern ABAxBxxx where A,B are A-Z and x are free pad positions (always 8 chars).
/// Letter skeleton stays A–Z so the word shape stays visible; free slots take the pad alphabet.
/// </summary>
file static class PsychosisAestheticSeeds
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static long GetSeedCount(char[]? paddingAlphabet)
    {
        char[] free = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        checked
        {
            long freePow = 1;
            for (int i = 0; i < 4; i++)
                freePow *= free.Length;
            return 26L * 26L * freePow;
        }
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        // Reuse one 8-char buffer instead of interpolating a fresh string per seed.
        // Pattern ABAxBxxx: positions 0,2 = a; positions 1,4 = b; positions 3,5,6,7 = free.
        char[] buffer = new char[8];

        foreach (char a in Letters)
        {
            buffer[0] = a;
            buffer[2] = a;
            foreach (char b in Letters)
            {
                buffer[1] = b;
                buffer[4] = b;
                for (int i1 = 0; i1 < alphabet.Length; i1++)
                {
                    buffer[3] = alphabet[i1];
                    for (int i2 = 0; i2 < alphabet.Length; i2++)
                    {
                        buffer[5] = alphabet[i2];
                        for (int i3 = 0; i3 < alphabet.Length; i3++)
                        {
                            buffer[6] = alphabet[i3];
                            for (int i4 = 0; i4 < alphabet.Length; i4++)
                            {
                                buffer[7] = alphabet[i4];
                                yield return new string(buffer);
                            }
                        }
                    }
                }
            }
        }
    }
}

/// <summary>
/// Mirror-symmetric seeds: composed only of chars that look the same in a mirror
/// (A H I M O T U V W X Y 1 8), intersected with the pad alphabet when provided.
/// Lengths 1..MaxSeedLength.
/// </summary>
file static class MirrorAestheticSeeds
{
    private static readonly char[] MirrorChars = "AHIMOTUVWXY18".ToCharArray();

    private static char[] EffectiveAlphabet(char[]? paddingAlphabet)
    {
        // Full aesthetic: all mirror-looking glyphs.
        if (paddingAlphabet is not { Length: > 0 })
            return MirrorChars;

        // Quick / custom pad: only mirror glyphs that appear in the pad set (digits → 1,8).
        var set = new HashSet<char>(paddingAlphabet);
        var result = MirrorChars.Where(set.Contains).ToArray();
        return result.Length > 0 ? result : MirrorChars;
    }

    public static long GetSeedCount(char[]? paddingAlphabet)
    {
        char[] alphabet = EffectiveAlphabet(paddingAlphabet);
        checked
        {
            long total = 0;
            int c = alphabet.Length;
            if (c == 0)
                return 0;
            long pow = 1;
            for (int len = 1; len <= MotelyGlobals.MaxSeedLength; len++)
            {
                pow *= c;
                total += pow;
            }
            return total;
        }
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        char[] alphabet = EffectiveAlphabet(paddingAlphabet);
        if (alphabet.Length == 0)
            yield break;

        char[] buf = new char[MotelyGlobals.MaxSeedLength];

        for (int len = 1; len <= MotelyGlobals.MaxSeedLength; len++)
        {
            foreach (var seed in Generate(buf, 0, len, alphabet))
                yield return seed;
        }
    }

    private static IEnumerable<string> Generate(char[] buf, int pos, int length, char[] alphabet)
    {
        if (pos == length)
        {
            yield return new string(buf, 0, length);
            yield break;
        }

        foreach (char c in alphabet)
        {
            buf[pos] = c;
            foreach (var seed in Generate(buf, pos + 1, length, alphabet))
                yield return seed;
        }
    }
}

/// <summary>
/// Repeater seeds: a base pattern that exactly tiles all 8 characters. Patterns drawn from the pad
/// alphabet. Only periods 1, 2, and 4 qualify; a 6-character prefix plus two repeated characters
/// is not a repeated 8-character seed.
/// </summary>
file static class RepeaterAestheticSeeds
{
    private static readonly int[] PatternLengths = [1, 2, 4];

    public static long GetSeedCount(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        checked
        {
            int c = alphabet.Length;
            long total = 0;

            foreach (int p in PatternLengths)
            {
                long patterns = 1;
                for (int i = 0; i < p; i++)
                    patterns *= c;
                total += patterns;
            }

            return total;
        }
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        char[] buf = new char[MotelyGlobals.MaxSeedLength];

        foreach (int patternLen in PatternLengths)
        {
            char[] pattern = new char[patternLen];
            foreach (var filled in GeneratePatterns(pattern, 0, alphabet, buf))
                yield return filled;
        }
    }

    private static IEnumerable<string> GeneratePatterns(
        char[] pattern,
        int pos,
        char[] alphabet,
        char[] buf
    )
    {
        if (pos == pattern.Length)
        {
            for (int i = 0; i < buf.Length; i++)
                buf[i] = pattern[i % pattern.Length];
            yield return new string(buf);
            yield break;
        }

        foreach (char c in alphabet)
        {
            pattern[pos] = c;
            foreach (var seed in GeneratePatterns(pattern, pos + 1, alphabet, buf))
                yield return seed;
        }
    }
}

/// <summary>
/// Step seeds: evenly-spaced alphabet steps over the pad alphabet. Always 8 chars.
/// |alphabet|² seeds (start × step).
/// </summary>
file static class StepAestheticSeeds
{
    public static long GetSeedCount(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        return checked((long)alphabet.Length * alphabet.Length);
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        int n = alphabet.Length;
        char[] buf = new char[MotelyGlobals.MaxSeedLength];

        for (int step = 0; step < n; step++)
        {
            for (int start = 0; start < n; start++)
            {
                for (int i = 0; i < buf.Length; i++)
                    buf[i] = alphabet[(start + i * step) % n];
                yield return new string(buf);
            }
        }
    }
}

/// <summary>
/// Pi seeds: every 8-character window of π's first <see cref="DigitCount"/> digits, starting at
/// <c>31415926</c>. The seed alphabet has no <c>0</c>, so <c>O</c> stands in for it. The digits are
/// fixed, so the pad alphabet does not apply.
/// </summary>
file static class PiAestheticSeeds
{
    public const int DigitCount = 10_000;

    private static readonly Lazy<string> Digits = new(() => ComputeDigits(DigitCount).Replace('0', 'O'));

    public static long GetSeedCount(char[]? paddingAlphabet) => DigitCount - MotelyGlobals.MaxSeedLength + 1;

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        string digits = Digits.Value;
        for (int i = 0; i + MotelyGlobals.MaxSeedLength <= digits.Length; i++)
            yield return digits.Substring(i, MotelyGlobals.MaxSeedLength);
    }

    // Machin: π = 16·arccot(5) − 4·arccot(239), in fixed point with ten guard digits.
    private static string ComputeDigits(int count)
    {
        var unity = BigInteger.Pow(10, count + 10);
        var pi = 4 * (4 * ArcCot(5, unity) - ArcCot(239, unity));
        return pi.ToString()[..count];
    }

    private static BigInteger ArcCot(int x, BigInteger unity)
    {
        BigInteger sum = 0;
        BigInteger power = unity / x;
        BigInteger xSquared = x * x;
        for (int n = 1, sign = 1; !power.IsZero; n += 2, sign = -sign)
        {
            sum += sign * (power / n);
            power /= xSquared;
        }

        return sum;
    }
}

/// <summary>
/// Spin seeds: 180° ambigrams over lengths 1..<see cref="MotelyGlobals.MaxSeedLength"/>. Turned upside
/// down the seed reads the same: <c>H I N O S X Z 8</c> map to themselves, <c>M</c>↔<c>W</c> and
/// <c>6</c>↔<c>9</c> swap, so <c>SWIMS</c>, <c>MOW</c>, <c>NOON</c> and <c>69</c> all qualify.
/// A pad alphabet keeps only the glyphs (and whole pairs) it contains.
/// </summary>
file static class SpinAestheticSeeds
{
    private const string Turnable = "689HIMNOSWXZ";
    private const string TurnedUp = "986HIWNOSMXZ";

    private static (char[] All, char[] Self) EffectiveAlphabet(char[]? paddingAlphabet)
    {
        var all = Turnable.ToCharArray();
        if (paddingAlphabet is { Length: > 0 })
        {
            var pad = new HashSet<char>(paddingAlphabet);
            var kept = all.Where(c => pad.Contains(c) && pad.Contains(Turn(c))).ToArray();
            if (kept.Length > 0)
                all = kept;
        }

        return (all, all.Where(static c => Turn(c) == c).ToArray());
    }

    private static char Turn(char c) => TurnedUp[Turnable.IndexOf(c)];

    public static long GetSeedCount(char[]? paddingAlphabet)
    {
        var (all, self) = EffectiveAlphabet(paddingAlphabet);
        checked
        {
            long total = 0;
            long halves = 1;
            for (int len = 1; len <= MotelyGlobals.MaxSeedLength; len++)
            {
                if (len % 2 == 0)
                {
                    halves *= all.Length;
                    total += halves;
                }
                else
                {
                    total += halves * self.Length;
                }
            }

            return total;
        }
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        var (all, self) = EffectiveAlphabet(paddingAlphabet);
        char[] buf = new char[MotelyGlobals.MaxSeedLength];
        for (int len = 1; len <= MotelyGlobals.MaxSeedLength; len++)
        {
            foreach (var seed in Generate(buf, 0, len, all, self))
                yield return seed;
        }
    }

    private static IEnumerable<string> Generate(char[] buf, int pos, int length, char[] all, char[] self)
    {
        int half = length / 2;
        if (pos == half)
        {
            if (length % 2 == 0)
            {
                yield return new string(buf, 0, length);
                yield break;
            }

            foreach (char c in self)
            {
                buf[half] = c;
                yield return new string(buf, 0, length);
            }

            yield break;
        }

        foreach (char c in all)
        {
            buf[pos] = c;
            buf[length - 1 - pos] = Turn(c);
            foreach (var seed in Generate(buf, pos + 1, length, all, self))
                yield return seed;
        }
    }
}

/// <summary>
/// Fibonacci seeds: pick the first two characters, then each next one is the sum of the two before
/// it (as positions in the pad alphabet, mod its size). Always 8 chars; |alphabet|² seeds.
/// </summary>
file static class FibonacciAestheticSeeds
{
    public static long GetSeedCount(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        return checked((long)alphabet.Length * alphabet.Length);
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        char[] alphabet = MotelyAesthetics.AlphabetOrFull(paddingAlphabet);
        int n = alphabet.Length;
        int[] index = new int[MotelyGlobals.MaxSeedLength];
        char[] buf = new char[MotelyGlobals.MaxSeedLength];

        for (int first = 0; first < n; first++)
        {
            for (int second = 0; second < n; second++)
            {
                index[0] = first;
                index[1] = second;
                for (int i = 2; i < index.Length; i++)
                    index[i] = (index[i - 1] + index[i - 2]) % n;
                for (int i = 0; i < buf.Length; i++)
                    buf[i] = alphabet[index[i]];
                yield return new string(buf);
            }
        }
    }
}
