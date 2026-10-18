using System.Collections.Concurrent;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// Property sweep: random clause configs over a fixed random seed list, engine result vs the
/// scalar law (passthrough + scoring pass with must re-eval forced and every mustNot scored).
/// </summary>
public sealed class JamlPropertySweepTests(ITestOutputHelper output)
{
    private static string[] RandomSeeds(int count, int rngSeed)
    {
        var rng = new Random(rngSeed);
        var digits = MotelyGlobals.SeedDigits;
        var seeds = new string[count];
        for (int i = 0; i < count; i++)
        {
            var chars = new char[8];
            for (int c = 0; c < 8; c++)
                chars[c] = digits[rng.Next(digits.Length)];
            seeds[i] = new string(chars);
        }
        return seeds;
    }

    // ── random clause generation ──

    private static T Pick<T>(Random r, T[] values) => values[r.Next(values.Length)];

    private static T[] PickSome<T>(Random r, T[] values, int min, int max)
    {
        int n = r.Next(min, max + 1);
        var list = new List<T>();
        for (int i = 0; i < n; i++)
            list.Add(Pick(r, values));
        return [.. list.Distinct()];
    }

    private static int[] Subset(Random r, int lo, int hi, int minCount, int maxCount)
    {
        int n = r.Next(minCount, maxCount + 1);
        var set = new SortedSet<int>();
        for (int i = 0; i < n * 3 && set.Count < n; i++)
            set.Add(r.Next(lo, hi + 1));
        if (set.Count == 0)
            set.Add(lo);
        return [.. set];
    }

    private static int[] RandomAntes(Random r)
    {
        int roll = r.Next(10);
        if (roll < 6)
            return Subset(r, 1, 3, 1, 2);
        if (roll < 8)
            return Subset(r, 0, 8, 1, 3);
        return Subset(r, 0, MaxSweepAnte, 1, 3);
    }

    private static readonly int MaxSweepAnte =
        int.TryParse(Environment.GetEnvironmentVariable("MOTELY_SWEEP_MAXANTE"), out var m) ? m : 39;

    private static MotelyItemEdition? RandomEdition(Random r) =>
        r.Next(4) == 0 ? Pick(r, Enum.GetValues<MotelyItemEdition>()) : null;

    private static void RandomBounds(Random r, IMotelyClause c)
    {
        c.Min = r.Next(6) == 0 ? r.Next(2, 4) : 1;
        c.Max = r.Next(5) == 0 ? c.Min + r.Next(0, 2) : null;
        c.Score = r.Next(6) == 0 ? r.Next(-3, 1) : r.Next(1, 6);
    }

    private static JokerSourceConfig? RandomJokerSources(Random r)
    {
        int roll = r.Next(8);
        if (roll < 3)
            return null;
        var s = new JokerSourceConfig();
        if (r.Next(3) != 0)
            s.ShopItems = Subset(r, 0, 9, 1, 4);
        if (r.Next(2) == 0)
            s.BoosterPacks = Subset(r, 0, 7, 1, 4);
        if (r.Next(6) == 0)
            s.RequireMegaPack = true;
        if (r.Next(8) == 0)
            s.Judgement = Subset(r, 0, 3, 1, 2);
        if (r.Next(8) == 0)
            s.Wraith = Subset(r, 0, 3, 1, 2);
        if (r.Next(8) == 0)
            s.RiffRaff = Subset(r, 0, 3, 1, 2);
        if (r.Next(8) == 0)
            s.RareTag = Subset(r, 0, 2, 1, 1);
        if (r.Next(8) == 0)
            s.UncommonTag = Subset(r, 0, 2, 1, 1);
        if (r.Next(8) == 0)
            s.CommonShopJokers = Subset(r, 0, 4, 1, 2);
        if (r.Next(8) == 0)
            s.UncommonShopJokers = Subset(r, 0, 4, 1, 2);
        if (r.Next(8) == 0)
            s.RareShopJokers = Subset(r, 0, 4, 1, 2);
        if (r.Next(8) == 0)
            s.AllShopJokers = Subset(r, 0, 4, 1, 2);
        return s;
    }

