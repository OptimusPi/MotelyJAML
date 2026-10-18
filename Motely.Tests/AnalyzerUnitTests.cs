namespace Motely.Tests;

public sealed class AnalyzerUnitTests
{

    [Theory]
    [InlineData("1234567")]
    [InlineData("12345678")]
    [InlineData("ALEEB")]
    [InlineData("ALEEBOOO")]
    [InlineData("UNITTES")]
    [InlineData("UNITTEST")]
    [InlineData("KK1XD111", MotelyDeck.Ghost, MotelyStake.Black)]
    public async Task TestAnalyzer(string seed, MotelyDeck deck = MotelyDeck.Red, MotelyStake stake = MotelyStake.White)
    {
        string actualOutput = GetAnalyzerOutput(seed, deck, stake);

        await Verify(actualOutput)
            .UseFileName(seed)
            .UseDirectory("seeds");
    }

    /// <summary>
    /// A seed the engine cannot search never reaches the filter. Analyze returned null for it, and
    /// the CLI's <c>--analyze ABCDEFGHIJK</c> died reading <c>.Error</c> off that null.
    /// </summary>
    [Theory]
    [InlineData("ABCDEFGHIJK")]
    [InlineData("")]
    [InlineData("OO0O")]
    public void TestAnalyzer_UnsearchableSeed_ReturnsError(string seed)
    {
        var analysis = MotelyUnitTestAnalyzer.Analyze(new(seed, MotelyDeck.Red, MotelyStake.White));
        Assert.NotNull(analysis);
        Assert.False(string.IsNullOrEmpty(analysis.Error));
    }

    private string GetAnalyzerOutput(string seed, MotelyDeck deck = MotelyDeck.Red, MotelyStake stake = MotelyStake.White)
    {
        return MotelyUnitTestAnalyzer.Analyze(new(seed, deck, stake)).ToString();
    }

    [Fact]
    public void TestAnalyzer_PackContentsFormat()
    {
        string seed = "UNITTEST";
        var output = GetAnalyzerOutput(seed);

        Assert.Contains("Buffoon Pack - ", output);
        Assert.Contains("Arcana Pack - ", output);
        Assert.Contains("Standard Pack - ", output);

        Assert.Contains("Mega Standard Pack - ", output);
        Assert.Contains("Mega Arcana Pack - ", output);
        Assert.Contains("Mega Celestial Pack - ", output);
    }

    [Fact]
    public void TestAnalyzer_TagsNotActivated()
    {
        string seed = "UNITTEST";
        var output = GetAnalyzerOutput(seed);

        var lines = output.Split('\n');
        bool inAnte1 = false;
        int packCount = 0;

        foreach (var line in lines)
        {
            if (line.Contains("==ANTE 1=="))
            {
                inAnte1 = true;
            }
            else if (line.Contains("==ANTE 2=="))
            {
                break;
            }
            else if (inAnte1 && IsPackLine(line.Trim()))
            {
                packCount++;
            }
        }

        Assert.Equal(4, packCount);

        static bool IsPackLine(string line) =>
            line.StartsWith("Buffoon Pack")
            || line.StartsWith("Arcana Pack")
            || line.StartsWith("Celestial Pack")
            || line.StartsWith("Spectral Pack")
            || line.StartsWith("Standard Pack")
            || line.StartsWith("Jumbo")
            || line.StartsWith("Mega");
    }
}
