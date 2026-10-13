using Motely.Filters;

namespace Motely.Tests;

/// <summary>
/// The loader refuses what the engine cannot run, and says where. Each case is one rule of
/// <see cref="JamlValidator"/>; the message names the clause by its place in the document.
/// </summary>
public sealed class JamlValidatorTests
{
    private static string Error(string yaml)
    {
        Assert.False(YamlConfigLoader.TryLoad(yaml, out _, out var error), "the filter loaded");
        return error!;
    }

    private static JamlConfig Load(string yaml)
    {
        Assert.True(YamlConfigLoader.TryLoad(yaml, out var config, out var error), error);
        return config!;
    }

    [Theory]
    [InlineData("must:\n  - joker: Blueprint\n    min: 0\n", "must[0] (joker): min 0")]
    [InlineData("must:\n  - joker: Blueprint\n    min: 3\n    max: 2\n", "max 2 is below min 3")]
    [InlineData("should:\n  - joker: Blueprint\n    label: 'a, b'\n", "should[0] (joker): label")]
    [InlineData("must:\n  - joker: Blueprint\n    antes: []\n", "antes cannot be empty")]
    [InlineData("must:\n  - joker: Blueprint\n    antes: [40]\n", "antes 40 is past the last ante, 39")]
    [InlineData("must:\n  - joker: Blueprint\n    antes: [1, 1]\n", "antes lists an index twice")]
    [InlineData("must:\n  - luckyMoney: []\n", "must[0] (luckyMoney): rolls cannot be empty")]
    [InlineData("must:\n  - luckyMoney: [-1]\n", "rolls -1 is negative")]
    [InlineData("must:\n  - voucher: Telescope\n    rolls: [100000000]\n", "rolls 100000000 is past the last roll, 999")]
    [InlineData("must:\n  - tag: NegativeTag\n    rolls: [1000]\n", "rolls 1000 is past the last roll, 999")]
    [InlineData("must:\n  - or: []\n", "must[0] (or): needs at least one clause")]
    [InlineData("must:\n  - or:\n      - joker: Blueprint\n    min: 2\n", "min 2 is more arms than its 1")]
    [InlineData("must:\n  - misprintMult: [0]\n    mult: 24\n", "mult 24")]
    [InlineData("must:\n  - standardCard: AceOfSpades\n    rank: King\n", "AceOfSpades has rank Ace, not King")]
    public void A_clause_the_engine_cannot_run_is_refused_by_name(string yaml, string expected)
    {
        Assert.Contains(expected, Error(yaml));
    }

    [Fact]
    public void Every_failure_is_reported_at_once()
    {
        var error = Error("must:\n  - joker: Blueprint\n    min: 0\n  - or:\n      - tag: NegativeTag\n        rolls: []\n");
        Assert.Contains("must[0] (joker)", error);
        Assert.Contains("must[1] (or)[0] (tag): rolls cannot be empty", error);
    }

    [Fact]
    public void Too_many_should_clauses_for_one_tally_are_refused()
    {
        var should = string.Concat(Enumerable.Repeat("  - joker: Blueprint\n", MotelyScoredSeedResult.MAX_TALLY_COUNT + 1));
        Assert.Contains("a seed's tally holds at most", Error("should:\n" + should));
    }

    [Fact]
    public void A_whole_card_fills_its_rank_and_suit()
    {
        var config = Load("must:\n  - standardCard: KingOfHearts\n    seal: Red\n");
        var clause = Assert.IsType<StandardCardClause>(config.Must[0]);
        Assert.Equal(MotelyStandardcardRank.King, clause.Rank);
        Assert.Equal(MotelyStandardcardSuit.Hearts, clause.Suit);

        var draw = Assert.IsType<StartingDrawClause>(Load("must:\n  - startingDraw: TwoOfClubs\n").Must[0]);
        Assert.Equal((MotelyStandardcardRank.Two, MotelyStandardcardSuit.Clubs), (draw.Rank, draw.Suit));
    }

    [Fact]
    public void Any_is_the_whole_category_for_single_valued_keys_too()
    {
        var clause = Assert.IsType<StandardCardClause>(Load("must:\n  - standardCard: any\n    seal: Gold\n").Must[0]);
        Assert.Null(clause.Card);
        Assert.Null(clause.Rank);
        Assert.Equal(MotelyItemSeal.Gold, clause.Seal);
    }

    [Fact]
    public void Endless_antes_load()
    {
        var clause = Assert.IsType<JokerClause>(Load("should:\n  - joker: DietCola\n    antes: [10, 11, 12]\n").Should[0]);
        Assert.Equal([10, 11, 12], clause.Antes);
    }

    [Fact]
    public void A_config_edited_after_loading_is_checked_again_before_it_runs()
    {
        // A host (the Wasm JS side, or code) can change a loaded config; the filters size stack
        // buffers from rolls, so CreateSettings validates rather than trusting the loader.
        var config = Load("must:\n  - voucher: Telescope\n");
        ((VoucherClause)config.Must[0]).Rolls = [100_000_000];
        var ex = Assert.Throws<InvalidOperationException>(() => MotelySearchBuilder.CreateSettings(config));
        Assert.Contains("rolls 100000000 is past the last roll", ex.Message);
    }
}
