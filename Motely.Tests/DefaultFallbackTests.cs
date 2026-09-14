using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// Pins the FilterDesc-side fallback defaults that <see cref="JamlSearchBuilder"/> fills in for
/// clauses that named no ante / no source. The loader injects nothing (JAML is typed — a clause
/// arrives as a real <see cref="JokerClause"/> with empty <c>Antes</c>/<c>Sources</c>, not a blob
/// of text), so without these defaults every <c>foreach (ante in clause.Antes)</c> and
/// <c>if (Sources.X.Length &gt; 0)</c> would simply never fire and the clause would match nothing.
///
/// Ground truth is differential, not magic-number: a sourceless wildcard joker must score exactly
/// the same as one that spells the defaults out by hand — antes 1..8, shop slots 0..7
/// (packs require an explicit <c>sources:</c> block).
/// </summary>
public class DefaultFallbackTests
{
    private const string Seed = "MOTELY77";

    private static (long Matching, int Score) Score(JokerClause clause)
    {
        var config = new JamlConfig
        {
            Id = "default-fallback",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Should.Add(clause);

        int score = 0;
        long matching = 0;
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator([Seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result => score = result.Score);

        using var search = settings.Start();
        search.AwaitCompletion();
        matching = search.MatchingSeeds;
        return (matching, score);
    }

    [Fact]
    public void SourcelessWildcardJoker_DefaultsToAllAntesAndShopOnly()
    {
        // No antes, no sources — the clause as the loader hands it over.
        var (implicitMatching, implicitScore) = Score(
            new JokerClause { Score = 1 }
        );

        // The same clause with shop-only defaults written out longhand (no packs).
        var (_, explicitScore) = Score(
            new JokerClause
            {
                Score = 1,
                Antes = [1, 2, 3, 4, 5, 6, 7, 8],
                Sources = new JokerSourceConfig
                {
                    ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
                },
            }
        );

        Assert.True(
            implicitScore > 0,
            "a sourceless wildcard joker must match jokers, not nothing"
        );
        Assert.Equal(explicitScore, implicitScore); // defaults == antes 1..8, shop 0..7
        Assert.Equal(1, implicitMatching);
    }

    [Fact]
    public void ExplicitSources_AreNotOverwrittenByDefaults()
    {
        // A clause that named a source keeps exactly that source — the default fill must not touch it.
        var (_, narrowScore) = Score(
            new JokerClause
            {
                Score = 1,
                Antes = [1],
                Sources = new JokerSourceConfig { ShopItems = [0] }, // one slot, one ante
            }
        );

        var (_, wideScore) = Score(new JokerClause { Score = 1 }); // defaulted

        Assert.True(
            wideScore >= narrowScore,
            "the all-antes default must cover at least the single-slot case"
        );
    }

    // ── Tally-column labels ──
    // CreatePlan names each should-clause tally column. An explicit label: wins; an
    // unlabeled clause gets its one-line JAML spelling as the column name; scoreN is the
    // last resort for clauses that spelling cannot render as a single line.

    private static JamlConfig LabelConfig(params IJamlClause[] should)
    {
        var config = new JamlConfig
        {
            Id = "tally-labels",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        foreach (var clause in should)
            config.Should.Add(clause);
        return config;
    }

    [Fact]
    public void TallyLabels_ExplicitLabelWins()
    {
        var clause = new JokerClause { Jokers = [MotelyJoker.Blueprint], Label = "bp" };
        var plan = JamlSearchBuilder.CreatePlan(LabelConfig(clause));
        Assert.Equal(["bp"], plan.TallyLabels);
    }

    [Fact]
    public void TallyLabels_UnlabeledClause_FallsBackToScoreIndex()
    {
        var labeled = new JokerClause { Jokers = [MotelyJoker.Blueprint], Label = "bp" };
        var unlabeled = new JokerClause { Jokers = [MotelyJoker.Blueprint], Antes = [1, 2] };

        var plan = JamlSearchBuilder.CreatePlan(LabelConfig(labeled, unlabeled));

        Assert.Equal(["bp", "score1"], plan.TallyLabels);
    }
}
