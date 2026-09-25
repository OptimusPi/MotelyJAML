using Motely.Filters.Jaml;
using Xunit;

namespace Motely.Tests;

public sealed class ChainedMustClauseSeedTests
{
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
            .WithSeedMatchCallback(seed =>
            {
                lock (matched)
                    matched.Add(seed);
            })
            .Start();
        search.AwaitCompletion();
        return (search.MatchingSeeds, matched);
    }

    private static readonly string[] ShowmanSeeds =
    [
        "1332JGL3", "15YUSRSA", "161TBL83", "179JDMBF", "17HGH7BG", "1A1V4OVA", "1FDAUI9I", "1HHSUP26",
        "1MZ7NUKL", "1QATUAZK", "1R2TE2Y5", "1SFMNC35", "1TQXZ6SI", "1VAFOD25", "1VR8E42O", "1WK7LIZF",
    ];

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
        var (matching, matched) = Run(ShowmanSettings(), ShowmanSeeds, threads);
        Assert.Equal(ShowmanSeeds.Length, matched.Count);
        Assert.Equal((long)ShowmanSeeds.Length, matching);
    }

    [Fact]
    public void ChainedMustClauses_SingleSeed_C7AOGOYY_ShouldMatch()
    {
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
