namespace Motely.SeedProviders;

/// <summary>
/// Seed-space constraints declared under top-level <c>aesthetics</c> in a JAML document.
/// Enumeration and classification: <see cref="JamlAesthetics"/>.
/// </summary>
public enum JamlAesthetic
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
}

/// <summary>
/// Generation and counting of JAML <see cref="JamlAesthetic"/> seed spaces over Motely's alphabet
/// and length rules. Palindrome/Psychosis/Mirror/Repeater/Runs/Step live here; keyword-backed aesthetics
/// (<see cref="JamlAesthetic.Gross"/>, <see cref="JamlAesthetic.Funny"/>,
/// <see cref="JamlAesthetic.Balatro"/>, <see cref="JamlAesthetic.Nsfw"/>) delegate to
/// <see cref="MotelySeedKeywordSequences"/>.
/// <para>
/// <b>Padding alphabet:</b> free / generated positions use <paramref name="paddingAlphabet"/> when
/// provided (CLI <c>--padding</c>). Null keeps the full <see cref="MotelyGlobals.SeedDigits"/> space
/// (historical full aesthetic). Collect's aesthetic prepass defaults to
/// <see cref="QuickPaddingChars"/> so words stay visible and the stream is searchable.
/// </para>
/// </summary>
public static class JamlAesthetics
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
    public static long GetSeedCount(JamlAesthetic aesthetic, char[]? paddingAlphabet = null) =>
        aesthetic switch
        {
            JamlAesthetic.Palindrome => PalindromeAestheticSeeds.GetSeedCount(paddingAlphabet),
            JamlAesthetic.Psychosis => PsychosisAestheticSeeds.GetSeedCount(paddingAlphabet),
            JamlAesthetic.Mirror => MirrorAestheticSeeds.GetSeedCount(paddingAlphabet),
            JamlAesthetic.Repeater => RepeaterAestheticSeeds.GetSeedCount(paddingAlphabet),
            JamlAesthetic.Runs => RunsAestheticSeeds.GetSeedCount(paddingAlphabet),
            JamlAesthetic.Step => StepAestheticSeeds.GetSeedCount(paddingAlphabet),
            JamlAesthetic.Gross
                or JamlAesthetic.Funny
                or JamlAesthetic.Balatro
                or JamlAesthetic.Leet
                or JamlAesthetic.Nsfw => MotelySeedKeywordSequences.GetAestheticSeedCount(
                aesthetic,
                paddingAlphabet
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(aesthetic)),
        };

    /// <summary>Deterministic enumeration; order matches historical full-alphabet providers when pad is null.</summary>
    public static IEnumerable<string> EnumerateSeeds(
        JamlAesthetic aesthetic,
        char[]? paddingAlphabet = null
    ) =>
        aesthetic switch
        {
            JamlAesthetic.Palindrome => PalindromeAestheticSeeds.Enumerate(paddingAlphabet),
            JamlAesthetic.Psychosis => PsychosisAestheticSeeds.Enumerate(paddingAlphabet),
            JamlAesthetic.Mirror => MirrorAestheticSeeds.Enumerate(paddingAlphabet),
            JamlAesthetic.Repeater => RepeaterAestheticSeeds.Enumerate(paddingAlphabet),
            JamlAesthetic.Runs => RunsAestheticSeeds.Enumerate(paddingAlphabet),
            JamlAesthetic.Step => StepAestheticSeeds.Enumerate(paddingAlphabet),
            JamlAesthetic.Gross
                or JamlAesthetic.Funny
                or JamlAesthetic.Balatro
                or JamlAesthetic.Leet
                or JamlAesthetic.Nsfw => MotelySeedKeywordSequences.EnumerateAestheticSeeds(
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
        char[] alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
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
        char[] alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
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
        char[] free = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
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
        char[] alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
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
        char[] alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
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
        char[] alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
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
        char[] alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
        return checked((long)alphabet.Length * alphabet.Length);
    }

    public static IEnumerable<string> Enumerate(char[]? paddingAlphabet)
    {
        char[] alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);
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
