namespace Motely.SeedProviders;

public interface IMotelySeedProvider
{
    public long SeedCount { get; }
    public string NextSeed();

    public int NextSeeds(string[] seeds);
}

public sealed class MotelyRandomSeedProvider(int seedCount) : IMotelySeedProvider
{
    public long SeedCount { get; } = seedCount;
    private int _seedsGenerated;

    public string NextSeed()
    {
        if (Interlocked.Increment(ref _seedsGenerated) > SeedCount)
            return string.Empty;

        return string.Create(
            MotelyGlobals.MaxSeedLength,
            (object?)null,
            static (buf, _) => Random.Shared.GetItems(MotelyGlobals.SeedDigits, buf)
        );
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds is not { Length: > 0 })
            return 0;

        int filled = 0;
        for (int i = 0; i < seeds.Length; i++)
        {
            if (Interlocked.Increment(ref _seedsGenerated) > SeedCount)
                break;

            seeds[i] = string.Create(
                MotelyGlobals.MaxSeedLength,
                (object?)null,
                static (buf, _) => Random.Shared.GetItems(MotelyGlobals.SeedDigits, buf)
            );
            filled++;
        }
        return filled;
    }
}

public sealed class MotelyPalindromeSeedProvider : IMotelySeedProvider
{
    public long SeedCount { get; } = JamlAesthetics.GetSeedCount(JamlAesthetic.Palindrome);

    private readonly IEnumerator<string> _palindromeEnumerator;
    private readonly object _enumeratorLock = new();

    public MotelyPalindromeSeedProvider()
    {
        _palindromeEnumerator = JamlAesthetics
            .EnumerateSeeds(JamlAesthetic.Palindrome)
            .GetEnumerator();
    }

    public string NextSeed()
    {
        lock (_enumeratorLock)
        {
            if (_palindromeEnumerator.MoveNext())
            {
                return _palindromeEnumerator.Current;
            }
            return string.Empty;
        }
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            return 0;

        lock (_enumeratorLock)
        {
            int count = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                if (!_palindromeEnumerator.MoveNext())
                    break;
                seeds[i] = _palindromeEnumerator.Current;
                count++;
            }
            return count;
        }
    }
}

public sealed class MotelyPsychosisSeedProvider : IMotelySeedProvider
{
    public long SeedCount { get; } = JamlAesthetics.GetSeedCount(JamlAesthetic.Psychosis);

    private readonly IEnumerator<string> _psychosisEnumerator;
    private readonly object _enumeratorLock = new();

    public MotelyPsychosisSeedProvider()
    {
        _psychosisEnumerator = JamlAesthetics.EnumerateSeeds(JamlAesthetic.Psychosis).GetEnumerator();
    }

    public string NextSeed()
    {
        lock (_enumeratorLock)
        {
            if (_psychosisEnumerator.MoveNext())
            {
                return _psychosisEnumerator.Current;
            }
            return string.Empty;
        }
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            return 0;

        lock (_enumeratorLock)
        {
            int count = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                if (!_psychosisEnumerator.MoveNext())
                    break;
                seeds[i] = _psychosisEnumerator.Current;
                count++;
            }
            return count;
        }
    }
}

public sealed class MotelyAestheticSeedProvider : IMotelySeedProvider
{
    public long SeedCount { get; }

    private readonly IEnumerator<string> _enumerator;
    private readonly object _enumeratorLock = new();

    public MotelyAestheticSeedProvider(JamlAesthetic aesthetic, char[]? paddingAlphabet = null)
    {
        SeedCount = JamlAesthetics.GetSeedCount(aesthetic, paddingAlphabet);
        _enumerator = JamlAesthetics.EnumerateSeeds(aesthetic, paddingAlphabet).GetEnumerator();
    }

    public string NextSeed()
    {
        lock (_enumeratorLock)
        {
            if (_enumerator.MoveNext())
                return _enumerator.Current;
            return string.Empty;
        }
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            return 0;

        lock (_enumeratorLock)
        {
            int count = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                if (!_enumerator.MoveNext())
                    break;
                seeds[i] = _enumerator.Current;
                count++;
            }
            return count;
        }
    }
}

