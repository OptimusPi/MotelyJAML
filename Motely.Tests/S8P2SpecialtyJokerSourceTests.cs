using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// S8.P2 — scalar specialty joker sources (judgement / wraith / riffRaff / rareTag /
/// uncommonTag / rarity shop streams) through real should-clause scoring.
///
/// The expected tallies are derived here from the raw streams, not pinned from an earlier
/// engine run: a pinned number only proves the engine agrees with itself, and that is how a
/// rarity-rolled Wraith survived. The rarity laws come from the game's <c>create_card</c> calls:
/// Wraith <c>0.99</c> → Rare, Rare Tag <c>1</c> → Rare, Uncommon Tag <c>0.9</c> → Uncommon,
/// Riff-Raff <c>0</c> → Common.
/// </summary>
public sealed class S8P2SpecialtyJokerSourceTests
{
    private static readonly string[] WideSeeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF",
    ];

    private static readonly int[] Antes = [1, 2];
    private const int Rolls = 2;

    private const string SpecialtyJaml = """
        name: s8p2-specialty
        deck: Red
        stake: White
        must:
          - joker: []
            antes: [1]
        should:
          - joker: []
            antes: [1, 2]
            sources:
              judgement: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              wraith: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              riffRaff: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              rareTag: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              uncommonTag: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              commonShopJokers: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              uncommonShopJokers: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              rareShopJokers: [0, 1]
          - joker: []
            antes: [1, 2]
            sources:
              allShopJokers: [0, 1]
          - joker: Joker
            antes: [1, 2]
            sources:
              judgement: [0, 1]
              wraith: [0, 1]
              riffRaff: [0, 1]
          - joker: Joker
            antes: [1, 2]
            sources:
              rareTag: [0, 1]
              uncommonTag: [0, 1]
          - joker: Joker
            antes: [1, 2]
            sources:
              commonShopJokers: [0, 1]
              uncommonShopJokers: [0, 1]
              rareShopJokers: [0, 1]
              allShopJokers: [0, 1]
        """;

    private static SortedDictionary<string, int> RunScores()
    {
        var config = ProofSearch.LoadOrThrow(SpecialtyJaml);
        var scores = new SortedDictionary<string, int>(StringComparer.Ordinal);
        using var search = MotelySearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(WideSeeds, WideSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(r => scores[r.Seed] = r.Score)
            .Start();
        search.AwaitCompletion();
        return scores;
    }

    private static void ForEachSeed(Action<MotelySingleSearchContext> body)
    {
        using var search = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(WideSeeds, WideSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(ctx =>
            {
                body(ctx);
                return 1;
            })
            .Start();
        search.AwaitCompletion();
        Assert.Equal((long)WideSeeds.Length, search.MatchingSeeds);
    }

    /// <summary>The first <see cref="Rolls"/> pulls of every specialty family, per ante, straight off the streams.</summary>
    private static Dictionary<string, List<MotelyItem>> Pulls(MotelySingleSearchContext ctx)
    {
        var pulls = new Dictionary<string, List<MotelyItem>>();
        void Add(string family, MotelyItem item)
        {
            if (!pulls.TryGetValue(family, out var list))
                pulls[family] = list = [];
            list.Add(item);
        }

        foreach (int ante in Antes)
        {
            var judgement = ctx.CreateJudgementJokerStream(ante);
            var wraith = ctx.CreateWraithJokerStream(ante);
            var riffRaff = ctx.CreateRiffRaffJokerStream(ante);
            var rareTag = ctx.CreateRareTagJokerStream(ante);
            var uncommonTag = ctx.CreateUncommonTagJokerStream(ante);
            var commonShop = ctx.CreateCommonShopJokerStream(ante);
            var uncommonShop = ctx.CreateUncommonShopJokerStream(ante);
            var rareShop = ctx.CreateRareShopJokerStream(ante);
            var allShop = ctx.CreateShopJokerStream(ante);
            for (int roll = 0; roll < Rolls; roll++)
            {
                Add("judgement", ctx.GetNextJoker(ref judgement));
                Add("wraith", ctx.GetNextJoker(ref wraith));
                Add("riffRaff", ctx.GetNextJoker(ref riffRaff));
                Add("rareTag", ctx.GetNextJoker(ref rareTag));
                Add("uncommonTag", ctx.GetNextJoker(ref uncommonTag));
                Add("commonShop", ctx.GetNextJoker(ref commonShop));
                Add("uncommonShop", ctx.GetNextJoker(ref uncommonShop));
                Add("rareShop", ctx.GetNextJoker(ref rareShop));
                Add("allShop", ctx.GetNextJoker(ref allShop));
            }
        }

        return pulls;
    }

    private static int CountType(Dictionary<string, List<MotelyItem>> pulls, MotelyItemType type, params string[] families) =>
        families.Sum(f => pulls[f].Count(i => i.Type == type));

    private static MotelyJokerRarity RarityOf(MotelyItem item) =>
        (MotelyJokerRarity)((int)item.Type & MotelyGlobals.JokerRarityMask);

    /// <summary>
    /// Each wildcard clause names only a specialty stream, so it counts exactly the jokers that
    /// stream hands out (every pull is a joker) — and no Souls, because its <c>sources:</c> block
    /// names no booster packs. Each <c>joker: Joker</c> clause counts the pulls that are Joker.
    /// </summary>
    [Fact]
    public void SpecialtySources_ScoreEqualsStreamDerivedTally()
    {
        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal);
        ForEachSeed(ctx =>
        {
            var pulls = Pulls(ctx);
            int wildcard = pulls.Values.Sum(list => list.Count(i => i.TypeCategory == MotelyItemTypeCategory.Joker));
            int named =
                CountType(pulls, MotelyItemType.Joker, "judgement", "wraith", "riffRaff")
                + CountType(pulls, MotelyItemType.Joker, "rareTag", "uncommonTag")
                + CountType(pulls, MotelyItemType.Joker, "commonShop", "uncommonShop", "rareShop", "allShop");
            lock (expected)
                expected[ctx.GetSeed()] = wildcard + named;
        });

        Assert.Equal(WideSeeds.Length, expected.Count);
        Assert.All(expected.Values, v => Assert.True(v >= 9 * Antes.Length * Rolls));
        Assert.Equal(expected, RunScores());
    }

    [Fact]
    public void FixedRaritySpawns_FollowTheGameRarity()
    {
        var violations = new List<string>();
        ForEachSeed(ctx =>
        {
            var pulls = Pulls(ctx);
            void Expect(string family, MotelyJokerRarity rarity)
            {
                foreach (var item in pulls[family])
                    if (RarityOf(item) != rarity)
                        lock (violations)
                            violations.Add($"{ctx.GetSeed()} {family}: {item.Type} is {RarityOf(item)}, expected {rarity}");
            }

            Expect("wraith", MotelyJokerRarity.Rare);
            Expect("rareTag", MotelyJokerRarity.Rare);
            Expect("uncommonTag", MotelyJokerRarity.Uncommon);
            Expect("riffRaff", MotelyJokerRarity.Common);
            Expect("commonShop", MotelyJokerRarity.Common);
            Expect("uncommonShop", MotelyJokerRarity.Uncommon);
            Expect("rareShop", MotelyJokerRarity.Rare);
        });

        Assert.Empty(violations);
    }
}
