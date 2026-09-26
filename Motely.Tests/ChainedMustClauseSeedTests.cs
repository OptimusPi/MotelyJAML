using Motely.Filters.Jaml;
using Xunit;

namespace Motely.Tests;

// Regression coverage for the additional-filter batch path (Tacodiva/Motely#5 family).
//
// Two distinct bugs live here:
//  1. BatchSeeds' cached-pseudohash copy used a stray vector offset (fixed: source read is [lane]).
//  2. SearchFilterBatch did not Reset() filterBatch->SeedHashCache between flushes, so every SIMD
//     batch AFTER the first reused the previous batch's dynamically-cached pseudohashes
//     (CachePartialHash short-circuits when a key is already present). A vectorized filter (shop/
//     pack streams) then derived later seeds against stale hashes and silently dropped them —
//     deterministically "first 8 pass, rest dropped", and thread-count-dependent once concurrent.
//
// These tests force multiple SIMD batches (>8 seeds) through a vectorized joker filter and assert
// nothing is lost, plus the classic single-seed chained repro.
public sealed class ChainedMustClauseSeedTests
{
    // Explicit shop+pack sources: post shop-only defaults, multi-batch vector cache bugs only
    // fire when the filter actually walks pack streams (dynamic pseudohash keys per batch).
    private static JokerFilterDesc JokerDesc(MotelyJoker joker, int[] antes) =>
        new(
            new JokerClause
            {
                Jokers = [joker],
                Antes = antes,
                Min = 1,
                Sources = new JokerSourceConfig
                {
                    ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
                    BoosterPacks = [0, 1, 2, 3, 4, 5],
                },
            }
        );

    private static (long Matching, List<string> Matched) Run(
        IMotelySearchSettings settings,
        string[] seeds,
        int threads
    )
    {
        var matched = new List<string>();
        using var search = settings
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(threads)
            .WithQuietMode(true)
            // Match callbacks arrive concurrently at threads > 1; List<T>.Add is not thread-safe.
            .WithSeedMatchCallback(seed =>
            {
                lock (matched)
                    matched.Add(seed);
            })
            .Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, matched);
    }

    // 16 seeds that each individually carry Showman in antes 1-3 on Anaglyph/White. Two full SIMD
    // batches — the second batch is what the stale-cache bug dropped.
    private static readonly string[] ShowmanSeeds =
    [
        "1332JGL3", "15YUSRSA", "161TBL83", "179JDMBF", "17HGH7BG", "1A1V4OVA", "1FDAUI9I", "1HHSUP26",
        "1MZ7NUKL", "1QATUAZK", "1R2TE2Y5", "1SFMNC35", "1TQXZ6SI", "1VAFOD25", "1VR8E42O", "1WK7LIZF",
    ];

    // Passthrough base + Showman as an ADDITIONAL filter — the JAML/CLI shape. This is essential:
    // when the shop-using filter is the BASE, its pseudohash keys are pre-registered and never hit
    // the dynamic cache, so the stale-cache bug can't fire. Behind a passthrough it caches keys
    // dynamically per batch, which is exactly what must be Reset() between batches.
    private static IMotelySearchSettings ShowmanSettings() =>
        new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithAdditionalFilter(JokerDesc(MotelyJoker.Showman, [1, 2, 3]))
            .WithDeck(MotelyDeck.Anaglyph)
            .WithStake(MotelyStake.White);

    [Fact]
    public void MultiBatch_SingleFilter_KeepsEveryBatch()
    {
        // Pre-fix: only the first batch of 8 survived (the stale-cache drop). Must be all 16.
        var (matching, matched) = Run(ShowmanSettings(), ShowmanSeeds, threads: 1);
        Assert.Equal(ShowmanSeeds.Length, matched.Count);
        Assert.Equal((long)ShowmanSeeds.Length, matching);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void MultiBatch_SingleFilter_ThreadInvariant(int threads)
    {
        // The matched set must be complete and identical regardless of thread count.
        var (matching, matched) = Run(ShowmanSettings(), ShowmanSeeds, threads);
        Assert.Equal(ShowmanSeeds.Length, matched.Count);
        Assert.Equal((long)ShowmanSeeds.Length, matching);
    }

    [Fact]
    public void ChainedMustClauses_SingleSeed_C7AOGOYY_ShouldMatch()
    {
        // Baron (base) AND Mime (chained additional) both present in antes 1-4 on Ghost/Black.
        var settings = new MotelySearchSettings<JokerFilterDesc.JokerFilter>(
            JokerDesc(MotelyJoker.Baron, [1, 2, 3, 4])
        )
            .WithAdditionalFilter(JokerDesc(MotelyJoker.Mime, [1, 2, 3, 4]))
            .WithDeck(MotelyDeck.Ghost)
            .WithStake(MotelyStake.Black);

        var (matching, _) = Run(settings, ["C7AOGOYY"], threads: 1);
        Assert.Equal(1L, matching);
    }
}