    private static MotelyJokerSticker[] RandomStickers(Random r) =>
        r.Next(8) == 0 ? PickSome(r, [MotelyJokerSticker.Eternal, MotelyJokerSticker.Perishable, MotelyJokerSticker.Rental], 1, 1) : [];

    private static readonly MotelyJoker[] AllJokers = Enum.GetValues<MotelyJoker>();
    private static readonly MotelyJoker[] Legendaries =
    [
        MotelyJoker.Perkeo, MotelyJoker.Triboulet, MotelyJoker.Yorick, MotelyJoker.Chicot, MotelyJoker.Canio,
    ];

    private static IMotelyClause RandomLeaf(Random r)
    {
        IMotelyClause c;
        switch (r.Next(22))
        {
            case 0:
            case 1:
            {
                var jc = new JokerClause
                {
                    Antes = RandomAntes(r),
                    Jokers = r.Next(5) == 0 ? [] : PickSome(r, AllJokers, 1, 3),
                    Edition = RandomEdition(r),
                    Stickers = RandomStickers(r),
                    Sources = RandomJokerSources(r),
                };
                if (r.Next(6) == 0)
                    jc.Jokers = [.. jc.Jokers, Pick(r, Legendaries)];
                c = jc;
                break;
            }
            case 2:
                c = new CommonJokerClause
                {
                    Antes = RandomAntes(r),
                    Jokers = r.Next(4) == 0 ? [] : PickSome(r, Enum.GetValues<MotelyJokerCommon>(), 1, 3),
                    Edition = RandomEdition(r),
                    Stickers = RandomStickers(r),
                    Sources = RandomJokerSources(r),
                };
                break;
            case 3:
                c = new UncommonJokerClause
                {
                    Antes = RandomAntes(r),
                    Jokers = r.Next(4) == 0 ? [] : PickSome(r, Enum.GetValues<MotelyJokerUncommon>(), 1, 3),
                    Edition = RandomEdition(r),
                    Stickers = RandomStickers(r),
                    Sources = RandomJokerSources(r),
                };
                break;
            case 4:
                c = new RareJokerClause
                {
                    Antes = RandomAntes(r),
                    Jokers = r.Next(4) == 0 ? [] : PickSome(r, Enum.GetValues<MotelyJokerRare>(), 1, 3),
                    Edition = RandomEdition(r),
                    Stickers = RandomStickers(r),
                    Sources = RandomJokerSources(r),
                };
                break;
            case 5:
            {
                var lc = new LegendaryJokerClause
                {
                    Antes = RandomAntes(r),
                    Jokers = r.Next(3) == 0 ? [] : PickSome(r, Legendaries, 1, 2),
                    Edition = RandomEdition(r),
                    SoulCardOnly = r.Next(8) == 0,
                };
                int sroll = r.Next(5);
                if (sroll == 1)
                    lc.Sources = new LegendaryJokerSourceConfig { BoosterPacks = Subset(r, 0, 7, 1, 4) };
                else if (sroll == 2)
                    lc.Sources = new LegendaryJokerSourceConfig
                    {
                        ArcanaPacks = Subset(r, 0, 5, 1, 3),
                        SpectralPacks = r.Next(2) == 0 ? Subset(r, 0, 5, 1, 3) : [],
                    };
                else if (sroll == 3)
                    lc.Sources = new LegendaryJokerSourceConfig
                    {
                        BoosterPacks = Subset(r, 0, 5, 1, 4),
                        RequireMegaPack = true,
                    };
                c = lc;
                break;
            }
            case 6:
                c = new VoucherClause
                {
                    Antes = RandomAntes(r),
                    Vouchers = PickSome(r, Enum.GetValues<MotelyVoucher>(), 1, 4),
                    Rolls = r.Next(4) == 0 ? Subset(r, 0, 3, 1, 3) : [0],
                };
                break;
            case 7:
                c = new TagClause
                {
                    Antes = RandomAntes(r),
                    Tags = PickSome(r, Enum.GetValues<MotelyTag>(), 1, 4),
                    Rolls = Pick(r, new int[][] { [0], [1], [0, 1] }),
                };
                break;
            case 8:
                c = new BossClause
                {
                    Antes = Subset(r, 1, 8, 1, 3),
                    Bosses = PickSome(r, Enum.GetValues<MotelyBossBlind>(), 1, 5),
                };
                break;
            case 9:
                c = new BoosterPackClause
                {
                    Antes = RandomAntes(r),
                    Packs = r.Next(5) == 0 ? [] : PickSome(r, Enum.GetValues<MotelyBoosterPack>(), 1, 4),
                    Rolls = Subset(r, 0, 7, 1, 3),
                };
                break;
            case 10:
            {
                TarotCardSourceConfig? s = null;
                if (r.Next(3) != 0)
                {
                    s = new TarotCardSourceConfig();
                    if (r.Next(2) == 0)
                        s.ShopItems = Subset(r, 0, 9, 1, 4);
                    if (r.Next(2) == 0)
                        s.BoosterPacks = Subset(r, 0, 7, 1, 4);
                    if (r.Next(5) == 0)
                        s.Emperor = Subset(r, 0, 2, 1, 2);
                    if (r.Next(5) == 0)
                        s.PurpleSealOrEightBall = Subset(r, 0, 2, 1, 2);
                    if (r.Next(5) == 0)
                        s.CharmTag = true;
                    if (r.Next(6) == 0)
                        s.RequireMegaPack = true;
                }
                c = new TarotCardClause
                {
                    Antes = RandomAntes(r),
                    Tarots = r.Next(5) == 0 ? [] : PickSome(r, Enum.GetValues<MotelyTarotCard>(), 1, 3),
                    Sources = s,
                };
                break;
            }
            case 11:
            {
                SpectralCardSourceConfig? s = null;
                if (r.Next(3) != 0)
                {
                    s = new SpectralCardSourceConfig();
                    if (r.Next(2) == 0)
                        s.ShopItems = Subset(r, 0, 9, 1, 4);
                    if (r.Next(2) == 0)
                        s.BoosterPacks = Subset(r, 0, 7, 1, 4);
                    if (r.Next(5) == 0)
                        s.SixthSense = Subset(r, 0, 2, 1, 2);
                    if (r.Next(5) == 0)
                        s.Seance = Subset(r, 0, 2, 1, 2);
                    if (r.Next(5) == 0)
                        s.EtherealTag = true;
                    if (r.Next(6) == 0)
                        s.OmenGlobe = true;
                    if (r.Next(6) == 0)
                        s.RequireMegaPack = true;
                }
                c = new SpectralCardClause
                {
                    Antes = RandomAntes(r),
                    Spectrals = r.Next(5) == 0 ? [] : PickSome(r, Enum.GetValues<MotelySpectralCard>(), 1, 3),
                    Sources = s,
                };
                break;
            }
            case 12:
            {
                PlanetSourceConfig? s = null;
                if (r.Next(3) != 0)
                {
                    s = new PlanetSourceConfig();
                    if (r.Next(2) == 0)
                        s.ShopItems = Subset(r, 0, 9, 1, 4);
                    if (r.Next(2) == 0)
                        s.BoosterPacks = Subset(r, 0, 7, 1, 4);
                    if (r.Next(6) == 0)
                        s.RequireMegaPack = true;
                }
                c = new PlanetCardClause
                {
                    Antes = RandomAntes(r),
                    Planets = r.Next(5) == 0 ? [] : PickSome(r, Enum.GetValues<MotelyPlanetCard>(), 1, 3),
                    Sources = s,
                };
                break;
            }
            case 13:
            {
                StandardCardSourceConfig? s = null;
                if (r.Next(3) != 0)
                {
                    s = new StandardCardSourceConfig();
                    if (r.Next(2) == 0)
                        s.ShopItems = Subset(r, 0, 9, 1, 4);
                    if (r.Next(2) == 0)
                        s.BoosterPacks = Subset(r, 0, 7, 1, 4);
                    if (r.Next(6) == 0)
                        s.RequireMegaPack = true;
                }
                c = new StandardCardClause
                {
                    Antes = RandomAntes(r),
                    Rank = r.Next(2) == 0 ? Pick(r, Enum.GetValues<MotelyStandardcardRank>()) : null,
                    Suit = r.Next(2) == 0 ? Pick(r, Enum.GetValues<MotelyStandardcardSuit>()) : null,
                    Enhancement = r.Next(3) == 0 ? Pick(r, Enum.GetValues<MotelyItemEnhancement>()) : null,
                    Seal = r.Next(3) == 0 ? Pick(r, Enum.GetValues<MotelyItemSeal>()) : null,
                    Edition = RandomEdition(r),
                    Sources = s,
                };
                break;
            }
            case 14:
                c = r.Next(2) == 0
                    ? new ErraticRankClause { Antes = RandomAntes(r), Rank = Pick(r, Enum.GetValues<MotelyStandardcardRank>()) }
                    : new ErraticSuitClause { Antes = RandomAntes(r), Suit = Pick(r, Enum.GetValues<MotelyStandardcardSuit>()) };
                break;
            case 15:
                c = new StartingDrawClause
                {
                    Antes = Subset(r, 1, 4, 1, 2),
                    Rank = r.Next(2) == 0 ? Pick(r, Enum.GetValues<MotelyStandardcardRank>()) : null,
                    Suit = r.Next(2) == 0 ? Pick(r, Enum.GetValues<MotelyStandardcardSuit>()) : null,
                };
                break;
            case 16:
                c = new PokerHandClause
                {
                    Antes = Subset(r, 1, 4, 1, 2),
                    PokerHands = r.Next(5) == 0 ? [] : PickSome(r, Enum.GetValues<MotelyPokerHand>(), 1, 3),
                    Rolls = Subset(r, 0, 2, 1, 2),
                };
                break;
            default:
                c = RandomEvent(r);
                break;
        }
        RandomBounds(r, c);
        return c;
    }

