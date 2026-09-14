using System;
using System.Linq;
using Motely.Filters.Jaml;
using Motely.Filters.Native;

namespace Motely.Filters;

public sealed class JamlSearchPlan
{
    internal IMotelySearchSettings Settings { get; set; } = null!;
    public int ScoreTallyColumnCount { get; set; }
    public IReadOnlyList<string> TallyLabels { get; set; } = [];
}

public static class JamlSearchBuilder
{
    /// <summary>Default ante scope for an ante-scoped clause that named no antes: all 8.</summary>
    private static readonly int[] DefaultAntes = [1, 2, 3, 4, 5, 6, 7, 8];

    public static JamlSearchPlan CreatePlan(JamlConfig config, int engineCutoff = 0)
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
    /// Re-apply <see cref="LogicClause.Antes"/> down the tree (same law as loader HoistAntes).
    /// </summary>
    private static void RehoistLogicAntes(IJamlClause clause)
    {
        if (clause is not LogicClause logic)
            return;
        if (logic.Antes.Length > 0)
            JamlConfigLoader.HoistAntes(logic.Clauses, logic.Antes);
        for (int i = 0; i < logic.Clauses.Length; i++)
            RehoistLogicAntes(logic.Clauses[i]);
    }

    /// <summary>
    /// Fill empty <c>antes</c> with 1..8 on this clause and every nested <c>and:</c>/<c>or:</c> arm.
    /// </summary>
    private static void FillDefaultAntes(IJamlClause clause)
    {
        if (clause is IAnteScopedClause { Antes.Length: 0 } anteScoped)
            anteScoped.Antes = DefaultAntes;
        if (clause is LogicClause logic)
        {
            for (int i = 0; i < logic.Clauses.Length; i++)
                FillDefaultAntes(logic.Clauses[i]);
        }
    }

    /// <summary>
    /// The tally-column label for a should clause: the author's explicit label when given,
    /// otherwise "score{index}".
    /// </summary>
    public static string DefaultTallyLabel(IJamlClause clause, int index) =>
        clause.Label ?? $"score{index}";

    /// <summary>
    /// The ante normalization every scoring pass assumes, applied in place:
    /// 1) Parent and:/or: antes pass through nested arms that did not override (re-hoist so
    ///    programmatic clause trees match loader shape).
    /// 2) Still-empty antes → default 1..8 (sourceless == "anywhere"). Neg Tag authors
    ///    2..8 explicitly so they never ride this default. Event clauses are roll-scoped.
    /// <see cref="CreateSettings"/> does this before building; the standalone Jamlyzer does it
    /// before scoring, so both report the same score for the same JAML and seed. Idempotent.
    /// </summary>
    internal static void NormalizeAntes(JamlConfig config)
    {
        foreach (var clause in config.Must.Concat(config.Should).Concat(config.MustNot))
            RehoistLogicAntes(clause);
        foreach (var clause in config.Must.Concat(config.Should).Concat(config.MustNot))
            FillDefaultAntes(clause);
    }

    public static IMotelySearchSettings CreateSettings(JamlConfig config, int engineCutoff = 0)
    {
        // A JAML with no must/should/mustNot clauses is a valid, real search: deck/stake/seeds
        // and nothing else, with the host's own predicate free to drive the whole decision.
        NormalizeAntes(config);

        IMotelySearchSettings settings =
            config.Filter is { Length: > 0 } filterName
                && MotelyNativeFilterNames.TryParse(filterName, out var nativeFilter)
                ? MotelyNativeFilterFactory.CreateSettings(nativeFilter)
                : new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
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
        var simdMustNot = config.MustNot.Where(JamlScoring.IsExactFilterConfirm).ToArray();
        var scoredMustNot = config.MustNot.Where(c => !JamlScoring.IsExactFilterConfirm(c)).ToArray();

        // SIMD filter chain: must + mustNot cheapest-first so trivial clauses kill lanes before
        // expensive SearchIndividualSeeds arms. Does not mutate the config; scoring still sees
        // authored must/should order (tally columns + must re-eval).
        foreach (
            var (clause, negate) in config
                .Must.Select(c => (clause: c, negate: false))
                .Concat(simdMustNot.Select(c => (clause: c, negate: true)))
                .OrderBy(x => x.clause.EstimateCrunches())
        )
        {
            var desc = ClauseToFilterDesc(clause);
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

    /// <summary>
    /// Maps one JAML clause to its SIMD filter desc. Soul/BlackHole spectral clauses route to
    /// <see cref="SpecialSpectralCardFilterDesc"/> (T4); every other spectral stays on the content path.
    /// Internal so tests can lock the special-spectral gate without reflecting private methods.
    /// </summary>
    internal static IMotelySeedFilterDesc ClauseToFilterDesc(IJamlClause clause) =>
        clause switch
        {
            JokerClause c => new JokerFilterDesc(c),
            CommonJokerClause c => new CommonJokerFilterDesc(c),
            UncommonJokerClause c => new UncommonJokerFilterDesc(c),
            RareJokerClause c => new RareJokerFilterDesc(c),
            LegendaryJokerClause c => new LegendaryJokerFilterDesc(c),
            VoucherClause c => new VoucherFilterDesc(c),
            TarotCardClause c => new TarotCardFilterDesc(c),
            SpectralCardClause c => SpecialSpectralCardFilterDesc.Handles(c)
                ? new SpecialSpectralCardFilterDesc(c)
                : new SpectralCardFilterDesc(c),
            PlanetCardClause c => new PlanetCardFilterDesc(c),
            BossClause c => new BossFilterDesc(c),
            TagClause c => new TagFilterDesc(c),
            BoosterPackClause c => new BoosterPackFilterDesc(c),
            StandardCardClause c => new StandardCardFilterDesc(c),
            ErraticRankClause c => new ErraticRankFilterDesc(c),
            ErraticSuitClause c => new ErraticSuitFilterDesc(c),
            LuckyMoneyClause c => new LuckyMoneyFilterDesc(c),
            LuckyMultClause c => new LuckyMultFilterDesc(c),
            MisprintMultClause c => new MisprintMultFilterDesc(c),
            WheelOfFortuneClause c => new WheelOfFortuneFilterDesc(c),
            CavendishExtinctClause c => new CavendishExtinctFilterDesc(c),
            GrosMichelExtinctClause c => new GrosMichelExtinctFilterDesc(c),
            SpaceLevelupClause c => new SpaceLevelupFilterDesc(c),
            BusinessPayoutClause c => new BusinessPayoutFilterDesc(c),
            BloodstoneTriggerClause c => new BloodstoneTriggerFilterDesc(c),
            ParkingPayoutClause c => new ParkingPayoutFilterDesc(c),
            GlassDestroyClause c => new GlassDestroyFilterDesc(c),
            WheelStaysFlippedClause c => new WheelStaysFlippedFilterDesc(c),
            StartingDrawClause c => new StartingDrawFilterDesc(c),
            PokerHandClause c => new PokerHandFilterDesc(c),
            AndClause c => new AndFilterDesc([.. c.Clauses.Select(ClauseToFilterDesc)]),
            OrClause c => new OrFilterDesc([.. c.Clauses.Select(ClauseToFilterDesc)], c.Min),
            _ => throw new InvalidOperationException(
                $"JamlSearchBuilder: clause '{clause.GetType().Name}' is not supported in the SIMD filter pass."
            ),
        };
}
