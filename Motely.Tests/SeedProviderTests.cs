using System.Collections;
using System.Runtime.CompilerServices;

namespace Motely.Tests;

public sealed class SeedProviderTests
{
    private const string ValidSeedChars = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static void AssertValidSeed(ReadOnlySpan<char> seed)
    {
        Assert.InRange(seed.Length, 1, 8);
        Assert.All(seed.ToArray(), c => Assert.Contains(c, ValidSeedChars));
    }

    [Fact]
    public void RandomSeedProvider_GeneratesBoundedSeedsAndStops()
    {
        var provider = new MotelyRandomSeedProvider(5);
        Assert.Equal(5, provider.SeedCount);

        int generated = 0;
        while (true)
        {
            var seed = provider.NextSeed();
            if (seed.Length == 0)
                break;
            AssertValidSeed(seed);
            generated++;
        }
        Assert.Equal(5, generated);
        Assert.True(provider.NextSeed().Length == 0);
    }

    [Fact]
    public void RandomSeedProvider_NextSeedsRespectsCountAndEmptyArray()
    {
        var provider = new MotelyRandomSeedProvider(3);
        var buffer = new string[5];
        Assert.Equal(0, provider.NextSeeds([]));
        Assert.Equal(3, provider.NextSeeds(buffer));
        for (int i = 0; i < 3; i++)
            AssertValidSeed(buffer[i]);
        Assert.Equal(0, provider.NextSeeds(buffer));
    }

    [Fact]
    public void SeedListProvider_ListSearchResolvesCountAndYieldsInOrder()
    {
        var seeds = new List<string> { "ABC12345", "XYZ98765", "MOTELY77" };
        var provider = new MotelySeedListProvider(seeds);
        Assert.Equal(3, provider.SeedCount);

        Assert.Equal("ABC12345", provider.NextSeed().ToString());
        Assert.Equal("XYZ98765", provider.NextSeed().ToString());

        var buffer = new string[5];
        Assert.Equal(1, provider.NextSeeds(buffer));
        Assert.Equal("MOTELY77", buffer[0]);
        Assert.True(provider.NextSeed().Length == 0);
    }

    [Fact]
    public void SeedListProvider_YieldEnumerableLeavesCountUnknownAndDrains()
    {
        IEnumerable<string> YieldSeeds()
        {
            yield return "ONE";
            yield return "TWO";
        }

        var provider = new MotelySeedListProvider(YieldSeeds());
        Assert.Equal(-1, provider.SeedCount);
        Assert.Equal("ONE", provider.NextSeed().ToString());
        Assert.Equal("TWO", provider.NextSeed().ToString());
        Assert.True(provider.NextSeed().Length == 0);
    }

    [Fact]
    public void SeedListProvider_ReadOnlyCollectionResolvesCount()
    {
        IReadOnlyCollection<string> seeds = new[] { "A", "B" };
        var provider = new MotelySeedListProvider(seeds);
        Assert.Equal(2, provider.SeedCount);
    }

    [Fact]
    public void SeedListProvider_NonGenericCollectionResolvesCount()
    {
        ICollection collection = new[] { "A", "B" };
        var provider = new MotelySeedListProvider(collection.Cast<string>()!);
        Assert.Equal(2, provider.SeedCount);
    }

    [Fact]
    public void SeedListProvider_DisposeIsIdempotent()
    {
        var provider = new MotelySeedListProvider(new[] { "A" });
        provider.Dispose();
        provider.Dispose();
    }

    [Fact]
    public void PalindromeSeedProvider_YieldsFirstPalindromes()
    {
        var provider = new MotelyPalindromeSeedProvider();
        Assert.Equal(3_089_520, provider.SeedCount);

        Assert.Equal("1", provider.NextSeed().ToString());
        Assert.Equal("2", provider.NextSeed().ToString());
        Assert.Equal("3", provider.NextSeed().ToString());

        var buffer = new string[3];
        Assert.Equal(3, provider.NextSeeds(buffer));
        Assert.Equal(["4", "5", "6"], buffer);
    }

    [Fact]
    public void PsychosisSeedProvider_YieldsFirstPsychosisSeeds()
    {
        var provider = new MotelyPsychosisSeedProvider();
        Assert.Equal(1_014_422_500, provider.SeedCount);

        Assert.Equal("AAA1A111", provider.NextSeed().ToString());
        Assert.Equal("AAA1A112", provider.NextSeed().ToString());

        var buffer = new string[2];
        Assert.Equal(2, provider.NextSeeds(buffer));
        Assert.Equal(["AAA1A113", "AAA1A114"], buffer);
    }

    [Fact]
    public void AestheticSeedProvider_GrossYieldsKeywordSeeds()
    {
        var provider = new MotelyAestheticSeedProvider(JamlAesthetic.Gross);
        Assert.Equal(MotelySeedKeywordSequences.GrossKeywordAestheticSeedCount, provider.SeedCount);

        var first = provider.NextSeed().ToString();
        Assert.InRange(first.Length, 4, 8);
        AssertValidSeed(first);

        var buffer = new string[2];
        Assert.Equal(2, provider.NextSeeds(buffer));
        Assert.All(buffer, s => AssertValidSeed(s));
    }

