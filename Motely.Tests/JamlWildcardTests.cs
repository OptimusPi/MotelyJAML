using Motely.Filters.Jaml;
using Xunit;

namespace Motely.Tests;

/// <summary>
/// Category wildcards are <b>empty discriminator lists</b> (and optional props), not a token
/// <c>Any</c>. Same shape as <c>standardCard:</c> with no rank/suit — props only, or nothing.
/// </summary>
public sealed class JamlWildcardTests
{
    private static string Block(string disc, string extra = "antes: [1]\n    score: 1") =>
        $"name: wildcard-probe\ndeck: Red\nstake: White\nshould:\n  - {disc}: []\n    {extra}\n";

    [Fact]
    public void EmptyJokerList_IsCategoryAny()
    {
        Assert.True(JamlConfigLoader.TryLoad(Block("joker"), out var config, out var error), error);
        var clause = Assert.IsType<JokerClause>(config!.Should[0]);
        Assert.Empty(clause.Jokers);
        Assert.Null(clause.Sources);
        Assert.Equal([1], clause.Antes);
    }

    [Fact]
    public void BareJokerKey_IsCategoryAny()
    {
        const string jaml = """
            name: bare-any
            deck: Red
            stake: White
            must:
              - joker:
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        Assert.Empty(Assert.IsType<JokerClause>(config!.Must[0]).Jokers);
    }

    [Fact]
    public void EmptyJoker_WithEditionOnly_IsAnyNegativeJoker()
    {
        var jaml = """
            name: edition-any
            deck: Red
            stake: White
            must:
              - joker: []
                edition: Negative
                antes: [1]
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        var clause = Assert.IsType<JokerClause>(config!.Must[0]);
        Assert.Empty(clause.Jokers);
        Assert.Equal(MotelyItemEdition.Negative, clause.Edition);
    }

    [Fact]
    public void TokenAny_IsCategoryAny()
    {
        var jaml = """
            name: token-any
            deck: Red
            stake: White
            must:
              - joker: Any
                antes: [1]
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        Assert.Empty(Assert.IsType<JokerClause>(config!.Must[0]).Jokers);
    }

    [Fact]
    public void AliasSyntaxIsRejected()
    {
        Assert.False(
            JamlConfigLoader.TryLoad(Block("joker").Replace("joker: []", "joker: *any*"), out _, out var error)
        );
        Assert.Contains("*any*", error);
    }

    [Fact]
    public void EmptyTarotList_IsCategoryAny_WithNamedAntes_SourcesNull()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(Block("tarotCard", "antes: [4, 5]\n    score: 1"), out var config, out var error),
            error
        );
        var clause = Assert.IsType<TarotCardClause>(config!.Should[0]);
        Assert.Empty(clause.Tarots);
        Assert.Null(clause.Sources);
        Assert.Equal([4, 5], clause.Antes);
    }

    [Fact]
    public void EmptySpectralList_IsCategoryAny_NotSoulSpecialPath()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(Block("spectralCard"), out var config, out var error),
            error
        );
        var clause = Assert.IsType<SpectralCardClause>(config!.Should[0]);
        Assert.Empty(clause.Spectrals);
        Assert.Null(clause.Sources);
        Assert.False(SpecialSpectralCardFilterDesc.Handles(clause));
        Assert.IsType<SpectralCardFilterDesc>(JamlSearchBuilder.ClauseToFilterDesc(clause));
    }

    [Fact]
    public void EmptyPlanetList_IsCategoryAny()
    {
        Assert.True(
            JamlConfigLoader.TryLoad(Block("planetCard"), out var config, out var error),
            error
        );
        var clause = Assert.IsType<PlanetCardClause>(config!.Should[0]);
        Assert.Empty(clause.Planets);
        Assert.Null(clause.Sources);
    }

    [Fact]
    public void WithLuck_UnderTarot_FailsLoad()
    {
        var jaml = """
            name: with-on-tarot
            deck: Red
            stake: White
            must:
              - tarotCard: []
                with:
                  luck: 2
            """;
        Assert.False(JamlConfigLoader.TryLoad(jaml, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void StandardCard_EmptyProps_LoadsAsAnyPlayingCard()
    {
        var jaml = """
            name: std-any
            deck: Red
            stake: White
            must:
              - standardCard:
                antes: [1]
            """;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        var clause = Assert.IsType<StandardCardClause>(config!.Must[0]);
        Assert.Null(clause.Rank);
        Assert.Null(clause.Suit);
        Assert.Null(clause.Seal);
        Assert.Null(clause.Edition);
        Assert.Null(clause.Enhancement);
    }

    [Fact]
    public void NullJokerArray_IsCategoryAny_LikeEmpty()
    {
        // Host-built clause with null disc list (not loader) must not NRE — same as [].
        var clause = new JokerClause { Jokers = null!, Antes = [1], Score = 1 };
        Assert.True(JamlDisc.IsCategoryAny(clause.Jokers));
        Assert.Empty(JamlDisc.OrEmpty(clause.Jokers));

        var config = new JamlConfig
        {
            Id = "null-jokers",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);
        int delivered = 0;
        using var search = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(["UNITTEST"], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(_ => Interlocked.Increment(ref delivered))
            .Start();
        search.AwaitCompletion();
        Assert.Equal(1, delivered);
    }

    [Fact]
    public void EmptyJoker_FindsASeed_ListProof()
    {
        // Same dense filter StopAfter/Proof smoke uses — empty list is category any.
        var jaml = """
            name: empty-joker-proof
            deck: Red
            stake: White
            must:
              - joker: []
                antes: [1]
            """;
        ProofSearch.MustMatchAll(jaml, "UNITTEST");
    }

    /// <summary>
    /// Empty list + shop default (null sources) matches ordinary spectrals only.
    /// ALEEB on Ghost/White has Sigil in ante-2 shop (S8 ground truth) — not Soul/BlackHole.
    /// </summary>
    [Fact]
    public void EmptySpectral_ShopDefault_MatchesAleebGhost_NotSoulPath()
    {
        var jaml = """
            name: empty-spectral-proof
            deck: Ghost
            stake: White
            must:
              - spectralCard: []
                antes: [1, 2, 3, 4]
            """;
        ProofSearch.MustMatchAll(jaml, "ALEEB");
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        var clause = Assert.IsType<SpectralCardClause>(config!.Must[0]);
        Assert.Empty(clause.Spectrals);
        Assert.Null(clause.Sources);
        Assert.False(SpecialSpectralCardFilterDesc.Handles(clause));
    }

    [Fact]
    public void EmptyPlanet_FindsASeed_ListOrSequential()
    {
        var jaml = """
            name: empty-planet-proof
            deck: Red
            stake: White
            must:
              - planetCard: []
                antes: [1]
            """;
        int delivered = 0;
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        var settings = JamlSearchBuilder
            .CreateSettings(config!)
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(0)
            .WithEndBatchIndex(1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .StopAfter(1)
            .WithSeedMatchCallback(_ => Interlocked.Increment(ref delivered));
        using var search = settings.Start();
        search.AwaitCompletion();
        Assert.True(delivered >= 1, "planet category any should find ≥1 seed quickly");
    }
}
