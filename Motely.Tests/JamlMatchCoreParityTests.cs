namespace Motely.Tests;

/// <summary>
/// Pins P0 single-match-core: FilterDesc scalar confirm arms and should-scoring share
/// <see cref="JamlScoring.ClauseMeetsMinForFilter"/> / CountRawOccurrences so they cannot drift.
/// </summary>
public sealed class JamlMatchCoreParityTests
{
    private static readonly string[] Seeds = ["ALEEB", "MOTELY77", "AAAAAAAA", "11111111"];

    private static HashSet<string> RunMustHits(IJamlClause clause)
    {
        var config = new JamlConfig
        {
            Id = "match-core-must",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);

        var hits = new HashSet<string>();
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => hits.Add(s));

        using var search = settings.Start();
        search.AwaitCompletion();
        return hits;
    }

    private static HashSet<string> RunShouldHits(IJamlClause clause)
    {
        clause.Score = 1;
        var config = new JamlConfig
        {
            Id = "match-core-should",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Should.Add(clause);

        var hits = new HashSet<string>();
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(r =>
            {
                if (r.Score > 0)
                    hits.Add(r.Seed);
            });

        using var search = settings.Start();
        search.AwaitCompletion();
        return hits;
    }

    [Fact]
    public void Boss_MustAndShould_SameHits()
    {
        var must = new BossClause
        {
            Bosses = [MotelyBossBlind.TheClub, MotelyBossBlind.TheGoad, MotelyBossBlind.TheWindow],
            Antes = [1, 2, 3],
            Min = 1,
        };
        var should = new BossClause
        {
            Bosses = [MotelyBossBlind.TheClub, MotelyBossBlind.TheGoad, MotelyBossBlind.TheWindow],
            Antes = [1, 2, 3],
            Min = 1,
            Score = 1,
        };

        Assert.Equal(RunMustHits(must), RunShouldHits(should));
    }

    [Fact]
    public void StartingDraw_MustAndShould_SameHits()
    {
        var must = new StartingDrawClause
        {
            Rank = MotelyStandardcardRank.Ace,
            Antes = [1],
            Min = 1,
        };
        var should = new StartingDrawClause
        {
            Rank = MotelyStandardcardRank.Ace,
            Antes = [1],
            Min = 1,
            Score = 1,
        };

        Assert.Equal(RunMustHits(must), RunShouldHits(should));
    }

    [Fact]
    public void StandardCard_MustAndShould_SameHits()
    {
        var must = new StandardCardClause
        {
            Rank = MotelyStandardcardRank.Ace,
            Antes = [1],
            Min = 1,
        };
        var should = new StandardCardClause
        {
            Rank = MotelyStandardcardRank.Ace,
            Antes = [1],
            Min = 1,
            Score = 1,
        };

        Assert.Equal(RunMustHits(must), RunShouldHits(should));
    }
}