    [Fact]
    public void AestheticQuickPadding_CollapsesFreeSlotsToDigits()
    {
        char[] pad = JamlAesthetics.QuickPaddingChars;

        Assert.Equal(26L * 26L * (long)Math.Pow(9, 4), JamlAesthetics.GetSeedCount(JamlAesthetic.Psychosis, pad));
        Assert.Equal(14_760, JamlAesthetics.GetSeedCount(JamlAesthetic.Palindrome, pad));
        Assert.Equal(81, JamlAesthetics.GetSeedCount(JamlAesthetic.Step, pad));

        Assert.Equal(1_014_422_500, JamlAesthetics.GetSeedCount(JamlAesthetic.Psychosis));

        var provider = new MotelyAestheticSeedProvider(JamlAesthetic.Psychosis, pad);
        Assert.Equal(JamlAesthetics.GetSeedCount(JamlAesthetic.Psychosis, pad), provider.SeedCount);
        var first = provider.NextSeed().ToString();
        Assert.Equal(8, first.Length);
        Assert.All(new[] { 3, 5, 6, 7 }, i => Assert.Contains(first[i], pad));
        AssertValidSeed(first);
    }

    [Fact]
    public void RepeaterProvider_MatchesTheCanonicalAestheticOrder()
    {
        char[] smallAlphabet = "123".ToCharArray();
        var expected = JamlAesthetics
            .EnumerateSeeds(JamlAesthetic.Repeater, smallAlphabet)
            .ToArray();
        var provider = new MotelyRepeaterSeedProvider(smallAlphabet);
        var actual = new string[expected.Length];

        Assert.Equal(actual.Length, provider.NextSeeds(actual));
        Assert.Equal(expected, actual);
        Assert.Equal(
            JamlAesthetics.GetSeedCount(JamlAesthetic.Repeater, smallAlphabet),
            provider.SeedCount
        );

        var fullProvider = new MotelyRepeaterSeedProvider();
        Assert.Equal("11111111", fullProvider.SeedAt(0));
        Assert.Equal("ZZZYZZZY", fullProvider.SeedAt(fullProvider.SeedCount - 2));
        Assert.Equal("ZZZZZZZZ", fullProvider.SeedAt(fullProvider.SeedCount - 1));
    }

    [Fact]
    public void RunsAesthetic_GeneratesFourCharacterChunksAtEveryOffset()
    {
        char[] pad = "12".ToCharArray();
        var seeds = JamlAesthetics.EnumerateSeeds(JamlAesthetic.Runs, pad).ToArray();

        Assert.Equal(
            JamlAesthetics.GetSeedCount(JamlAesthetic.Runs, pad),
            seeds.LongLength
        );
        Assert.Contains("11111111", seeds);
        Assert.Contains("21111112", seeds);
        Assert.Contains("12111111", seeds);
        Assert.All(
            seeds,
            seed => Assert.True(
                MotelyGlobals.SeedDigits.Any(character => seed.Contains(new string(character, 4))),
                $"'{seed}' has no four-character run"
            )
        );
    }

    [Fact]
    public void KeywordSeedProvider_PadsKeywordsAndExhausts()
    {
        var keywords = new[] { "FART", "UNIT" };
        var provider = new MotelyKeywordSeedProvider(keywords);
        Assert.Equal(MotelyGlobals.GetPaddedSeedCountForKeywordsLong(keywords), provider.SeedCount);

        var first = provider.NextSeed().ToString();
        Assert.InRange(first.Length, 4, 8);
        AssertValidSeed(first);

        var buffer = new string[4];
        Assert.Equal(4, provider.NextSeeds(buffer));
        Assert.All(buffer, s => AssertValidSeed(s));
    }

    [Fact]
    public void KeywordSeedProvider_ExplicitPaddingCharsRestrictsAlphabet()
    {
        var provider = new MotelyKeywordSeedProvider(["HI"], ['1', '2']);
        Assert.Equal(448L, provider.SeedCount);

        var first = provider.NextSeed().ToString();
        Assert.All(first.ToArray(), c => Assert.Contains(c, "HI12"));
        Assert.Equal(5, provider.NextSeeds(new string[5]));
        Assert.True(provider.NextSeed().ToString().All(c => "HI12".Contains(c)));
    }

    [Fact]
    public void AsyncSeedListProvider_MirrorsListProvider()
    {
        var provider = new MotelyAsyncSeedListProvider(GetSeedsAsync());
        Assert.Equal(-1, provider.SeedCount);

        Assert.Equal("A1", provider.NextSeed().ToString());
        var buffer = new string[3];
        Assert.Equal(2, provider.NextSeeds(buffer));
        Assert.Equal("A2", buffer[0]);
        Assert.Equal("A3", buffer[1]);
        Assert.Null(buffer[2]);
        Assert.True(provider.NextSeed().Length == 0);

        provider.Dispose();

        async IAsyncEnumerable<string> GetSeedsAsync()
        {
            await Task.Yield();
            yield return "A1";
            yield return "A2";
            yield return "A3";
        }
    }

    [Fact]
    public void AsyncSeedListProvider_DisposeAfterPartialRead()
    {
        var provider = new MotelyAsyncSeedListProvider(GetSeedsAsync());
        Assert.Equal("B1", provider.NextSeed().ToString());
        provider.Dispose();
        Assert.True(provider.NextSeed().Length == 0);

        async IAsyncEnumerable<string> GetSeedsAsync()
        {
            await Task.Yield();
            yield return "B1";
            yield return "B2";
        }
    }
}
