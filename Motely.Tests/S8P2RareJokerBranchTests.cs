namespace Motely.Tests;

/// <summary>
/// S8.P2 — RareJokerFilter branch proofs: Gold-stake sticker gates, sparse shop slots,
/// ante-1 pack-slot extension with Jumbo/Mega buffoon lanes.
///
/// Every expected match set is derived in this file by walking the raw shop / pack streams
/// for each fixture seed — never pinned from an earlier engine run, which would only prove
/// the engine agrees with itself. The search (SIMD filter + scalar must re-eval) must then
/// report exactly that set.
/// </summary>
public sealed class S8P2RareJokerBranchTests
{
    private static readonly string[] WideSeeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF",
    ];

    private static readonly int[] DefaultShopSlots = [0, 1, 2, 3, 4, 5, 6, 7];

    private static string[] Run(string body)
    {
        var (matching, matched) = ProofSearch.ListMatch(
            $"""
            name: s8p2-rare
            {body}
            """,
            WideSeeds
        );
        Assert.Equal(matching, matched.Count());
        return [.. matched.OrderBy(s => s, StringComparer.Ordinal)];
    }

    /// <summary>Seeds for which <paramref name="hasRare"/> holds, walked on the given stake.</summary>
    private static string[] Expected(MotelyStake stake, Func<MotelySingleSearchContext, bool> hasRare)
    {
        var hits = new List<string>();
        using var search = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(stake)
            .WithSeedGenerator(WideSeeds, WideSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(ctx =>
            {
                if (hasRare(ctx))
                    lock (hits)
                        hits.Add(ctx.GetSeed());
                return 1;
            })
            .Start();
        search.AwaitCompletion();
        Assert.Equal((long)WideSeeds.Length, search.MatchingSeeds);
        return [.. hits.OrderBy(s => s, StringComparer.Ordinal)];
    }

    private static bool IsRareJoker(MotelyItem item, MotelyJokerSticker[] stickers)
    {
        if (item.TypeCategory != MotelyItemTypeCategory.Joker)
            return false;
        if ((MotelyJokerRarity)((int)item.Type & MotelyGlobals.JokerRarityMask) != MotelyJokerRarity.Rare)
            return false;
        foreach (var sticker in stickers)
        {
            bool has = sticker switch
            {
                MotelyJokerSticker.Eternal => item.IsEternal,
                MotelyJokerSticker.Perishable => item.IsPerishable,
                MotelyJokerSticker.Rental => item.IsRental,
                _ => true,
            };
            if (!has)
                return false;
        }

        return true;
    }

    /// <summary>A rare joker in any of <paramref name="slots"/> of the assembled shop, antes 1–4.</summary>
    private static Func<MotelySingleSearchContext, bool> ShopHasRare(int[] slots, params MotelyJokerSticker[] stickers) =>
        ctx =>
        {
            int maxSlot = slots.Max();
            for (int ante = 1; ante <= 4; ante++)
            {
                var shop = ctx.CreateShopItemStream(ante);
                for (int slot = 0; slot <= maxSlot; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shop);
                    if (slots.Contains(slot) && IsRareJoker(item, stickers))
                        return true;
                }
            }

            return false;
        };

    /// <summary>
    /// Ante 1 offers pack slots 0–3. Buying Hieroglyph or Petroglyph as ante 2's voucher sends
    /// the run back through ante 1's two shops, so slots 4–7 exist too. An ante-1 Hieroglyph or
    /// Petroglyph draws a bonus voucher before ante 2's is rolled.
    /// </summary>
    private static bool Ante1Extended(MotelySingleSearchContext ctx)
    {
        var state = new MotelyRunState();
        var ante1 = ctx.GetAnteFirstVoucher(1, state);
        state.ActivateVoucher(ante1);
        if (ante1 is MotelyVoucher.Hieroglyph or MotelyVoucher.Petroglyph)
        {
            var voucherStream = ctx.CreateVoucherStream(1);
            state.ActivateVoucher(ctx.GetNextVoucher(ref voucherStream, state));
        }

        return ctx.GetAnteFirstVoucher(2, state) is MotelyVoucher.Hieroglyph or MotelyVoucher.Petroglyph;
    }

    [Fact]
    public void GoldStake_WildcardRare_MatchesStreamWalk()
    {
        var expected = Expected(MotelyStake.Gold, ShopHasRare(DefaultShopSlots));
        Assert.NotEmpty(expected);
        Assert.Equal(
            expected,
            Run(
                """
                deck: Red
                stake: Gold
                must:
                  - rareJoker: []
                    antes: [1, 2, 3, 4]
                """
            )
        );
    }

    /// <summary>
    /// Eternal sticker on Gold stake: a strict subset of the unstickered wildcard — the sticker
    /// mask is a real gate, reading the eternal/perishable stream that exists from Black stake.
    /// </summary>
    [Fact]
    public void GoldStake_EternalSticker_GatesToSubset()
    {
        var all = Expected(MotelyStake.Gold, ShopHasRare(DefaultShopSlots));
        var expected = Expected(MotelyStake.Gold, ShopHasRare(DefaultShopSlots, MotelyJokerSticker.Eternal));
        Assert.NotEmpty(expected);
        Assert.True(expected.Length < all.Length && expected.All(all.Contains), "Eternal must gate to a strict subset");
        Assert.Equal(
            expected,
            Run(
                """
                deck: Red
                stake: Gold
                must:
                  - rareJoker: []
                    antes: [1, 2, 3, 4]
                    stickers: [Eternal]
                """
            )
        );
    }

    /// <summary>Perishable + Rental: every listed sticker must be present (rental stream is Gold-only).</summary>
    [Fact]
    public void GoldStake_PerishableAndRental_GatesToSubset()
    {
        var expected = Expected(
            MotelyStake.Gold,
            ShopHasRare(DefaultShopSlots, MotelyJokerSticker.Perishable, MotelyJokerSticker.Rental)
        );
        Assert.NotEmpty(expected);
        Assert.Equal(
            expected,
            Run(
                """
                deck: Red
                stake: Gold
                must:
                  - rareJoker: []
                    antes: [1, 2, 3, 4]
                    stickers: [Perishable, Rental]
                """
            )
        );
    }

    /// <summary>Sparse shop slots ({2, 5}): non-target slots are skipped while the stream still
    /// advances through every slot.</summary>
    [Fact]
    public void SparseShopSlots_MatchesStreamWalk()
    {
        var expected = Expected(MotelyStake.White, ShopHasRare([2, 5]));
        Assert.NotEmpty(expected);
        Assert.Equal(
            expected,
            Run(
                """
                deck: Red
                stake: White
                must:
                  - rareJoker: []
                    antes: [1, 2, 3, 4]
                    sources:
                      shopItems: [2, 5]
                """
            )
        );
    }

    /// <summary>
    /// Ante-1 buffoon packs with slots 0–5 requested: slots 4–5 only count on seeds whose run
    /// reaches the extended ante 1; Jumbo/Mega packs show four jokers, deduplicated.
    /// </summary>
    [Fact]
    public void Ante1PackSlots_WithExtension_MatchesStreamWalk()
    {
        var expected = Expected(
            MotelyStake.White,
            ctx =>
            {
                int maxSlot = Ante1Extended(ctx) ? 5 : MotelyGlobals.EarlyAnteMaxPackSlot;
                var packs = ctx.CreateBoosterPackStream(1);
                var jokers = ctx.CreateBuffoonPackJokerStream(1);
                for (int slot = 0; slot <= maxSlot; slot++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packs);
                    if (pack.GetPackType() != MotelyBoosterPackType.Buffoon)
                        continue;
                    var contents = ctx.GetNextBuffoonPackContents(ref jokers, pack.GetPackSize());
                    for (int i = 0; i < contents.Length; i++)
                        if (IsRareJoker(contents.GetItem(i), []))
                            return true;
                }

                return false;
            }
        );
        Assert.NotEmpty(expected);
        Assert.Equal(
            expected,
            Run(
                """
                deck: Red
                stake: White
                must:
                  - rareJoker: []
                    antes: [1]
                    sources:
                      boosterPacks: [0, 1, 2, 3, 4, 5]
                """
            )
        );
    }
}
