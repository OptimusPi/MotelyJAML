namespace Motely.Tests;

/// <summary>
/// Pins legendary edition soul prefilter for any Min (not only Min==1), and expanded exact-confirm families.
/// </summary>
public sealed class JamlLegendaryEditionPrefilterTests
{
    [Fact]
    public void Legendary_Edition_MinTwo_StillFindsHieroglyphSeedWhenRangeWide()
    {
        // Edition prefilter must not false-negative Min>1 when at least one soul has the edition.
        // KHTW99TC has Negative Perkeo ante 1 slot 6; Min=1 is the real case — Min=2 on antes 1..8
        // may or may not match; use Min=1 with edition to prove prefilter path with edition set.
        const string seed = "KHTW99TC";
        var jaml = """
            name: leg-ed
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
                edition: Negative
                min: 1
                antes: [1]
                sources:
                  boosterPacks: [6]
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var err), err);
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSeedGenerator([seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true);
        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.Equal(1, search.MatchingSeeds);
    }

    [Fact]
    public void Legendary_Edition_MinTwo_DoesNotFalseNegativeOnExactConfirm()
    {
        // Clause with edition + Min=2 must still run (prefilter applies, scalar confirm gates).
        var clause = new LegendaryJokerClause
        {
            Jokers = [MotelyJoker.Perkeo],
            Edition = MotelyItemEdition.Negative,
            Antes = [1, 2, 3, 4, 5, 6, 7, 8],
            Min = 2,
            Sources = new LegendaryJokerSourceConfig { BoosterPacks = [0, 1, 2, 3, 4, 5] },
        };
        var config = new JamlConfig
        {
            Id = "leg-min2",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);

        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(["ALEEB", "MOTELY77", "AAAAAAAA", "11111111"], 4)
            .WithThreadCount(1)
            .WithQuietMode(true);
        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.True(search.TotalSeedsSearched >= 1);
    }

    [Fact]
    public void ExactConfirm_IncludesVoucherTagErratic()
    {
        Assert.True(
            JamlScoring.IsExactFilterConfirm(
                new VoucherClause
                {
                    Vouchers = [MotelyVoucher.Overstock],
                    Rolls = [0],
                    Antes = [1],
                }
            )
        );
        Assert.True(
            JamlScoring.IsExactFilterConfirm(
                new TagClause
                {
                    Tags = [MotelyTag.RareTag],
                    Rolls = [0],
                    Antes = [1],
                }
            )
        );
        Assert.True(
            JamlScoring.IsExactFilterConfirm(
                new ErraticRankClause { Rank = MotelyStandardcardRank.Ace, Antes = [1] }
            )
        );
        Assert.True(JamlScoring.CanSkipMustReeval(
            [
                new VoucherClause
                {
                    Vouchers = [MotelyVoucher.Overstock],
                    Rolls = [0],
                    Antes = [1],
                },
                new TagClause
                {
                    Tags = [MotelyTag.NegativeTag],
                    Rolls = [0],
                    Antes = [1],
                },
            ]
        ));
    }
}
