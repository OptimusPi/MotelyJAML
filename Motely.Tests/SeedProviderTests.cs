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
        var provider = new MotelyAestheticSeedProvider(MotelyAesthetic.Palindrome);
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
        var provider = new MotelyAestheticSeedProvider(MotelyAesthetic.Psychosis);
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
        var provider = new MotelyAestheticSeedProvider(MotelyAesthetic.Gross);
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
        char[] pad = MotelyAesthetics.QuickPaddingChars;

        // Digit free slots: psychosis free^4 only, letter skeleton A–Z stays.
        Assert.Equal(26L * 26L * (long)Math.Pow(9, 4), MotelyAesthetics.GetSeedCount(MotelyAesthetic.Psychosis, pad));
        Assert.Equal(14_760, MotelyAesthetics.GetSeedCount(MotelyAesthetic.Palindrome, pad));
        Assert.Equal(81, MotelyAesthetics.GetSeedCount(MotelyAesthetic.Step, pad));

        // Full alphabet still matches historical baked sizes when pad is null.
        Assert.Equal(1_014_422_500, MotelyAesthetics.GetSeedCount(MotelyAesthetic.Psychosis));

        var provider = new MotelyAestheticSeedProvider(MotelyAesthetic.Psychosis, pad);
        Assert.Equal(MotelyAesthetics.GetSeedCount(MotelyAesthetic.Psychosis, pad), provider.SeedCount);
        var first = provider.NextSeed().ToString();
        Assert.Equal(8, first.Length);
        // Free slots (indices 3,5,6,7) are digits only under quick pad.
        Assert.All(new[] { 3, 5, 6, 7 }, i => Assert.Contains(first[i], pad));
        AssertValidSeed(first);
    }

    [Fact]
    public void RepeaterProvider_MatchesTheCanonicalAestheticOrder()
    {
        char[] smallAlphabet = "123".ToCharArray();
        var expected = MotelyAesthetics
            .EnumerateSeeds(MotelyAesthetic.Repeater, smallAlphabet)
            .ToArray();
        var provider = new MotelyRepeaterSeedProvider(smallAlphabet);
        var actual = new string[expected.Length];

        Assert.Equal(actual.Length, provider.NextSeeds(actual));
        Assert.Equal(expected, actual);
        Assert.Equal(
            MotelyAesthetics.GetSeedCount(MotelyAesthetic.Repeater, smallAlphabet),
            provider.SeedCount
        );

        var fullProvider = new MotelyRepeaterSeedProvider();
        Assert.Equal("11111111", fullProvider.SeedAt(0));
        Assert.Equal("ZZZYZZZY", fullProvider.SeedAt(fullProvider.SeedCount - 2));
        Assert.Equal("ZZZZZZZZ", fullProvider.SeedAt(fullProvider.SeedCount - 1));
    }

    [Fact]
    public void PiAesthetic_SlidesAcrossPisDigits_WithOForZero()
    {
        var seeds = MotelyAesthetics.EnumerateSeeds(MotelyAesthetic.Pi).ToArray();

        Assert.Equal(9_993, MotelyAesthetics.GetSeedCount(MotelyAesthetic.Pi));
        Assert.Equal(9_993, seeds.Length);
        Assert.Equal(["31415926", "14159265", "41592653"], seeds[..3]);
        // π's first 0 is decimal 32; the alphabet has no 0, so O plays it.
        Assert.Equal("3832795O", seeds[25]);
        // The Feynman point: six 9s from decimal 762.
        Assert.Contains(seeds, static s => s.Contains("999999"));
        Assert.All(seeds, static s => Assert.DoesNotContain('0', s));
    }

    [Fact]
    public void SpinAesthetic_ReadsTheSameUpsideDown()
    {
        const string turnable = "689HIMNOSWXZ";
        const string turned = "986HIWNOSMXZ";
        static string UpsideDown(string s) =>
            new([.. s.Reverse().Select(c => turned[turnable.IndexOf(c)])]);

        var seeds = MotelyAesthetics.EnumerateSeeds(MotelyAesthetic.Spin).ToArray();

        Assert.Equal(37_700, MotelyAesthetics.GetSeedCount(MotelyAesthetic.Spin));
        Assert.Equal(37_700, seeds.Length);
        Assert.Equal(seeds.Length, seeds.Distinct().Count());
        Assert.All(seeds, s => Assert.Equal(s, UpsideDown(s)));
        Assert.Subset(seeds.ToHashSet(), new HashSet<string> { "SWIMS", "MOW", "NOON", "SOS", "69" });
        Assert.DoesNotContain("SWIM", seeds);
    }

    [Fact]
    public void FibonacciAesthetic_EachCharIsTheSumOfTheTwoBefore()
    {
        char[] pad = MotelyAesthetics.QuickPaddingChars;
        var seeds = MotelyAesthetics.EnumerateSeeds(MotelyAesthetic.Fibonacci, pad).ToArray();

        Assert.Equal(81, MotelyAesthetics.GetSeedCount(MotelyAesthetic.Fibonacci, pad));
        Assert.Equal(81, seeds.Length);
        // Positions 0,1,1,2,3,5,8,13 mod 9 = 4, over "123456789".
        Assert.Equal("12234695", seeds[1]);
        Assert.All(seeds, static s =>
        {
            for (int i = 2; i < s.Length; i++)
                Assert.Equal((s[i - 1] - '1' + s[i - 2] - '1') % 9, s[i] - '1');
        });
    }

    [Theory]
    [InlineData("pi", MotelyAesthetic.Pi)]
    [InlineData("spin", MotelyAesthetic.Spin)]
    [InlineData("fibonacci", MotelyAesthetic.Fibonacci)]
    public void NewAestheticsParseAndJoinAll(string name, MotelyAesthetic aesthetic)
    {
        Assert.True(MotelyAestheticParser.TryParse(name, out var parsed));
        Assert.Equal(aesthetic, parsed);
        Assert.Contains(aesthetic, MotelyAestheticParser.AllAesthetics());
    }

    [Fact]
    public void RunsAesthetic_GeneratesFourCharacterChunksAtEveryOffset()
    {
        char[] pad = "12".ToCharArray();
        var seeds = MotelyAesthetics.EnumerateSeeds(MotelyAesthetic.Runs, pad).ToArray();

        Assert.Equal(
            MotelyAesthetics.GetSeedCount(MotelyAesthetic.Runs, pad),
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

        // Each keyword is 4 chars, so padLen == 4 and each yields 5 * 35^4 = 600,250 seeds.
        // Grabbing a few should be trivial and deterministic.
        var buffer = new string[4];
        Assert.Equal(4, provider.NextSeeds(buffer));
        Assert.All(buffer, s => AssertValidSeed(s));
    }

    [Fact]
    public void KeywordSeedProvider_ExplicitPaddingCharsRestrictsAlphabet()
    {
        // "HI" length 2 => padLen 6 => 2^6 * 7 positions = 448 seeds.
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

    private const string PermissiveJaml = "name: p\nmust: []\nshould: []\n";

    [Fact]
    public void ProviderSearch_AllInvalidFirstPack_KeepsSearchingTheRest()
    {
        // Eight seeds with '0' (not in the alphabet) fill the first SIMD pack on their own.
        string[] seeds = ["10", "20", "30", "40", "50", "60", "70", "80", "AAAAAAAA"];
        var (matching, matched) = ProofSearch.ListMatch(PermissiveJaml, seeds);
        Assert.Equal(1L, matching);
        Assert.Equal(["AAAAAAAA"], matched);
    }

    [Fact]
    public void ProviderSearch_AllInvalidPackMidList_KeepsTrailingSeeds()
    {
        var seeds = new List<string> { "AAAAAAAA" };
        for (int i = 0; i < 8; i++)
            seeds.Add("0000000" + i);
        seeds.Add("BBBBBBBB");
        var (matching, _) = ProofSearch.ListMatch(PermissiveJaml, [.. seeds]);
        Assert.Equal(2L, matching);
    }

    [Fact]
    public void ProviderSearch_OnlyInvalidSeeds_CompletesWithNothingSearched()
    {
        var (matching, _) = ProofSearch.ListMatch(PermissiveJaml, ["10", "20", "30"]);
        Assert.Equal(0L, matching);
    }
}
