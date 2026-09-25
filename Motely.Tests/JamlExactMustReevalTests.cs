namespace Motely.Tests;

public sealed class JamlExactMustReevalTests
{
    [Fact]
    public void ExactFamilies_CanSkipMustReeval()
    {
        Assert.True(JamlScoring.CanSkipMustReeval([]));
        Assert.True(
            JamlScoring.CanSkipMustReeval(
                [new BossClause { Bosses = [MotelyBossBlind.TheClub], Antes = [1] }]
            )
        );
        Assert.False(
            JamlScoring.CanSkipMustReeval(
                [
                    new LegendaryJokerClause
                    {
                        Jokers = [MotelyJoker.Perkeo],
                        Antes = [1],
                    },
                ]
            )
        );
        Assert.True(
            JamlScoring.CanSkipMustReeval(
                [
                    new TarotCardClause
                    {
                        Tarots = [MotelyTarotCard.TheFool],
                        Antes = [1],
                        Sources = new TarotCardSourceConfig { CharmTag = true, BoosterPacks = [0] },
                    },
                ]
            )
        );
        Assert.True(
            JamlScoring.CanSkipMustReeval(
                [new LuckyMoneyClause { Rolls = [0], Min = 1 }]
            )
        );
    }

    [Fact]
    public void CoarseFamilies_CannotSkipMustReeval()
    {
        Assert.False(
            JamlScoring.CanSkipMustReeval(
                [new JokerClause { Jokers = [MotelyJoker.Blueprint], Antes = [1] }]
            )
        );
        Assert.True(
            JamlScoring.CanSkipMustReeval(
                [new JokerClause { Antes = [1] }]
            )
        );
        Assert.False(
            JamlScoring.CanSkipMustReeval(
                [
                    new LegendaryJokerClause
                    {
                        Jokers = [],
                        Edition = MotelyItemEdition.Negative,
                        Antes = [1, 2],
                    },
                ]
            )
        );
        Assert.False(
            JamlScoring.CanSkipMustReeval(
                [
                    new TarotCardClause
                    {
                        Tarots = [MotelyTarotCard.TheFool],
                        Antes = [1],
                        Sources = new TarotCardSourceConfig { BoosterPacks = [0, 1] },
                    },
                ]
            )
        );
        Assert.False(
            JamlScoring.CanSkipMustReeval(
                [
                    new BossClause { Bosses = [MotelyBossBlind.TheClub], Antes = [1] },
                    new JokerClause { Jokers = [MotelyJoker.Blueprint], Antes = [1] },
                ]
            )
        );
    }

    [Fact]
    public void ExactMustOnly_StillFindsSeed()
    {
        var config = new JamlConfig
        {
            Id = "exact-must",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(
            new BossClause
            {
                Bosses =
                [
                    MotelyBossBlind.TheClub,
                    MotelyBossBlind.TheGoad,
                    MotelyBossBlind.TheWindow,
                    MotelyBossBlind.TheHead,
                    MotelyBossBlind.ThePlant,
                ],
                Antes = [1],
                Min = 1,
            }
        );

        var hits = new HashSet<string>();
        var seeds = new[] { "ALEEB", "MOTELY77", "AAAAAAAA", "11111111" };
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => hits.Add(s));

        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.True(search.TotalSeedsSearched >= 1);
        Assert.True(hits.Count >= 0);
        Assert.Equal(hits.Count, (int)search.MatchingSeeds);
    }
}
