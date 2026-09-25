namespace Motely.Tests;

public sealed class S8P2RareJokerBranchTests
{
    private static readonly string[] WideSeeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF",
    ];

    private static (long Matching, string[] Matched) Run(string body)
    {
        var (matching, matched) = ProofSearch.ListMatch(
            $"""
            name: s8p2-rare
            {body}
            """,
            WideSeeds
        );
        return (matching, [.. matched.OrderBy(s => s, StringComparer.Ordinal)]);
    }

    private const string GoldAny = """
        deck: Red
        stake: Gold
        must:
          - rareJoker: []
            antes: [1, 2, 3, 4]
        """;

    [Fact]
    public void GoldStake_WildcardRare_KnownSet()
    {
        var (matching, matched) = Run(GoldAny);
        Assert.Equal(18L, matching);
        Assert.Equal(
            ["3X3", "474", "4C4", "5X5", "616", "696", "6J6", "ALEEB", "CC",
             "CUC", "FMF", "H", "I", "MOTELY", "MOTELY77", "Q", "R", "Z"],
            matched
        );
    }

    [Fact]
    public void GoldStake_EternalSticker_GatesToSubset()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: Gold
            must:
              - rareJoker: []
                antes: [1, 2, 3, 4]
                stickers: [Eternal]
            """
        );
        Assert.Equal(6L, matching);
        Assert.Equal(["474", "5X5", "CC", "CUC", "H", "Z"], matched);
    }

    [Fact]
    public void GoldStake_PerishableOrRental_GatesToSubset()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: Gold
            must:
              - rareJoker: []
                antes: [1, 2, 3, 4]
                stickers: [Perishable, Rental]
            """
        );
        Assert.Equal(1L, matching);
        Assert.Equal(["4C4"], matched);
    }

    [Fact]
    public void SparseShopSlots_KnownSet()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: White
            must:
              - rareJoker: []
                antes: [1, 2, 3, 4]
                sources:
                  shopItems: [2, 5]
            """
        );
        Assert.Equal(6L, matching);
        Assert.Equal(["3X3", "6J6", "ALEEB", "CC", "H", "MOTELY77"], matched);
    }

    [Fact]
    public void Ante1PackSlots_WithExtension_KnownSet()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: White
            must:
              - rareJoker: []
                antes: [1]
                sources:
                  boosterPacks: [0, 1, 2, 3, 4, 5]
            """
        );
        Assert.Equal(4L, matching);
        Assert.Equal(["2A2", "99", "GHG", "H"], matched);
    }
}