    private static IMotelyClause RandomEvent(Random r)
    {
        int[] rolls = Subset(r, 0, 12, 1, 5);
        var luck = new MotelyWith { Luck = Pick(r, Enum.GetValues<MotelyLuck>()) };
        return r.Next(12) switch
        {
            0 => new LuckyMoneyClause { Rolls = rolls, With = luck },
            1 => new LuckyMultClause { Rolls = rolls, With = luck },
            2 => new MisprintMultClause { Rolls = rolls, Mult = r.Next(0, 24) },
            3 => new WheelOfFortuneClause { Rolls = rolls, With = luck },
            4 => new CavendishExtinctClause { Rolls = rolls, With = luck },
            5 => new GrosMichelExtinctClause { Rolls = rolls, With = luck },
            6 => new SpaceLevelupClause { Rolls = rolls, With = luck },
            7 => new BusinessPayoutClause { Rolls = rolls },
            8 => new BloodstoneTriggerClause { Rolls = rolls },
            9 => new ParkingPayoutClause { Rolls = rolls },
            10 => new GlassDestroyClause { Rolls = rolls, With = luck },
            _ => new WheelStaysFlippedClause { Rolls = rolls, With = luck },
        };
    }

    private static IMotelyClause RandomClause(Random r, int depth)
    {
        if (depth < 2 && r.Next(6) == 0)
        {
            int arms = r.Next(1, 4);
            var children = new IMotelyClause[arms];
            for (int i = 0; i < arms; i++)
                children[i] = RandomClause(r, depth + 1);
            LogicClause logic = r.Next(2) == 0 ? new AndClause() : new OrClause();
            logic.Clauses = children;
            logic.Min = logic is OrClause && r.Next(4) == 0 ? r.Next(1, arms + 1) : 1;
            logic.Max = null;
            logic.Score = r.Next(4) == 0 ? 0 : r.Next(1, 4);
            logic.Mode = r.Next(3) == 0 ? LogicScoreMode.Max : LogicScoreMode.Sum;
            return logic;
        }
        return RandomLeaf(r);
    }

