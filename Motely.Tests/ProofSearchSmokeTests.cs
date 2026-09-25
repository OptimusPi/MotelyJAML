namespace Motely.Tests;

public sealed class ProofSearchSmokeTests
{
    private const string Permissive = """
        name: proof-search-smoke
        deck: Red
        stake: White
        must:
          - joker: []
            antes: [1]
        """;

    [Fact]
    public void MustMatchAll_KnownSeed()
    {
        ProofSearch.MustMatchAll(Permissive, "UNITTEST");
    }

    [Fact]
    public void MustMatchNone_RejectsWhenFilterImpossible()
    {
        const string hard = """
            name: hard-miss
            deck: Red
            stake: White
            must:
              - rareJoker: Blueprint
                antes: [1]
                min: 2
            """;
        ProofSearch.MustMatchNone(hard, "ZZZZZZZZ");
    }
}
