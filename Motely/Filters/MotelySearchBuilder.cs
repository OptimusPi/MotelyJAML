using System;
using System.Linq;

namespace Motely.Filters;

public sealed class JamlSearchPlan
{
    internal IMotelySearchSettings Settings { get; set; } = null!;
    public int ScoreTallyColumnCount { get; set; }
    public IReadOnlyList<string> TallyLabels { get; set; } = [];
}

public static class MotelySearchBuilder
{
    public static JamlSearchPlan CreatePlan(JamlConfig config, int engineCutoff = int.MinValue)
    {
        var settings = CreateSettings(config, engineCutoff);
        return new JamlSearchPlan
        {
            Settings = settings,
            ScoreTallyColumnCount = config.Should.Count,
            TallyLabels = [.. config.Should.Select(DefaultTallyLabel)],
        };
    }

    /// <summary>
    /// The tally-column label for a should clause: the author's explicit label when given,
    /// otherwise "score{index}".
    /// </summary>
    public static string DefaultTallyLabel(IMotelyClause clause, int index) =>
        clause.Label ?? $"score{index}";

    public static IMotelySearchSettings CreateSettings(JamlConfig config, int engineCutoff = int.MinValue)
    {
        // A JAML with no must/should/mustNot clauses is a valid, real search: deck/stake/seeds
        // and nothing else, with the host's own predicate free to drive the whole decision.
        IMotelySearchSettings settings =
            new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
                new PassthroughFilterDesc()
            );

        // The JAML document names its own deck and stake; the settings carry them from here so
        // every caller searches what the filter says. Callers may still override afterwards
        // (the CLI applies --deck/--stake on top).
        settings = settings.WithDeck(config.Deck).WithStake(config.Stake);

        // A mustNot may only be negated in SIMD when its prefilter is the exact match law: a
        // coarse prefilter passes every lane that might hold the item, so its negation would
        // drop those seeds (a bare legendaryJoker passes all lanes and would reject everything).
        // Coarse mustNot clauses skip the chain and are rejected by the scalar scoring pass.
        var simdMustNot = config.MustNot.Where(ClauseScoring.IsExactFilterConfirm).ToArray();
        var scoredMustNot = config.MustNot.Where(c => !ClauseScoring.IsExactFilterConfirm(c)).ToArray();

        // SIMD filter chain: must + mustNot cheapest-first so trivial clauses kill lanes before
        // expensive SearchIndividualSeeds arms. Does not mutate the config; scoring still sees
        // authored must/should order (tally columns + must re-eval).
        foreach (
            var (clause, negate) in config
                .Must.Select(c => (clause: c, negate: false))
                .Concat(simdMustNot.Select(c => (clause: c, negate: true)))
        )
        {
            var desc = clause.CreateFilterDesc();
            settings = settings.WithAdditionalFilter(
                negate ? new NegationFilterDesc(desc) : desc
            );
        }

        if (config.Must.Count + config.Should.Count + scoredMustNot.Length > 0)
        {
            settings = settings.WithSeedScoreProvider(
                new JamlShouldScoreDesc(
                    [.. config.Must],
                    [.. config.Should],
                    minimumTotalScore: engineCutoff,
                    mustNotClauses: scoredMustNot
                )
            );
        }

        return settings;
    }
}
