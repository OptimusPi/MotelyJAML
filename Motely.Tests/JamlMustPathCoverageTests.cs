namespace Motely.Tests;

/// <summary>
/// Smoke: SIMD Must paths (named targets, editions, source combos) through the real search
/// pipeline. Asserts the list batch ran. Does not pin MatchingSeeds — find-proof is golden tests.
/// </summary>
public sealed class JamlMustPathCoverageTests
{
    private static readonly string[] Seeds = ["ALEEB", "MOTELY77", "UNITTEST"];

    private static void RunMust(IJamlClause clause, MotelyDeck deck = MotelyDeck.Red)
    {
        var config = new JamlConfig
        {
            Id = "must-cov",
            Deck = deck,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);

        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(Seeds, Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true);

        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.True(search.TotalSeedsSearched >= 1, "Must path must run the filter over the list batch");
    }

    [Fact]
    public void UncommonJoker_NamedTargets_EachSourceCombination()
    {
        RunMust(
                new UncommonJokerClause
                {
                    Jokers = [MotelyJokerUncommon.Fibonacci, MotelyJokerUncommon.Hack],
                    Antes = [1, 2],
                    Sources = new JokerSourceConfig { ShopItems = [0, 1, 2, 3] },
                }
            );
        RunMust(
                new UncommonJokerClause
                {
                    Jokers = [MotelyJokerUncommon.Mime],
                    Antes = [1, 2],
                    Sources = new JokerSourceConfig { BoosterPacks = [0, 1] },
                }
            );
        RunMust(
                new UncommonJokerClause
                {
                    Jokers = [MotelyJokerUncommon.Blackboard],
                    Antes = [1],
                    Sources = new JokerSourceConfig { UncommonShopJokers = [0, 1] },
                }
            );
    }

    [Fact]
    public void UncommonJoker_WithEdition_FiltersEditionBranch()
    {
        RunMust(
                new UncommonJokerClause
                {
                    Edition = MotelyItemEdition.Foil,
                    Antes = [1, 2],
                }
            );
    }

    [Fact]
    public void RareJoker_NamedAndEdition()
    {
        RunMust(new RareJokerClause { Jokers = [MotelyJokerRare.Blueprint], Antes = [1, 2] });
        RunMust(
                new RareJokerClause
                {
                    Edition = MotelyItemEdition.Negative,
                    Antes = [1],
                }
            );
    }

    [Fact]
    public void CommonJoker_NamedAndEdition()
    {
        RunMust(new CommonJokerClause { Jokers = [MotelyJokerCommon.Joker], Antes = [1, 2] });
        RunMust(
                new CommonJokerClause
                {
                    Edition = MotelyItemEdition.Holographic,
                    Antes = [1],
                }
            );
    }

    [Fact]
    public void AnyRarityJoker_NamedWithEdition()
    {
        RunMust(
                new JokerClause
                {
                    Jokers = [MotelyJoker.Blueprint, MotelyJoker.Brainstorm],
                    Antes = [1, 2, 3],
                }
            );
        RunMust(
                new JokerClause
                {
                    Edition = MotelyItemEdition.Polychrome,
                    Antes = [1, 2],
                }
            );
    }

    [Fact]
    public void Voucher_MultiRollMultiAnteAndMultiTarget()
    {
        RunMust(
                new VoucherClause
                {
                    Vouchers = [MotelyVoucher.Overstock, MotelyVoucher.Grabber, MotelyVoucher.Hone],
                    Rolls = [0, 1, 2],
                    Antes = [1, 2],
                }
            );
        RunMust(
                new VoucherClause
                {
                    Vouchers = [MotelyVoucher.MagicTrick],
                    Rolls = [0],
                    Antes = [1, 2, 3, 4],
                }
            );
    }

    [Fact]
    public void Tag_MultiRollBothBlinds()
    {
        RunMust(
                new TagClause
                {
                    Tags = [MotelyTag.SpeedTag, MotelyTag.CharmTag],
                    Rolls = [0, 1, 2],
                    Antes = [1, 2],
                }
            );
    }

    [Fact]
    public void Legendary_EachVariant()
    {
        // Named legendary, no edition: the plain Soul-match path across an ante range.
        RunMust(
                new LegendaryJokerClause { Jokers = [MotelyJoker.Canio], Antes = [1, 2, 3] }
            );
        // Wildcard legendary from spectral packs only.
        RunMust(
                new LegendaryJokerClause
                {
                    Antes = [1, 2],
                    Sources = new LegendaryJokerSourceConfig { SpectralPacks = [0, 1] },
                }
            );
        // Min > 1 forces the matcher to keep counting after the first hit.
        RunMust(
                new LegendaryJokerClause { Antes = [1, 2, 3, 4], Min = 2 }
            );
    }

    [Fact]
    public void Boss_MultiAnte()
    {
        RunMust(
                new BossClause
                {
                    Bosses = [MotelyBossBlind.TheHook, MotelyBossBlind.TheWall],
                    Antes = [1, 2, 3],
                }
            );
    }

    [Fact]
    public void GhostDeck_SpectralInShop_MustPath()
    {
        RunMust(
                new SpectralCardClause
                {
                    Spectrals = [MotelySpectralCard.Familiar],
                    Antes = [1, 2],
                    Sources = new SpectralCardSourceConfig { ShopItems = [0, 1, 2, 3] },
                },
                MotelyDeck.Ghost
            );
    }

    [Fact]
    public void Tarot_EmperorAndPurpleSeal_MustPath()
    {
        RunMust(
                new TarotCardClause
                {
                    Tarots = [MotelyTarotCard.Death],
                    Antes = [1, 2],
                    Sources = new TarotCardSourceConfig
                    {
                        Emperor = [0, 1],
                        PurpleSealOrEightBall = [0, 1],
                        CharmTag = true,
                        BoosterPacks = [0, 1],
                    },
                }
            );
    }
}