    private static JamlConfig RandomConfig(Random r, int index)
    {
        var config = new JamlConfig
        {
            Id = $"sweep-{index}",
            Deck = Pick(r, new[] { MotelyDeck.Red, MotelyDeck.Ghost, MotelyDeck.Erratic, MotelyDeck.Anaglyph, MotelyDeck.Magic, MotelyDeck.Plasma }),
            Stake = Pick(r, new[] { MotelyStake.White, MotelyStake.Gold, MotelyStake.Black }),
        };
        int must = r.Next(0, 3);
        int should = r.Next(0, 3);
        int mustNot = r.Next(4) == 0 ? 1 : 0;
        if (must + should + mustNot == 0)
            must = 1;
        for (int i = 0; i < must; i++)
            config.Must.Add(RandomClause(r, 0));
        for (int i = 0; i < should; i++)
            config.Should.Add(RandomClause(r, 0));
        for (int i = 0; i < mustNot; i++)
            config.MustNot.Add(RandomClause(r, 0));
        return config;
    }

    // ── runs ──

    internal sealed record Row(int Score, int[] Tallies);

    private static Dictionary<string, Row> RunEngine(JamlConfig config, string[] seeds, int threads)
    {
        var rows = new ConcurrentDictionary<string, Row>();
        var matched = new ConcurrentBag<string>();
        var settings = MotelySearchBuilder
            .CreateSettings(config)
            .WithSeedList(seeds)
            .WithThreadCount(threads)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => matched.Add(s))
            .WithScoredResultCallback(res => rows[res.Seed] = new Row(res.Score, res.Tallies));
        using var search = settings.Start();
        search.AwaitCompletion();
        // Must-only exact path with no score provider: rows come from the match callback only.
        foreach (var s in matched)
            rows.TryAdd(s, new Row(0, []));
        return new Dictionary<string, Row>(rows);
    }

    /// <summary>The scalar law: every seed scored with must re-eval forced and every mustNot scored.</summary>
    private sealed class OracleScoreDesc(JamlConfig config)
        : IMotelySeedScoreDesc<JamlShouldScoreDesc.JamlShouldScoreProvider>
    {
        public JamlShouldScoreDesc.JamlShouldScoreProvider CreateScoreProvider(ref MotelyFilterCreationContext ctx) =>
            new([.. config.Must], [.. config.Should], null, 0, skipMustReeval: false, [.. config.MustNot]);

        IMotelySeedScoreProvider IMotelySeedScoreDesc.CreateScoreProvider(ref MotelyFilterCreationContext ctx) =>
            CreateScoreProvider(ref ctx);
    }

    private static Dictionary<string, Row> RunOracle(JamlConfig config, string[] seeds)
    {
        var rows = new ConcurrentDictionary<string, Row>();
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(new PassthroughFilterDesc())
            .WithDeck(config.Deck)
            .WithStake(config.Stake)
            .WithSeedScoreProvider(new OracleScoreDesc(config))
            .WithSeedList(seeds)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(res => rows[res.Seed] = new Row(res.Score, res.Tallies));
        using var search = settings.Start();
        search.AwaitCompletion();
        return new Dictionary<string, Row>(rows);
    }

    internal static string Describe(IMotelyClause c) => c switch
    {
        LogicClause l => $"{l.GetType().Name}(min={l.Min},max={l.Max},score={l.Score},mode={l.Mode})[{string.Join("; ", l.Clauses.Select(Describe))}]",
        _ => $"{c.GetType().Name}{{{string.Join(",", c.GetType().GetProperties().Where(p => p.Name != "Label").Select(p => $"{p.Name}={Fmt(p.GetValue(c))}"))}}}",
    };

    private static string Fmt(object? v) => v switch
    {
        null => "null",
        string s => s,
        Array a => "[" + string.Join(",", a.Cast<object>().Select(Fmt)) + "]",
        JokerSourceConfig or TarotCardSourceConfig or SpectralCardSourceConfig or PlanetSourceConfig or StandardCardSourceConfig or LegendaryJokerSourceConfig =>
            "{" + string.Join(",", v.GetType().GetProperties().Select(p => (p.Name, Val: p.GetValue(v))).Where(t => t.Val is not (Array { Length: 0 } or false)).Select(t => $"{t.Name}={Fmt(t.Val)}")) + "}",
        MotelyWith w => $"luck={w.Luck}",
        _ => v.ToString() ?? "",
    };

    private static string DescribeConfig(JamlConfig c) =>
        $"deck={c.Deck} stake={c.Stake}\n  must: {string.Join("\n        ", c.Must.Select(Describe))}\n  should: {string.Join("\n          ", c.Should.Select(Describe))}\n  mustNot: {string.Join("\n           ", c.MustNot.Select(Describe))}";

    [Fact]
    public void Sweep_EngineMatchesScalarLaw()
    {
        int configs = int.TryParse(Environment.GetEnvironmentVariable("MOTELY_SWEEP_CONFIGS"), out var n) ? n : 30;
        int seedCount = int.TryParse(Environment.GetEnvironmentVariable("MOTELY_SWEEP_SEEDS"), out var s) ? s : 200;
        int rngStart = int.TryParse(Environment.GetEnvironmentVariable("MOTELY_SWEEP_RNG"), out var g) ? g : 1;
        var seeds = RandomSeeds(seedCount, 12345);
        var failures = new List<string>();

        var log = Environment.GetEnvironmentVariable("MOTELY_SWEEP_LOG");
        for (int i = 0; i < configs; i++)
        {
            var r = new Random(rngStart + i);
            var config = RandomConfig(r, rngStart + i);
            Dictionary<string, Row> engine, engine4, oracle;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (log is not null)
                File.AppendAllText(log, $"#{rngStart + i} start {DescribeConfig(config)}\n");
            try
            {
                oracle = RunOracle(config, seeds);
                engine = RunEngine(config, seeds, 1);
                engine4 = RunEngine(config, seeds, 4);
                if (log is not null)
                    File.AppendAllText(log, $"#{rngStart + i} done {sw.ElapsedMilliseconds}ms oracle={oracle.Count} engine={engine.Count}\n");
            }
            catch (Exception ex)
            {
                failures.Add($"#{rngStart + i} THREW {ex.GetType().Name}: {ex.Message}\n  {DescribeConfig(config)}");
                continue;
            }

            var problems = new List<string>();
            var missed = oracle.Keys.Except(engine.Keys).ToList();
            var extra = engine.Keys.Except(oracle.Keys).ToList();
            if (missed.Count > 0)
                problems.Add($"MISSED {missed.Count} (e.g. {string.Join(",", missed.Take(3))})");
            if (extra.Count > 0)
                problems.Add($"FALSE-MATCH {extra.Count} (e.g. {string.Join(",", extra.Take(3))})");
            bool hasScore = config.Should.Count + config.MustNot.Count > 0 || !ClauseScoring.CanSkipMustReeval([.. config.Must]);
            foreach (var key in engine.Keys.Intersect(oracle.Keys))
            {
                if (!hasScore)
                    break;
                var a = engine[key];
                var b = oracle[key];
                if (a.Score != b.Score || !a.Tallies.SequenceEqual(b.Tallies))
                {
                    problems.Add($"SCORE {key}: engine {a.Score}/[{string.Join(",", a.Tallies)}] oracle {b.Score}/[{string.Join(",", b.Tallies)}]");
                    break;
                }
            }
            if (!engine.Keys.ToHashSet().SetEquals(engine4.Keys))
                problems.Add($"THREADS: 1t={engine.Count} 4t={engine4.Count}");
            else
                foreach (var key in engine.Keys)
                    if (engine[key].Score != engine4[key].Score || !engine[key].Tallies.SequenceEqual(engine4[key].Tallies))
                    {
                        problems.Add($"THREAD-SCORE {key}");
                        break;
                    }

            if (problems.Count > 0)
                failures.Add($"#{rngStart + i} oracle={oracle.Count} engine={engine.Count}: {string.Join(" | ", problems)}\n  {DescribeConfig(config)}");
        }

        foreach (var f in failures)
            output.WriteLine(f);
        Assert.True(failures.Count == 0, $"{failures.Count} failing configs:\n" + string.Join("\n", failures.Take(30)));
    }
}
