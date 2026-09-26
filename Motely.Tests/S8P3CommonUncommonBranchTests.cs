namespace Motely.Tests;

/// <summary>
/// S8.P3 — Common/Uncommon joker filter residual branches, same recipe as
/// <see cref="S8P2RareJokerBranchTests"/>: Gold-stake stickers and ante-1 extended pack
/// slots, list-proved on the wide fixture list with pinned match sets (SIMD filter +
/// scalar must re-eval agree before a seed is reported).
/// </summary>
public sealed class S8P3CommonUncommonBranchTests
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
            name: s8p3-cu
            {body}
            """,
            WideSeeds
        );
        return (matching, [.. matched.OrderBy(s => s, StringComparer.Ordinal)]);
    }

    /// <summary>Eternal-stickered commons exist on 23 of 28 seeds — the excluded five
    /// (696, 88, F, MOTELY77, VV) prove the sticker mask is a live gate.</summary>
    [Fact]
    public void CommonJoker_GoldEternal_GatesFiveSeedsOut()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: Gold
            must:
              - commonJoker: []
                antes: [1, 2]
                stickers: [Eternal]
            """
        );
        Assert.Equal(23L, matching);
        Assert.DoesNotContain("696", matched);
        Assert.DoesNotContain("F", matched);
        Assert.DoesNotContain("MOTELY77", matched);
        Assert.DoesNotContain("VV", matched);
        Assert.DoesNotContain("88", matched);
    }

    [Fact]
    public void UncommonJoker_GoldRentalOrPerishable_KnownSet()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: Gold
            must:
              - uncommonJoker: []
                antes: [1, 2]
                stickers: [Rental, Perishable]
            """
        );
        Assert.Equal(4L, matching);
        Assert.Equal(["111", "R", "UNITTEST", "VV"], matched);
    }

    [Fact]
    public void CommonJoker_Ante1ExtendedPackSlots_KnownCount()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: White
            must:
              - commonJoker: []
                antes: [1]
                sources:
                  boosterPacks: [0, 1, 2, 3, 4, 5]
            """
        );
        Assert.Equal(26L, matching);
        Assert.DoesNotContain("GHG", matched);
        Assert.DoesNotContain("UNITTEST", matched);
    }

    [Fact]
    public void UncommonJoker_Ante1ExtendedPackSlots_KnownSet()
    {
        var (matching, matched) = Run(
            """
            deck: Red
            stake: White
            must:
              - uncommonJoker: []
                antes: [1]
                sources:
                  boosterPacks: [0, 1, 2, 3, 4, 5]
            """
        );
        Assert.Equal(18L, matching);
        Assert.Equal(
            ["111", "2A2", "3X3", "474", "616", "696", "6J6", "88", "99",
             "FMF", "GHG", "H", "I", "MOTELY77", "R", "UNITTEST", "VV", "Z"],
            matched
        );
    }
}
