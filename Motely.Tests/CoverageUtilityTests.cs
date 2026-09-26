namespace Motely.Tests;

public sealed class CoverageUtilityTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("1", 1)]
    [InlineData("9", 9)]
    [InlineData("A", 10)]
    [InlineData("Z", 35)]
    [InlineData("11", 36)]
    [InlineData("11111111", 66231629136)]
    public void SeedMath_TotalIndexRoundTrips(string seed, long index)
    {
        Assert.Equal(index, SeedMath.SeedToTotalIndex(seed));
        Assert.Equal(seed, SeedMath.TotalIndexToSeed(index));
    }

    [Theory]
    [InlineData("11111111", 0)]
    [InlineData("11111112", 1)]
    [InlineData("1111111Z", 34)]
    [InlineData("11111121", 35)]
    public void SeedMath_SearchIndexRoundTrips(string seed, long index)
    {
        Assert.Equal(index, SeedMath.SeedToSearchIndex(seed));
        Assert.Equal(seed, SeedMath.SearchIndexToSeed(index, seed.Length));
    }

    [Fact]
    public void SeedMath_BatchAndRangeHelpersUseInclusiveSearchIndices()
    {
        Assert.Equal(0, SeedMath.GetFirstSeedOfLength(0));
        Assert.Equal(1, SeedMath.GetFirstSeedOfLength(1));
        Assert.Equal(36, SeedMath.GetFirstSeedOfLength(2));
        Assert.Equal(35 * 35 - 1, SeedMath.MaxSearchIndexInclusive(2));

        Assert.Equal(0, SeedMath.SeedToBatchIndex("11111111", 3));
        Assert.Equal("11111", SeedMath.BatchIndexToSeedPrefix(0, 3));

        var range = SeedMath.SearchIndexRangeToBatchRange(0, 34, 1);
        Assert.Equal(0, range.StartBatchIndex);
        Assert.Equal(1, range.EndBatchIndexExclusive);
    }

    /// <summary>
    /// The two seeds a real interrupted run printed, from the batch it printed beside them. Pinned
    /// as literals because the failure they guard is silent: the old resume seed put the batch
    /// digits at the head, which reads back as batch 0, so resuming restarted the whole sweep.
    /// </summary>
    [Theory]
    [InlineData(409387, 4, "1111S7KA")]
    [InlineData(1449672, 4, "11118FTY")]
    [InlineData(44799, 4, "1111ZK22")]
    [InlineData(0, 4, "11111111")]
    public void SeedMath_BatchIndexToFirstSeed_PutsBatchDigitsInTheTail(
        long batchIndex,
        int batchCharCount,
        string expected
    )
    {
        Assert.Equal(expected, SeedMath.BatchIndexToFirstSeed(batchIndex, batchCharCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    public void SeedMath_BatchIndexToFirstSeed_RoundTripsThroughSeedToBatchIndex(int batchCharCount)
    {
        long max = (long)Math.Pow(35, 8 - batchCharCount);
        foreach (
            long batch in new[] { 0L, 1L, 34L, 35L, 1234L, max / 2, max - 1 }
                .Where(b => b >= 0 && b < max)
                .Distinct()
        )
        {
            string seed = SeedMath.BatchIndexToFirstSeed(batch, batchCharCount);
            Assert.Equal(8, seed.Length);
            Assert.Equal(batch, SeedMath.SeedToBatchIndex(seed, batchCharCount));
        }
    }

    [Fact]
    public void SeedMath_BatchIndexToFirstSeed_RejectsBatchesOutsideTheSpace()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedMath.BatchIndexToFirstSeed(-1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SeedMath.BatchIndexToFirstSeed((long)Math.Pow(35, 4), 4)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedMath.BatchIndexToFirstSeed(0, 8));
    }

    [Fact]
    public void SeedMath_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => SeedMath.SeedToTotalIndex("10"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedMath.TotalIndexToSeed(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SeedMath.SearchIndexRangeToBatchRange(0, 1, 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SeedMath.SearchIndexRangeToBatchRange(2, 1, 1)
        );
    }

    [Fact]
    public void KeywordSequences_BasicGeneratorsAreLazyAndDeterministic()
    {
        Assert.Equal("AAA", MotelySeedKeywordSequences.RepeatCharKeywords(3).First());
        Assert.Equal("ZZZ", MotelySeedKeywordSequences.RepeatCharKeywords(3).Last());
        Assert.Equal(26, MotelySeedKeywordSequences.RepeatCharKeywords(3).Count());

        Assert.Equal("1234", MotelySeedKeywordSequences.AscendingDigitLetterKeywords(4).First());
        Assert.Equal("WXYZ", MotelySeedKeywordSequences.AscendingDigitLetterKeywords(4).Last());
        Assert.Equal("ZYXW", MotelySeedKeywordSequences.DescendingDigitLetterKeywords(4).First());
        Assert.Equal("4321", MotelySeedKeywordSequences.DescendingDigitLetterKeywords(4).Last());

        var mirrors = MotelySeedKeywordSequences.MirrorPatternKeywords(2).ToArray();
        Assert.Equal(13 * 13, mirrors.Length);
        Assert.Contains("AA", mirrors);
        Assert.Contains("88", mirrors);
    }

    [Fact]
    public void KeywordSequences_AestheticCountsAndValidationArePinned()
    {
        // Each baked constant must be recomputed from its own keyword array. Comparing the
        // constant to GetAestheticSeedCount(aesthetic) is a tautology for the null-pad case —
        // that call returns the constant — so the live count is the only real pin.
        var baked = string.Join(
            " ",
            MotelySeedKeywordSequences.GrossKeywordAestheticSeedCount,
            MotelySeedKeywordSequences.FunnyKeywordAestheticSeedCount,
            MotelySeedKeywordSequences.BalatroKeywordAestheticSeedCount,
            MotelySeedKeywordSequences.LeetKeywordAestheticSeedCount,
            MotelySeedKeywordSequences.NsfwKeywordAestheticSeedCount
        );
        var live = string.Join(
            " ",
            MotelyGlobals.GetPaddedSeedCountForKeywordsLong(
                MotelySeedKeywordSequences.GrossKeywords
            ),
            MotelyGlobals.GetPaddedSeedCountForKeywordsLong(
                MotelySeedKeywordSequences.FunnyKeywords
            ),
            MotelyGlobals.GetPaddedSeedCountForKeywordsLong(
                MotelySeedKeywordSequences.BalatroKeywords
            ),
            MotelyGlobals.GetPaddedSeedCountForKeywordsLong(MotelySeedKeywordSequences.LeetKeywords),
            MotelyGlobals.GetPaddedSeedCountForKeywordsLong(MotelySeedKeywordSequences.NsfwKeywords)
        );
        Assert.Equal(baked, live);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MotelySeedKeywordSequences.GetAestheticSeedCount(JamlAesthetic.Palindrome)
        );

        foreach (
            var keywords in new[]
            {
                MotelySeedKeywordSequences.GrossKeywords,
                MotelySeedKeywordSequences.FunnyKeywords,
                MotelySeedKeywordSequences.BalatroKeywords,
                MotelySeedKeywordSequences.NsfwKeywords,
            }
        )
        {
            Assert.NotEmpty(keywords);
            Assert.All(
                keywords,
                keyword =>
                {
                    Assert.InRange(keyword.Length, 1, 8);
                    Assert.All(
                        keyword.ToUpperInvariant(),
                        c => Assert.Contains(c, MotelyGlobals.SeedDigits)
                    );
                }
            );
        }
    }

    [Fact]
    public void NativeFilterNames_ParseEveryDisplayNameAndFactoryCreatesSettings()
    {
        Assert.Equal(
            Enum.GetValues<MotelyNativeFilter>().Length,
            MotelyNativeFilterNames.DisplayNames.Length
        );

        foreach (var expected in Enum.GetValues<MotelyNativeFilter>())
        {
            var name = MotelyNativeFilterNames.DisplayNames[(int)expected];
            Assert.True(MotelyNativeFilterNames.TryParse(name, out var parsed));
            Assert.Equal(expected, parsed);
            Assert.NotNull(MotelyNativeFilterFactory.CreateSettings(parsed));
        }
    }

    [Fact]
    public void NativeFilterNames_RejectUnknownAndFactoryRejectsOutOfRange()
    {
        Assert.False(MotelyNativeFilterNames.TryParse("not-a-filter", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MotelyNativeFilterFactory.CreateSettings((MotelyNativeFilter)999)
        );
    }
}