public sealed class MotelyRepeaterSeedProvider : IMotelySeedProvider
{
    private static readonly int[] PatternLengths = [1, 2, 4];
    private readonly char[] _alphabet;
    private readonly long[] _firstIndexByPatternLength = new long[PatternLengths.Length];
    private long _nextIndex;

    public long SeedCount { get; }

    public MotelyRepeaterSeedProvider(char[]? paddingAlphabet = null)
    {
        _alphabet = JamlAesthetics.AlphabetOrFull(paddingAlphabet);

        long total = 0;
        long patterns = 1;
        checked
        {
            for (int i = 0; i < PatternLengths.Length; i++)
            {
                int patternLength = PatternLengths[i];
                _firstIndexByPatternLength[i] = total;
                while (patterns < Math.Pow(_alphabet.Length, patternLength))
                    patterns *= _alphabet.Length;
                total += patterns;
            }
        }
        SeedCount = total;
    }

    public string NextSeed()
    {
        long index = Interlocked.Increment(ref _nextIndex) - 1;
        return index < SeedCount ? SeedAt(index) : string.Empty;
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds is not { Length: > 0 })
            return 0;

        long start = Interlocked.Add(ref _nextIndex, seeds.Length) - seeds.Length;
        if (start >= SeedCount)
            return 0;

        int count = (int)Math.Min(seeds.Length, SeedCount - start);
        for (int i = 0; i < count; i++)
            seeds[i] = SeedAt(start + i);
        return count;
    }

    internal string SeedAt(long index)
    {
        if (index < 0 || index >= SeedCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        int patternLengthIndex = 0;
        for (; patternLengthIndex < PatternLengths.Length - 1; patternLengthIndex++)
            if (index < _firstIndexByPatternLength[patternLengthIndex + 1])
                break;

        int patternLength = PatternLengths[patternLengthIndex];
        long patternIndex = index - _firstIndexByPatternLength[patternLengthIndex];
        return string.Create(
            MotelyGlobals.MaxSeedLength,
            (_alphabet, patternLength, patternIndex),
            static (seed, state) =>
            {
                Span<char> pattern = stackalloc char[state.patternLength];
                for (int i = state.patternLength - 1; i >= 0; i--)
                {
                    pattern[i] = state._alphabet[(int)(state.patternIndex % state._alphabet.Length)];
                    state.patternIndex /= state._alphabet.Length;
                }
                for (int i = 0; i < seed.Length; i++)
                    seed[i] = pattern[i % pattern.Length];
            }
        );
    }
}

public sealed class MotelyKeywordSeedProvider : IMotelySeedProvider
{
    public long SeedCount { get; }

    private readonly IEnumerator<string> _enumerator;
    private readonly object _enumeratorLock = new();

    public MotelyKeywordSeedProvider(IEnumerable<string> keywords, char[]? paddingChars = null)
    {
        SeedCount = MotelyGlobals.GetPaddedSeedCountForKeywordsLong(keywords, paddingChars);
        _enumerator = MotelyGlobals
            .GeneratePaddedSeedsForKeywords(keywords, paddingChars)
            .GetEnumerator();
    }

    public string NextSeed()
    {
        lock (_enumeratorLock)
        {
            if (_enumerator.MoveNext())
            {
                return _enumerator.Current;
            }
            return string.Empty;
        }
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            return 0;

        lock (_enumeratorLock)
        {
            int count = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                if (!_enumerator.MoveNext())
                    break;
                seeds[i] = _enumerator.Current;
                count++;
            }
            return count;
        }
    }
}

public sealed class MotelySeedListProvider : IMotelySeedProvider
{
    private readonly IEnumerator<string> _seedEnumerator;

    private readonly object _enumeratorLock = new();

    public long SeedCount { get; private set; } = -1;

    public MotelySeedListProvider(IEnumerable<string> seeds, long seedCount = -1)
    {
        _seedEnumerator = seeds.GetEnumerator();
        SeedCount = ResolveSeedCount(seeds, seedCount);
    }

