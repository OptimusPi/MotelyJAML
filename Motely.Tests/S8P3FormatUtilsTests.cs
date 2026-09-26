namespace Motely.Tests;

/// <summary>
/// S8.P3 — FormatUtils display contract. The strong law is the Format ↔ Parse round trip:
/// anything <c>FormatItem</c> prints, <c>ParseMotelyItem</c> reads back to the same item.
/// </summary>
public sealed class S8P3FormatUtilsTests
{
    [Fact]
    public void FormatJokerName_SplitsPascalCase()
    {
        Assert.Equal("Lucky Cat", FormatUtils.FormatJokerName(MotelyJoker.LuckyCat));
        Assert.Equal("Blueprint", FormatUtils.FormatJokerName(MotelyJoker.Blueprint));
    }

    [Theory]
    [InlineData("C", "Clubs")]
    [InlineData("D", "Diamonds")]
    [InlineData("H", "Hearts")]
    [InlineData("S", "Spades")]
    [InlineData("X", "Unknown")]
    public void FormatStandardcardSuit_MapsAbbreviations(string abbr, string expected)
    {
        Assert.Equal(expected, FormatUtils.FormatStandardcardSuit(abbr));
    }

    [Theory]
    [InlineData("2", "2")]
    [InlineData("10", "10")]
    [InlineData("J", "Jack")]
    [InlineData("Q", "Queen")]
    [InlineData("K", "King")]
    [InlineData("A", "Ace")]
    public void FormatStandardcardRank_MapsAbbreviations(string abbr, string expected)
    {
        Assert.Equal(expected, FormatUtils.FormatStandardcardRank(abbr));
    }

    [Fact]
    public void FormatStandardcardRank_InvalidAbbreviationThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FormatUtils.FormatStandardcardRank("Z")
        );
    }

    [Fact]
    public void FormatPackName_UnknownValueFallsBackToEnumText()
    {
        Assert.Equal(((MotelyBoosterPack)999).ToString(),
            FormatUtils.FormatPackName((MotelyBoosterPack)999));
    }

    public static TheoryData<MotelyItem> RoundTripItems()
    {
        return
        [
            new MotelyItem(MotelyJoker.Blueprint),
            new MotelyItem(MotelyJoker.LuckyCat).WithEdition(MotelyItemEdition.Negative),
            new MotelyItem(MotelyJoker.Perkeo).WithEdition(MotelyItemEdition.Foil),
            new MotelyItem(MotelyJoker.GrosMichel).WithEternal(true),
            new MotelyItem(MotelyJoker.Cavendish).WithPerishable(true),
            new MotelyItem(MotelyJoker.Banner).WithRental(true),
            new MotelyItem(MotelyJoker.Baron)
                .WithEdition(MotelyItemEdition.Polychrome)
                .WithEternal(true)
                .WithRental(true),
        ];
    }

    [Theory]
    [MemberData(nameof(RoundTripItems))]
    public void FormatItem_ParseMotelyItem_RoundTrips(MotelyItem item)
    {
        var text = FormatUtils.FormatItem(item);
        var parsed = FormatUtils.ParseMotelyItem(text);
        Assert.Equal(item.Value, parsed.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Complete Gibberish Item")]
    public void TryParseMotelyItem_RejectsGarbage(string text)
    {
        Assert.False(FormatUtils.TryParseMotelyItem(text, out _));
    }

    [Fact]
    public void ParseMotelyItem_GarbageThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => FormatUtils.ParseMotelyItem("Not An Item"));
    }
}
