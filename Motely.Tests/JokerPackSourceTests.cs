namespace Motely.Tests;

/// <summary>
/// The buffoon-pack half of the common/uncommon joker filters — per-lane pack size, ante-1 slot
/// reachability, edition and sticker narrowing. Seeds here were found by real CLI searches and
/// then pinned with their non-matching neighbours, so each case proves both that the path finds
/// what it should and that it rejects what it should.
/// </summary>
public sealed class JokerPackSourceTests
{
    private const string CommonJokerInPacks = """
        name: common-joker-packs
        deck: Red
        stake: White
        must:
          - commonJoker: [Joker, GreedyJoker, LustyJoker]
            antes: [1, 2]
            sources:
              boosterPacks: [0, 1, 2]
        """;

    private const string CommonJokerShopOnly = """
        name: common-joker-shop-only
        deck: Red
        stake: White
        must:
          - commonJoker: [Joker, GreedyJoker, LustyJoker]
            antes: [1, 2]
            sources:
              shopItems: [0, 1, 2, 3]
        """;

    private const string UncommonWildcardInPacks = """
        name: uncommon-wildcard-packs
        deck: Red
        stake: White
        must:
          - uncommonJoker: []
            antes: [1, 2, 3]
            sources:
              boosterPacks: [0, 1, 2]
        """;

    private const string UncommonWildcardInPacksNegativeEdition = """
        name: uncommon-wildcard-packs-negative
        deck: Red
        stake: White
        must:
          - uncommonJoker: []
            antes: [1, 2, 3]
            edition: Negative
            sources:
              boosterPacks: [0, 1, 2]
        """;

    /// <summary>Found by CLI search against <c>CommonJokerInPacks</c>.</summary>
    private static readonly string[] CommonPackHits = ["1D1", "1Z1", "262", "323"];
    private static readonly string[] CommonPackMisses = ["MM", "NN", "ALEEB", "UNITTEST"];

    /// <summary>Found by CLI search against <c>UncommonWildcardInPacks</c>.</summary>
    private static readonly string[] UncommonPackHits = ["EE", "MM", "NN", "P", "UNITTEST"];
    private static readonly string[] UncommonPackMisses = ["1D1", "ALEEB"];

    [Fact]
    public void CommonJoker_InBuffoonPacks_MatchesTheFoundSeeds() =>
        ProofSearch.MustMatchAll(CommonJokerInPacks, CommonPackHits);

    [Fact]
    public void CommonJoker_InBuffoonPacks_RejectsTheNeighbours() =>
        ProofSearch.MustMatchNone(CommonJokerInPacks, CommonPackMisses);

    [Fact]
    public void UncommonJoker_WildcardInBuffoonPacks_MatchesTheFoundSeeds() =>
        ProofSearch.MustMatchAll(UncommonWildcardInPacks, UncommonPackHits);

    [Fact]
    public void UncommonJoker_WildcardInBuffoonPacks_RejectsTheNeighbours() =>
        ProofSearch.MustMatchNone(UncommonWildcardInPacks, UncommonPackMisses);

    /// <summary>
    /// R2: packs and shop are genuinely different streams. If <c>sources:</c> were being ignored,
    /// these two clauses would select the same seeds.
    /// </summary>
    [Fact]
    public void PackSources_AndShopSources_SelectDifferentSeeds()
    {
        var all = CommonPackHits.Concat(CommonPackMisses).ToArray();

        var viaPacks = ProofSearch.ListMatch(CommonJokerInPacks, all);
        var viaShop = ProofSearch.ListMatch(CommonJokerShopOnly, all);

        Assert.Equal(CommonPackHits.Length, (int)viaPacks.Matching);
        Assert.NotEqual(
            viaPacks.Matched.OrderBy(static s => s, StringComparer.Ordinal).ToArray(),
            viaShop.Matched.OrderBy(static s => s, StringComparer.Ordinal).ToArray()
        );
    }

    /// <summary>
    /// R2: an edition is a narrowing, never a widening. Negative uncommons are rare enough that
    /// this list yields none — the assertion that matters is the subset relation, which would
    /// break the moment the edition branch stopped being applied.
    /// </summary>
    [Fact]
    public void Edition_NarrowsTheWildcardMatchSet()
    {
        var all = UncommonPackHits.Concat(UncommonPackMisses).ToArray();

        var unfiltered = ProofSearch.ListMatch(UncommonWildcardInPacks, all);
        var negativeOnly = ProofSearch.ListMatch(UncommonWildcardInPacksNegativeEdition, all);

        Assert.True(
            negativeOnly.Matching <= unfiltered.Matching,
            "edition must narrow, never widen, the wildcard match set"
        );
        Assert.Subset(
            unfiltered.Matched.ToHashSet(StringComparer.Ordinal),
            negativeOnly.Matched.ToHashSet(StringComparer.Ordinal)
        );
    }

    // ── stickers ──

    private const string CommonWildcardEternalWhiteStake = """
        name: common-wildcard-eternal-white
        deck: Red
        stake: White
        must:
          - commonJoker: []
            antes: [2, 3]
            stickers: [Eternal]
            sources:
              shopItems: [0, 1, 2, 3]
              boosterPacks: [0, 1]
        """;

    private const string CommonWildcardNoStickerWhiteStake = """
        name: common-wildcard-white
        deck: Red
        stake: White
        must:
          - commonJoker: []
            antes: [2, 3]
            sources:
              shopItems: [0, 1, 2, 3]
              boosterPacks: [0, 1]
        """;

    /// <summary>
    /// Eternal is gated to Black stake and above in the engine
    /// (<c>MotelySingleSearchContext.Jokers.cs</c>), so at White stake this clause is provably
    /// unsatisfiable — a sequential search for it never terminates. The same seeds match freely
    /// once the sticker requirement is dropped, which is what makes this a stake gate rather than
    /// an empty seed list.
    /// </summary>
    [Fact]
    public void EternalSticker_AtWhiteStake_MatchesNothing()
    {
        var all = CommonPackHits.Concat(UncommonPackHits).Distinct().ToArray();

        var withSticker = ProofSearch.ListMatch(CommonWildcardEternalWhiteStake, all);
        var withoutSticker = ProofSearch.ListMatch(CommonWildcardNoStickerWhiteStake, all);

        Assert.Equal(0L, withSticker.Matching);
        Assert.True(
            withoutSticker.Matching > 0,
            "the same seeds must match without the sticker, or this proves nothing about stakes"
        );
    }
}