    private static long ResolveSeedCount(IEnumerable<string> seeds, long seedCount)
    {
        if (seedCount >= 0)
            return seedCount;

        if (seeds is ICollection<string> collection)
            return collection.Count;

        if (seeds is IReadOnlyCollection<string> readOnlyCollection)
            return readOnlyCollection.Count;

        if (seeds is System.Collections.ICollection nonGenericCollection)
            return nonGenericCollection.Count;

        return -1;
    }

    public string NextSeed()
    {
        lock (_enumeratorLock)
        {
            return _seedEnumerator.MoveNext() ? _seedEnumerator.Current : string.Empty;
        }
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            return 0;

        lock (_enumeratorLock)
        {
            int count = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                if (!_seedEnumerator.MoveNext())
                    break;

                seeds[i] = _seedEnumerator.Current;
                count++;
            }
            return count;
        }
    }

    public void Dispose()
    {
        lock (_enumeratorLock)
        {
            _seedEnumerator?.Dispose();
        }
    }
}

public sealed class MotelyChainedSeedProvider(IMotelySeedProvider first, IMotelySeedProvider second)
    : IMotelySeedProvider
{
    private bool _firstExhausted;

    public long SeedCount { get; } =
        first.SeedCount >= 0 && second.SeedCount >= 0
            ? first.SeedCount + second.SeedCount
            : -1;

    public string NextSeed()
    {
        if (!_firstExhausted)
        {
            string seed = first.NextSeed();
            if (seed.Length != 0)
                return seed;
            _firstExhausted = true;
        }
        return second.NextSeed();
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds == null || seeds.Length == 0)
            return 0;

        int count = 0;
        if (!_firstExhausted)
        {
            count = first.NextSeeds(seeds);
            if (count < seeds.Length)
                _firstExhausted = true;
        }

        if (count < seeds.Length)
        {
            var remaining = new string[seeds.Length - count];
            int gotFromSecond = second.NextSeeds(remaining);
            Array.Copy(remaining, 0, seeds, count, gotFromSecond);
            count += gotFromSecond;
        }

        return count;
    }
}

public sealed class MotelyAsyncSeedListProvider : IMotelySeedProvider, IDisposable, IAsyncDisposable
{
    private readonly IAsyncEnumerable<string> _seeds;
    private readonly CancellationToken _cancellationToken;

    private IAsyncEnumerator<string>? _enumerator;
    private string? _currentSeed;
    private readonly object _enumeratorLock = new();
    private bool _disposed;

    public long SeedCount { get; }

    public MotelyAsyncSeedListProvider(
        IAsyncEnumerable<string> seeds,
        long seedCount = -1,
        CancellationToken cancellationToken = default
    )
    {
        _seeds = seeds ?? throw new ArgumentNullException(nameof(seeds));
        SeedCount = seedCount;
        _cancellationToken = cancellationToken;
    }

    private IAsyncEnumerator<string> EnsureEnumerator()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _enumerator ??= _seeds.GetAsyncEnumerator(_cancellationToken);
    }

    private static bool MoveNextSync(IAsyncEnumerator<string> enumerator)
    {
        return enumerator.MoveNextAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public string NextSeed()
    {
        lock (_enumeratorLock)
        {
            if (_disposed)
                return string.Empty;

            var enumerator = EnsureEnumerator();
            if (!MoveNextSync(enumerator))
                return string.Empty;

            _currentSeed = enumerator.Current;
            return _currentSeed;
        }
    }

    public int NextSeeds(string[] seeds)
    {
        if (seeds is not { Length: > 0 })
            return 0;

        lock (_enumeratorLock)
        {
            if (_disposed)
                return 0;

            var enumerator = EnsureEnumerator();
            int count = 0;
            for (int i = 0; i < seeds.Length; i++)
            {
                if (!MoveNextSync(enumerator))
                    break;
                seeds[i] = enumerator.Current;
                count++;
            }

            return count;
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        IAsyncEnumerator<string>? enumerator;
        lock (_enumeratorLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            enumerator = _enumerator;
            _enumerator = null;
        }

        if (enumerator != null)
            await enumerator.DisposeAsync().ConfigureAwait(false);
    }
}
