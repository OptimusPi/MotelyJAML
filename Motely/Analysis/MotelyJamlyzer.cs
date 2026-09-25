using Motely.Filters.Jaml;

namespace Motely.Analysis;

public sealed record MotelyJamlyzerSeedResult(
    string Seed,
    int Score,
    IReadOnlyList<MotelyJamlyzerAnteResult> Antes,
    MotelyJamlyzerEvents Events,
    MotelyJamlyzerStreamStates StreamStates,
    MotelyItem[]? ErraticDeck = null,
    int[]? Tally = null
);

public sealed record MotelyJamlyzerAnteResult(
    int Ante,
    MotelyBossBlind Boss,
    MotelyVoucher Voucher,
    MotelyTag SmallBlindTag,
    MotelyTag BigBlindTag,
    IReadOnlyList<MotelyItem> ShopItems,
    IReadOnlyList<MotelyJamlyzerPack> Packs,
    MotelyJamlyzerPulls Pulls,
    MotelyJamlyzerShopStreams ShopStreams
);

public sealed record MotelyJamlyzerPack(MotelyBoosterPack Pack, IReadOnlyList<MotelyItem> Items);

public sealed record MotelyJamlyzerShopStreams(
    IReadOnlyList<MotelyItem> ShopJokers,
    IReadOnlyList<MotelyItem> CommonShopJokers,
    IReadOnlyList<MotelyItem> UncommonShopJokers,
    IReadOnlyList<MotelyItem> RareShopJokers,
    IReadOnlyList<MotelyItem> ShopTarots,
    IReadOnlyList<MotelyItem> ShopPlanets,
    IReadOnlyList<MotelyItem> ShopSpectrals
);

public sealed record MotelyJamlyzerPulls(
    IReadOnlyList<MotelyItem> JudgementJokers,
    IReadOnlyList<MotelyItem> WraithJokers,
    IReadOnlyList<MotelyItem> EmperorTarots,
    IReadOnlyList<MotelyItem> PurpleSealTarots,
    IReadOnlyList<MotelyItem> SixthSenseSpectrals,
    IReadOnlyList<MotelyItem> SeanceSpectrals,
    IReadOnlyList<MotelyItem> RiffRaffJokers,
    IReadOnlyList<MotelyItem> RareTagJokers,
    IReadOnlyList<MotelyItem> UncommonTagJokers,
    IReadOnlyList<MotelyItem> LegendaryJokers,
    IReadOnlyList<MotelyVoucher> VoucherSequence
);

public sealed record MotelyJamlyzerStreamStates(
    int RollOffset,
    int ShopOffset,
    double LuckyMoney,
    double LuckyMult,
    double WheelOfFortune,
    double Cavendish,
    double GrosMichel,
    double Space,
    double Business,
    double Bloodstone,
    double Parking,
    double EightBall,
    double Glass,
    double OmenGlobe,
    double TheWheel,
    double Misprint
);

public sealed record MotelyJamlyzerEvents(
    bool[] LuckyMoney,
    bool[] LuckyMult,
    MotelyItemEdition[] WheelOfFortune,
    bool[] Cavendish,
    bool[] GrosMichel,
    bool[] Space,
    bool[] Business,
    bool[] Bloodstone,
    bool[] Parking,
    bool[] EightBall,
    bool[] Glass,
    bool[] OmenGlobe,
    bool[] TheWheel,
    int[] Misprint
);

public static class MotelyJamlyzer
{
    public static IReadOnlyList<MotelyJamlyzerSeedResult> Analyze(
        JamlConfig config,
        int eventRolls = 20,
        int shopSlots = 0
    ) => AnalyzeCore(config, resumeStates: null, eventRolls, shopSlots);

    public static IReadOnlyList<MotelyJamlyzerSeedResult> Analyze(
        JamlConfig config,
        MotelyJamlyzerStreamStates resumeFrom,
        int eventRolls = 20,
        int shopSlots = 0
    )
    {
        if (config.Seeds.Count > 1)
            throw new InvalidOperationException(
                $"Resume (resumeFrom) is single-seed only; config has {config.Seeds.Count} seeds. "
                    + "Scroll one seed at a time, or use the per-seed dictionary overload — the "
                    + "state bag is seed-specific."
            );
        return AnalyzeCore(
            config,
            new Dictionary<string, MotelyJamlyzerStreamStates> { [config.Seeds[0]] = resumeFrom },
            eventRolls,
            shopSlots
        );
    }

    public static IReadOnlyList<MotelyJamlyzerSeedResult> Analyze(
        JamlConfig config,
        IReadOnlyDictionary<string, MotelyJamlyzerStreamStates> resumeFrom,
        int eventRolls = 20,
        int shopSlots = 0
    ) => AnalyzeCore(config, resumeFrom, eventRolls, shopSlots);

    public static readonly int[] AllAntes = [0, 1, 2, 3, 4, 5, 6, 7, 8];

    public static MotelyJamlyzerRiderDesc CreateRiderDesc(
        JamlConfig config,
        Action<MotelyJamlyzerSeedResult> onAnalyzed,
        int eventRolls = 20,
        int shopSlots = 0
    ) => new(ComputeAntes(config), onAnalyzed, eventRolls, shopSlots);

    private static IReadOnlyList<MotelyJamlyzerSeedResult> AnalyzeCore(
        JamlConfig config,
        IReadOnlyDictionary<string, MotelyJamlyzerStreamStates>? resumeStates,
        int eventRolls,
        int shopSlots = 0
    )
    {
        var antesToAnalyze = ComputeAntes(config);
        bool hasScore = config.Must.Count + config.Should.Count > 0;
        var results = new List<MotelyJamlyzerSeedResult>(config.Seeds.Count);

        foreach (var seed in config.Seeds)
        {
            MotelyJamlyzerStreamStates? seedResume =
                resumeStates is not null && resumeStates.TryGetValue(seed, out var s) ? s : null;
            var filterDesc = new MotelyJamlyzerFilterDesc(
                antesToAnalyze,
                eventRolls,
                seedResume,
                shopSlots
            );
            var settings = new MotelySearchSettings<MotelyJamlyzerFilterDesc.JamlyzerFilter>(
                filterDesc
            )
                .WithDeck(config.Deck)
                .WithStake(config.Stake)
                .WithSeedList([seed])
                .WithThreadCount(1);

            int score = 0;
            int[]? tally = null;
            if (hasScore)
            {
                settings = settings
                    .WithSeedScoreProvider(
                        new JamlShouldScoreDesc(
                            [.. config.Must],
                            [.. config.Should],
                            minimumTotalScore: 0
                        )
                    )
                    .WithScoredResultCallback(row =>
                    {
                        score = row.Score;
                        tally = row.Tallies;
                    });
            }

            using var search = settings.CreateSearch();
            search.Start();
            search.AwaitCompletion();

            if (filterDesc.Result is { } result)
                results.Add(result with { Score = score, Tally = tally });
        }

        return results;
    }

    public static int[] ComputeAntes(JamlConfig config)
    {
        var set = new SortedSet<int>();
        foreach (
            var clause in config
                .Must.Concat(config.Should)
                .Concat(config.MustNot)
                .OfType<IAnteScopedClause>()
        )
        foreach (var ante in clause.Antes)
            set.Add(ante);
        return set.Count > 0 ? [.. set] : AllAntes;
    }
}
