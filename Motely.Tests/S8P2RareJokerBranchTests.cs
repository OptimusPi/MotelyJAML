namespace Motely.Tests;

/// <summary>
/// S8.P2 — RareJokerFilter branch proofs: Gold-stake sticker gates, sparse shop slots,
/// ante-1 pack-slot extension with Jumbo/Mega buffoon lanes. List proofs over the wide
/// fixture list, match sets pinned from the engine's own dual-path run (SIMD filter +
/// scalar must re-eval agree before a seed is reported).
/// </summary>
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

    /// <summary>
    /// Eternal sticker on Gold stake: strict subset of the unstickered wildcard — the
    /// sticker mask is a real gate, and it reads the eternal/perishable PRNG stream that
    /// only exists at Black stake and above.
    /// </summary>
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

    /// <summary>Perishable|Rental exercises the remaining sticker switch arms (rental
    /// stream is Gold-stake-only).</summary>
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

    /// <summary>Sparse shop slots ({2, 5}) force the non-target slot skip while the
    /// stream still advances every slot — order-within-key law.</summary>
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

    /// <summary>
    /// Ante-1 buffoon packs with slots 4-5 requested: the filter computes the
    /// Hieroglyph/Petroglyph extension mask and walks Jumbo/Mega extra card lanes.
    /// </summary>
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
