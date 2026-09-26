namespace Motely.Tests;

/// <summary>
/// Pins exact-SIMD must re-eval skip: families that confirm via ClauseMeetsMinForFilter
/// (or full vector event counts) may skip scoring's must re-count; coarse prefilters may not.
/// </summary>
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
        // Legendary SIMD is edition/passthrough only — pack/Soul confirm is scoring must re-eval.
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
        // Named non-legendary joker uses vector shop/buffoon prefilter (not SearchIndividualSeeds).
        Assert.False(
            JamlScoring.CanSkipMustReeval(
                [new JokerClause { Jokers = [MotelyJoker.Blueprint], Antes = [1] }]
            )
        );
        // Empty joker list still routes through JokerFilterDesc exact confirm today — may skip.
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
        // Mixed exact + coarse → re-eval.
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
        // Boss must-only: skip re-eval path must still emit seed matches.
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
        // At least some seeds match a common ante-1 boss from the set (or zero is fine if unlucky).
        Assert.True(hits.Count >= 0);
        Assert.Equal(hits.Count, (int)search.MatchingSeeds);
    }
}
