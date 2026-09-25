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

    private string GetAnalyzerOutput(string seed, MotelyDeck deck = MotelyDeck.Red, MotelyStake stake = MotelyStake.White)
    {
        return MotelyUnitTestAnalyzer.Analyze(new(seed, deck, stake)).ToString();
    }

    private void AssertOutputsMatch(string expected, string actual, string seed)
    {
        expected = expected.Replace("\r\n", "\n").Trim();
        actual = actual.Replace("\r\n", "\n").Trim();

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        Assert.Equal(expectedLines.Length, actualLines.Length);

        for (int i = 0; i < expectedLines.Length; i++)
        {
            var expectedLine = expectedLines[i].TrimEnd();
            var actualLine = actualLines[i].TrimEnd();

            if (expectedLine != actualLine)
            {
                var message = $"Seed {seed} - Line {i + 1} mismatch:\n" +
                              $"Expected: {expectedLine}\n" +
                              $"Actual:   {actualLine}";
                Assert.Fail(message);
            }
        }
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
            else if (inAnte1 && line.Trim().StartsWith("Buffoon Pack") ||
                     line.Trim().StartsWith("Arcana Pack") ||
                     line.Trim().StartsWith("Celestial Pack") ||
                     line.Trim().StartsWith("Spectral Pack") ||
                     line.Trim().StartsWith("Standard Pack") ||
                     line.Trim().StartsWith("Jumbo") ||
                     line.Trim().StartsWith("Mega"))
            {
                packCount++;
            }
        }

        Assert.Equal(4, packCount);
    }
}
