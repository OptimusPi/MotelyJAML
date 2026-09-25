namespace Motely.Tests;

public sealed class PokerHandRollAdvanceTests
{
    private static readonly string[] Seeds = ["12345678", "UNITTEST", "1AAAAAAA", "ALEEBOOO"];

    private static MotelyItem[] StartingHand(
        MotelySingleSearchContext ctx,
        int ante,
        int advance
    )
    {
        var deck = new MotelyItem[MotelyEnum<MotelyStandardCard>.ValueCount];
        for (int i = 0; i < deck.Length; i++)
            deck[i] = new(MotelyEnum<MotelyStandardCard>.Values[i]);

        ctx.Shuffle(MotelyPokerHandEval.ShuffleKeyForAnte(ante), deck, advance);

        int handSize = Math.Min(8, deck.Length);
        return deck.AsSpan(deck.Length - handSize, handSize).ToArray();
    }

    private static MotelyItem[] StartingHandLegacy(MotelySingleSearchContext ctx, int ante)
    {
        var deck = new MotelyItem[MotelyEnum<MotelyStandardCard>.ValueCount];
        for (int i = 0; i < deck.Length; i++)
            deck[i] = new(MotelyEnum<MotelyStandardCard>.Values[i]);

        ctx.Shuffle(MotelyPokerHandEval.ShuffleKeyForAnte(ante), deck);

        int handSize = Math.Min(8, deck.Length);
        return deck.AsSpan(deck.Length - handSize, handSize).ToArray();
    }

    private static void ForEachSeed(Action<MotelySingleSearchContext> body)
    {
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(ctx =>
            {
                body(ctx);
                return 1;
            });

        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.Equal((long)Seeds.Length, search.MatchingSeeds);
    }

    [Fact]
    public void AdvanceZero_IsIdenticalToTheNoAdvanceCall()
    {
        int checkedSeeds = 0;

        ForEachSeed(ctx =>
        {
            for (int ante = 1; ante <= 3; ante++)
            {
                Assert.Equal(StartingHandLegacy(ctx, ante), StartingHand(ctx, ante, 0));
                checkedSeeds++;
            }
        });

        Assert.Equal(Seeds.Length * 3, checkedSeeds);
    }

    [Fact]
    public void EachBlindOfAnAnte_DrawsADifferentHandFromTheSameAnteStream()
    {
        ForEachSeed(ctx =>
        {
            for (int ante = 1; ante <= 3; ante++)
            {
                var small = StartingHand(ctx, ante, 0);
                var big = StartingHand(ctx, ante, 1);
                var boss = StartingHand(ctx, ante, 2);

                Assert.NotEqual(small, big);
                Assert.NotEqual(big, boss);
                Assert.NotEqual(small, boss);
            }
        });
    }

    [Fact]
    public void AdvanceIsCumulativeAlongOneStream()
    {
        ForEachSeed(ctx =>
        {
            var viaShuffle = StartingHand(ctx, 2, 2);

            var deck = new MotelyItem[MotelyEnum<MotelyStandardCard>.ValueCount];
            for (int i = 0; i < deck.Length; i++)
                deck[i] = new(MotelyEnum<MotelyStandardCard>.Values[i]);

            var stream = ctx.CreatePrngStream(MotelyPokerHandEval.ShuffleKeyForAnte(2));
            ctx.GetNextPrngState(ref stream);
            ctx.GetNextPrngState(ref stream);
            var random = ctx.GetNextLuaRandom(ref stream);
            for (int i = deck.Length - 1; i > 0; i--)
            {
                int j = random.RandInt(0, i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            int handSize = Math.Min(8, deck.Length);
            Assert.Equal(viaShuffle, deck.AsSpan(deck.Length - handSize, handSize).ToArray());
        });
    }

    [Fact]
    public void AnteReduction_ExtendsTheSameStreamPastOnePassOfBlinds()
    {
        Assert.Equal(9, PokerHandFilterDesc.MaxBlindsPerAnte);
        Assert.Equal(3, PokerHandFilterDesc.BlindsPerAntePass);

        ForEachSeed(ctx =>
        {
            var seen = new List<MotelyItem[]>();
            for (int advance = 0; advance < PokerHandFilterDesc.MaxBlindsPerAnte; advance++)
                seen.Add(StartingHand(ctx, 2, advance));

            for (int i = 0; i < seen.Count; i++)
                for (int j = i + 1; j < seen.Count; j++)
                    Assert.NotEqual(seen[i], seen[j]);
        });
    }

    [Fact]
    public void AnteOneAndAnteZero_ShareTheNr1Key()
    {
        Assert.Equal("nr1", MotelyPokerHandEval.ShuffleKeyForAnte(1));
        Assert.Equal("nr1", MotelyPokerHandEval.ShuffleKeyForAnte(0));
        Assert.Equal("nr2", MotelyPokerHandEval.ShuffleKeyForAnte(2));
    }
}
