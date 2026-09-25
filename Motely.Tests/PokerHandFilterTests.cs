namespace Motely.Tests;

public sealed class PokerHandFilterTests
{
    private const string FourOfAKind = """
        name: poker-quads
        deck: Red
        stake: White
        must:
          - pokerHand: FourOfAKind
            antes: [1]
        """;

    private const string StraightFlush = """
        name: poker-sf
        deck: Red
        stake: White
        must:
          - pokerHand: StraightFlush
            antes: [1]
        """;

    private const string PairOrBetter = """
        name: poker-pair-up
        deck: Red
        stake: White
        must:
          - pokerHand: [Pair, TwoPair, ThreeOfAKind, Straight, Flush, FullHouse, FourOfAKind, StraightFlush]
            antes: [1]
        """;

    [Fact]
    public void LoadsAndDiscriminates()
    {
        Assert.True(JamlConfigLoader.TryLoad(FourOfAKind, out var config, out var error), error);
        Assert.NotNull(config);
        var clause = Assert.IsType<PokerHandClause>(config.Must[0]);
        Assert.Equal([MotelyPokerHand.FourOfAKind], clause.PokerHands);
        Assert.Equal([1], clause.Antes);
    }

    [Fact]
    public void FourOfAKind_KnownSeedsMatch()
    {
        ProofSearch.MustMatchAll(FourOfAKind, "5S5", "D7D", "7I7", "KEK", "K5K");
    }

    [Fact]
    public void StraightFlush_KnownSeedsMatch()
    {
        ProofSearch.MustMatchAll(StraightFlush, "4K4", "9", "GGG");
    }

    [Fact]
    public void PairOrBetter_MatchesKnownQuadSeed()
    {
        ProofSearch.MustMatchAll(PairOrBetter, "5S5");
    }
}
