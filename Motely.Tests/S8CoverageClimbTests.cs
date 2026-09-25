using System.Runtime.Intrinsics;
using Motely.Filters.Jaml;
using Motely.SeedProviders;

namespace Motely.Tests;

public sealed class S8CoverageClimbTests
{
    private const string VoucherOverstock = """
        name: s8-voucher
        deck: Red
        stake: White
        must:
          - voucher: Overstock
            antes: [1]
        """;

    private const string StartingDrawAceHearts = """
        name: s8-starting
        deck: Red
        stake: White
        must:
          - startingDraw:
            rank: Ace
            suit: Hearts
            antes: [1]
        """;

    private const string PlanetPluto = """
        name: s8-planet
        deck: Red
        stake: White
        must:
          - planetCard: Pluto
            antes: [1]
        """;

    private const string PlanetPlutoPacks = """
        name: s8-planet-packs
        deck: Red
        stake: White
        must:
          - planetCard: Pluto
            antes: [1]
            sources:
              shopItems: [0, 1, 2, 3]
              boosterPacks: [0, 1]
        """;

    private static readonly string[] VoucherSeeds = ["5X5", "616", "696", "6J6", "7H7"];
    private static readonly string[] DrawSeeds = ["99", "CC", "F", "Q", "R", "VV"];
    private static readonly string[] PlanetSeeds = ["R", "H", "I", "Z", "88"];
    private static readonly string[] FixtureSeeds =
        ["ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7"];

    [Fact]
    public void Voucher_Overstock_KnownSeedsMatch() =>
        ProofSearch.MustMatchAll(VoucherOverstock, VoucherSeeds);

    [Fact]
    public void StartingDraw_AceHearts_KnownSeedsMatch() =>
        ProofSearch.MustMatchAll(StartingDrawAceHearts, DrawSeeds);

    [Fact]
    public void Planet_Pluto_KnownSeedsMatch() =>
        ProofSearch.MustMatchAll(PlanetPluto, PlanetSeeds);

    [Fact]
    public void Planet_NarrowedShopSlots_GateOutLateSlotSeeds()
    {
        ProofSearch.MustMatchAll(PlanetPluto, PlanetSeeds);

        var (matching, matched) = ProofSearch.ListMatch(PlanetPlutoPacks, PlanetSeeds);
        Assert.Equal(2L, matching);
        Assert.Equal(
            ["88", "I"],
            matched.OrderBy(static s => s, StringComparer.Ordinal).ToArray()
        );
        Assert.True(matched.Count < PlanetSeeds.Length, "narrowed sources must be a strict subset");
    }

    private static void RunClause(
        IJamlClause clause,
        string[] seeds,
        MotelyDeck deck = MotelyDeck.Red
    )
    {
        var config = new JamlConfig
        {
            Id = "s8-clause",
            Deck = deck,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);
        using var search = JamlSearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();
        Assert.True(search.TotalSeedsSearched >= 1);
    }

    [Fact]
    public void Voucher_HighRolls_ListRuns() =>
        RunClause(
            new VoucherClause
            {
                Vouchers = [MotelyVoucher.Overstock, MotelyVoucher.Grabber, MotelyVoucher.Telescope],
                Antes = [1, 2, 3, 4],
                Rolls = [0, 1, 2, 3],
                Min = 1,
                Max = 20,
            },
            FixtureSeeds
        );

    [Fact]
    public void StartingDraw_RankOnly_ListRuns() =>
        RunClause(
            new StartingDrawClause { Rank = MotelyStandardcardRank.Ace, Antes = [1] },
            DrawSeeds
        );

    [Fact]
    public void Planet_MultiTargetPacks_ListRuns() =>
        RunClause(
            new PlanetCardClause
            {
                Planets = [MotelyPlanetCard.Pluto, MotelyPlanetCard.Mercury],
                Antes = [1, 2],
                Min = 1,
                Max = 10,
                Sources = new PlanetSourceConfig
                {
                    ShopItems = [0, 1, 2, 3, 4, 5, 6, 7],
                    BoosterPacks = [0, 1, 2],
                },
            },
            FixtureSeeds.Concat(PlanetSeeds).Distinct().ToArray()
        );

    [Fact]
    public void Tarot_AllSources_ListRuns() =>
        RunClause(
            new TarotCardClause
            {
                Tarots = [MotelyTarotCard.Death, MotelyTarotCard.TheFool],
                Antes = [1, 2],
                Sources = new TarotCardSourceConfig
                {
                    ShopItems = [0, 1, 2, 3],
                    BoosterPacks = [0, 1],
                    Emperor = [0, 1],
                    PurpleSealOrEightBall = [0],
                    CharmTag = true,
                },
            },
            FixtureSeeds
        );

    [Fact]
    public void Spectral_GhostDeck_ListRuns() =>
        RunClause(
            new SpectralCardClause
            {
                Spectrals = [MotelySpectralCard.Familiar, MotelySpectralCard.Grim],
                Antes = [1, 2],
                Sources = new SpectralCardSourceConfig
                {
                    ShopItems = [0, 1, 2, 3],
                    BoosterPacks = [0, 1],
                },
            },
            FixtureSeeds,
            MotelyDeck.Ghost
        );

    [Fact]
    public void UncommonRare_EditionSources_ListRuns()
    {
        RunClause(
            new UncommonJokerClause
            {
                Jokers = [MotelyJokerUncommon.Mime, MotelyJokerUncommon.Fibonacci],
                Antes = [1, 2, 3],
                Edition = MotelyItemEdition.Foil,
                Sources = new JokerSourceConfig
                {
                    ShopItems = [0, 1, 2, 3, 4],
                    BoosterPacks = [0, 1],
                    UncommonShopJokers = [0, 1],
                },
            },
            FixtureSeeds
        );
        RunClause(
            new RareJokerClause
            {
                Jokers = [MotelyJokerRare.Blueprint, MotelyJokerRare.Brainstorm],
                Antes = [1, 2, 3, 4],
                Edition = MotelyItemEdition.Negative,
                Sources = new JokerSourceConfig
                {
                    ShopItems = [0, 1, 2, 3],
                    BoosterPacks = [0, 1],
                    RareShopJokers = [0],
                },
            },
            FixtureSeeds
        );
    }

    [Fact]
    public void Legendary_ListRuns()
    {
        RunClause(
            new LegendaryJokerClause
            {
                Jokers = [MotelyJoker.Canio, MotelyJoker.Perkeo],
                Antes = [1, 2, 3, 4],
            },
            FixtureSeeds
        );
        RunClause(
            new LegendaryJokerClause
            {
                Antes = [1, 2],
                Sources = new LegendaryJokerSourceConfig { SpectralPacks = [0, 1, 2] },
            },
            FixtureSeeds
        );
    }

    [Fact]
    public void BossTagStandard_ListRuns()
    {
        RunClause(
            new BossClause { Bosses = [MotelyBossBlind.TheHook, MotelyBossBlind.TheWall], Antes = [1, 2] },
            FixtureSeeds
        );
        RunClause(
            new TagClause { Tags = [MotelyTag.CharmTag, MotelyTag.SpeedTag], Antes = [1], Rolls = [0, 1] },
            FixtureSeeds
        );
        RunClause(
            new StandardCardClause
            {
                Rank = MotelyStandardcardRank.Ace,
                Antes = [1],
                Sources = new StandardCardSourceConfig
                {
                    ShopItems = [0, 1, 2, 3],
                    BoosterPacks = [0, 1],
                },
            },
            FixtureSeeds
        );
    }

    [Fact]
    public void EventsAndErratic_ListRuns()
    {
        RunClause(new MisprintMultClause { Mult = 1, Rolls = [0, 1], Min = 1 }, FixtureSeeds);
        RunClause(new WheelOfFortuneClause { Rolls = [0, 1], Min = 1 }, FixtureSeeds);
        RunClause(
            new ErraticRankClause { Rank = MotelyStandardcardRank.Ace, Antes = [1] },
            FixtureSeeds,
            MotelyDeck.Erratic
        );
        RunClause(
            new ErraticSuitClause { Suit = MotelyStandardcardSuit.Hearts, Antes = [1] },
            FixtureSeeds,
            MotelyDeck.Erratic
        );
    }

    private sealed class RecordingSink : IMotelyResultSink
    {
        public List<string> Seeds { get; } = [];
        public List<int> Scores { get; } = [];
        public bool Disposed { get; private set; }

        public void OnSeed(string seed)
        {
            var row = new MotelyScoredSeedResult();
            row.Reset(seed, 0);
            OnScored(in row);
        }

        public void Flush() { }

        public void OnScored(in MotelyScoredSeedResult tally)
        {
            Seeds.Add(tally.Seed);
            Scores.Add(tally.Score);
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void CompositeResultSink_ForwardsAndDisposes()
    {
        var a = new RecordingSink();
        var b = new RecordingSink();
        using (var composite = new CompositeMotelyResultSink([a, b]))
        {
            composite.OnSeed("SEED1");
            var scored = new MotelyScoredSeedResult();
            scored.Reset("SEED2", 42);
            composite.OnScored(in scored);
            composite.Flush();
        }
        Assert.Equal(["SEED1", "SEED2"], a.Seeds);
        Assert.Equal(["SEED1", "SEED2"], b.Seeds);
        Assert.Equal([0, 42], a.Scores);
        Assert.True(a.Disposed && b.Disposed);
    }

    [Fact]
    public void ChainedSeedProvider_FirstThenSecond()
    {
        var chained = new MotelyChainedSeedProvider(
            new MotelySeedListProvider(["A", "B"]),
            new MotelySeedListProvider(["C"])
        );
        Assert.Equal(3, chained.SeedCount);
        Assert.Equal("A", chained.NextSeed().ToString());
        Assert.Equal("B", chained.NextSeed().ToString());
        Assert.Equal("C", chained.NextSeed().ToString());
        Assert.True(chained.NextSeed().Length == 0);

        var batch = new MotelyChainedSeedProvider(
            new MotelySeedListProvider(["X"]),
            new MotelySeedListProvider(["Y", "Z"])
        );
        var buf = new string[3];
        Assert.Equal(3, batch.NextSeeds(buf));
        Assert.Equal("X", buf[0]);
        Assert.Equal("Y", buf[1]);
        Assert.Equal("Z", buf[2]);
    }

    private const string UncommonAnyRawStreams = """
        name: s8-uncommon-any-streams
        deck: Red
        stake: White
        must:
          - uncommonJoker: []
            antes: [1]
            sources:
              commonShopJokers: [0, 1]
              uncommonShopJokers: [0, 1]
              rareShopJokers: [0]
              allShopJokers: [0, 1]
        """;

    private const string UncommonAnyRawStreamsNegative = """
        name: s8-uncommon-any-negative
        deck: Red
        stake: White
        must:
          - uncommonJoker: []
            edition: Negative
            antes: [1]
            sources:
              commonShopJokers: [0, 1]
              uncommonShopJokers: [0, 1]
              rareShopJokers: [0]
              allShopJokers: [0, 1]
        """;

    private const string UncommonAnyPackExtension = """
        name: s8-uncommon-pack-extension
        deck: Red
        stake: White
        must:
          - uncommonJoker: []
            antes: [1]
            sources:
              boosterPacks: [0, 1, 2, 3, 4, 5]
        """;

    private const string UncommonAnyStickers = """
        name: s8-uncommon-stickers
        deck: Red
        stake: White
        must:
          - uncommonJoker: []
            stickers: [Eternal, Perishable, Rental]
            antes: [1]
            sources:
              uncommonShopJokers: [0, 1]
        """;

    [Fact]
    public void UncommonAny_RawStreams_MatchesAllSeeds() =>
        ProofSearch.MustMatchAll(UncommonAnyRawStreams, FixtureSeeds);

    [Fact]
    public void UncommonAny_RawStreams_NegativeEditionGatesAll() =>
        ProofSearch.MustMatchNone(UncommonAnyRawStreamsNegative, FixtureSeeds);

    [Fact]
    public void UncommonAny_Ante1PackExtension_PinnedMatches()
    {
        var (matching, matched) = ProofSearch.ListMatch(
            UncommonAnyPackExtension,
            FixtureSeeds
        );
        Assert.Equal(
            "616,696,6J6,MOTELY77,UNITTEST",
            string.Join(",", matched.OrderBy(static s => s, StringComparer.Ordinal))
        );
        Assert.Equal(matching, matched.Count);
    }

    [Fact]
    public void UncommonAny_Stickers_WhiteStakeMatchesNone() =>
        ProofSearch.MustMatchNone(UncommonAnyStickers, FixtureSeeds);

    [Fact]
    public void Uncommon_BadJokerName_FailsLoad()
    {
        Assert.False(
            JamlConfigLoader.TryLoad(
                """
                name: s8-uncommon-bad
                deck: Red
                stake: White
                must:
                  - uncommonJoker: NotARealJoker
                    antes: [1]
                """,
                out _,
                out var error
            )
        );
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RawStreams_VectorScalarBuilderParity()
    {
        static long Count(string sources)
        {
            var jaml = $"""
                name: s8-parity
                deck: Red
                stake: White
                must:
                  - uncommonJoker: []
                    antes: [1]
                    sources:
                {sources}
                """;
            var (matching, _) = ProofSearch.ListMatch(jaml, FixtureSeeds);
            return matching;
        }
        Assert.Equal(8, Count("      uncommonShopJokers: [0]"));
        Assert.Equal(3, Count("      allShopJokers: [0, 1]"));
        Assert.Equal(0, Count("      commonShopJokers: [0, 1]"));

        var rawDesc = new UncommonJokerFilterDesc(
            new UncommonJokerClause
            {
                Antes = [1],
                Sources = new JokerSourceConfig { UncommonShopJokers = [0] },
            }
        );
        using var rawSearch = new MotelySearchSettings<UncommonJokerFilterDesc.UncommonJokerFilter>(
            rawDesc
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        rawSearch.AwaitCompletion();
        Assert.Equal(FixtureSeeds.Length, (int)rawSearch.MatchingSeeds);
    }

    [Fact]
    public void TarotSources_KnownSeedCounts()
    {
        static long Count(string body)
        {
            var jaml = $"""
                name: s8-tarot-probe
                deck: Red
                stake: White
                must:
                {body}
                """;
            var (matching, _) = ProofSearch.ListMatch(jaml, FixtureSeeds);
            return matching;
        }
        long shopAny = Count("""
                  - tarotCard: [TheFool, TheMagician, TheHighPriestess, TheEmpress, TheEmperor, TheHierophant, TheLovers, TheChariot, Justice, TheHermit, TheWheelOfFortune, Strength, TheHangedMan, Death, Temperance, TheDevil, TheTower, TheStar, TheMoon, TheSun, Judgement, TheWorld]
                    antes: [1, 2]
                    sources:
                      shopItems: [0, 1, 2, 3]
                """);
        long packAny = Count("""
                  - tarotCard: [TheFool, TheMagician, TheHighPriestess, TheEmpress, TheEmperor, TheHierophant, TheLovers, TheChariot, Justice, TheHermit, TheWheelOfFortune, Strength, TheHangedMan, Death, Temperance, TheDevil, TheTower, TheStar, TheMoon, TheSun, Judgement, TheWorld]
                    antes: [1]
                    sources:
                      boosterPacks: [0, 1, 2, 3, 4, 5]
                """);
        long emperorAny = Count("""
                  - tarotCard: [TheFool, TheMagician, TheHighPriestess, TheEmpress, TheEmperor, TheHierophant, TheLovers, TheChariot, Justice, TheHermit, TheWheelOfFortune, Strength, TheHangedMan, Death, Temperance, TheDevil, TheTower, TheStar, TheMoon, TheSun, Judgement, TheWorld]
                    antes: [1]
                    sources:
                      emperor: [0, 1]
                """);
        long sealAny = Count("""
                  - tarotCard: [TheFool, TheMagician, TheHighPriestess, TheEmpress, TheEmperor, TheHierophant, TheLovers, TheChariot, Justice, TheHermit, TheWheelOfFortune, Strength, TheHangedMan, Death, Temperance, TheDevil, TheTower, TheStar, TheMoon, TheSun, Judgement, TheWorld]
                    antes: [1]
                    sources:
                      purpleSealOrEightBall: [0, 1]
                """);
        long charm = Count("""
                  - tarotCard: [TheFool, TheMagician, TheHighPriestess, TheEmpress, TheEmperor, TheHierophant, TheLovers, TheChariot, Justice, TheHermit, TheWheelOfFortune, Strength, TheHangedMan, Death, Temperance, TheDevil, TheTower, TheStar, TheMoon, TheSun, Judgement, TheWorld]
                    antes: [1, 2]
                    sources:
                      boosterPacks: [0, 1, 2, 3]
                      charmTag: true
                """);
        Assert.Equal(6, shopAny);
        Assert.Equal(4, packAny);
        Assert.Equal(8, emperorAny);
        Assert.Equal(8, sealAny);
        Assert.Equal(8, charm);
    }

    [Fact]
    public void SpectralSources_KnownSeedCounts()
    {
        static long Count(string body)
        {
            var jaml = $"""
                name: s8-spectral
                deck: Ghost
                stake: White
                must:
                {body}
                """;
            var (matching, _) = ProofSearch.ListMatch(jaml, FixtureSeeds);
            return matching;
        }
        const string AllContent =
            "[Familiar, Grim, Incantation, Talisman, Aura, Wraith, Sigil, Ouija, Ectoplasm, Immolate, Ankh, DejaVu, Hex, Trance, Medium, Cryptid]";
        long shop = Count($"""
                  - spectralCard: {AllContent}
                    antes: [1, 2, 3, 4]
                    sources:
                      shopItems: [0, 1, 2, 3, 4, 5, 6, 7]
                """);
        long packs = Count($"""
                  - spectralCard: {AllContent}
                    antes: [1]
                    sources:
                      boosterPacks: [0, 1, 2, 3, 4, 5]
                """);
        long sixthSense = Count($"""
                  - spectralCard: {AllContent}
                    antes: [1, 2]
                    sources:
                      sixthSense: [0, 1]
                """);
        long seance = Count($"""
                  - spectralCard: {AllContent}
                    antes: [1, 2]
                    sources:
                      seance: [0, 1]
                """);
        long ethereal = Count($"""
                  - spectralCard: {AllContent}
                    antes: [1, 2]
                    sources:
                      boosterPacks: [0, 1, 2, 3]
                      etherealTag: true
                """);
        var rawDesc = new SpectralCardFilterDesc(
            new SpectralCardClause
            {
                Spectrals =
                [
                    MotelySpectralCard.Familiar, MotelySpectralCard.Grim, MotelySpectralCard.Incantation,
                    MotelySpectralCard.Talisman, MotelySpectralCard.Aura, MotelySpectralCard.Wraith,
                    MotelySpectralCard.Sigil, MotelySpectralCard.Ouija, MotelySpectralCard.Ectoplasm,
                    MotelySpectralCard.Immolate, MotelySpectralCard.Ankh, MotelySpectralCard.DejaVu,
                    MotelySpectralCard.Hex, MotelySpectralCard.Trance, MotelySpectralCard.Medium,
                    MotelySpectralCard.Cryptid,
                ],
                Antes = [1, 2, 3, 4],
                Sources = new SpectralCardSourceConfig { ShopItems = [0, 1, 2, 3, 4, 5, 6, 7] },
            }
        );
        using var rawSearch = new MotelySearchSettings<SpectralCardFilterDesc.SpectralCardFilter>(
            rawDesc
        )
            .WithDeck(MotelyDeck.Ghost)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        rawSearch.AwaitCompletion();
        long rawShop = rawSearch.MatchingSeeds;

        using var addSearch = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithAdditionalFilter(rawDesc)
            .WithDeck(MotelyDeck.Ghost)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        addSearch.AwaitCompletion();
        long addShop = addSearch.MatchingSeeds;

        Assert.Equal(7, shop);
        Assert.Equal(1, packs);
        Assert.Equal(8, sixthSense);
        Assert.Equal(8, seance);
        Assert.Equal(8, ethereal);
        Assert.Equal(7, rawShop);
        Assert.Equal(7, addShop);
    }

    private struct ScalarProbeDesc : IMotelySeedFilterDesc<ScalarProbeDesc.ScalarProbeFilter>
    {
        public static readonly List<string> Log = [];
        public static SpectralCardClause? Clause;

        public readonly ScalarProbeFilter CreateFilter(ref MotelyFilterCreationContext ctx) =>
            new();

        public struct ScalarProbeFilter : IMotelySeedFilter
        {
            public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
            {
                return ctx.SearchIndividualSeeds(
                    (MotelySingleSearchContext single) =>
                    {
                        var shopStream = single.CreateShopItemStream(2);
                        var items = new List<string>();
                        for (int slot = 0; slot < 4; slot++)
                            items.Add(single.GetNextShopItem(ref shopStream).Type.ToString());
                        bool meets = JamlScoring.ClauseMeetsMinForFilter(ref single, ScalarProbeDesc.Clause!);
                        ScalarProbeDesc.Log.Add($"sigil0={items[0] == "Sigil"} meets={meets}");
                        return 0;
                    }
                );
            }
        }
    }

    [Fact]
    public void AleebGhostShop_ScalarSeesSigil_GroundTruth()
    {
        ScalarProbeDesc.Log.Clear();
        ScalarProbeDesc.Clause = new SpectralCardClause
        {
            Spectrals =
            [
                MotelySpectralCard.Familiar, MotelySpectralCard.Grim, MotelySpectralCard.Incantation,
                MotelySpectralCard.Talisman, MotelySpectralCard.Aura, MotelySpectralCard.Wraith,
                MotelySpectralCard.Sigil, MotelySpectralCard.Ouija, MotelySpectralCard.Ectoplasm,
                MotelySpectralCard.Immolate, MotelySpectralCard.Ankh, MotelySpectralCard.DejaVu,
                MotelySpectralCard.Hex, MotelySpectralCard.Trance, MotelySpectralCard.Medium,
                MotelySpectralCard.Cryptid,
            ],
            Antes = [1, 2, 3, 4],
            Sources = new SpectralCardSourceConfig { ShopItems = [0, 1, 2, 3, 4, 5, 6, 7] },
        };
        using var search = new MotelySearchSettings<ScalarProbeDesc.ScalarProbeFilter>(
            new ScalarProbeDesc()
        )
            .WithDeck(MotelyDeck.Ghost)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(["ALEEB"], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();
        Assert.Equal("sigil0=True meets=True", string.Join(" | ", ScalarProbeDesc.Log));
    }

    private static readonly string[] WideSeeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF",
    ];

    [Fact]
    public void LegendarySoul_KnownSeedCounts_MultiBatch()
    {
        static long Count(string body)
        {
            var jaml = $"""
                name: s8-legendary
                deck: Red
                stake: White
                must:
                {body}
                """;
            var (matching, _) = ProofSearch.ListMatch(jaml, WideSeeds);
            return matching;
        }
        long split = Count("""
                  - legendaryJoker: []
                    antes: [1, 2]
                    sources:
                      arcanaPacks: [0, 1, 2, 3]
                      spectralPacks: [0, 1, 2, 3]
                """);
        long soulOnly = Count("""
                  - legendaryJoker: []
                    soulCardOnly: true
                    antes: [1, 2]
                    sources:
                      arcanaPacks: [0, 1, 2, 3]
                      spectralPacks: [0, 1, 2, 3]
                """);
        long mega = Count("""
                  - legendaryJoker: []
                    soulCardOnly: true
                    antes: [1, 2]
                    sources:
                      arcanaPacks: [0, 1, 2, 3]
                      spectralPacks: [0, 1, 2, 3]
                      requireMegaPack: true
                """);
        long legacy = Count("""
                  - legendaryJoker: []
                    antes: [1, 2]
                    sources:
                      boosterPacks: [0, 1, 2, 3]
                """);
        long perkeo = Count("""
                  - legendaryJoker: Perkeo
                    antes: [1, 2]
                    sources:
                      arcanaPacks: [0, 1, 2, 3]
                      spectralPacks: [0, 1, 2, 3]
                """);
        long theSoulClause = Count("""
                  - spectralCard: TheSoul
                    antes: [1, 2]
                """);
        long ante0 = Count("""
                  - voucher: Hieroglyph
                    antes: [1]
                  - legendaryJoker: Perkeo
                    antes: [0]
                """);
        Assert.Equal(8, split);
        Assert.Equal(8, soulOnly);
        Assert.Equal(0, mega);
        Assert.Equal(8, legacy);
        Assert.Equal(0, perkeo);
        Assert.Equal(8, theSoulClause);
        Assert.Equal(0, ante0);
    }

    private static readonly string[] NegativeLegendaryAnte12Seeds =
    [
        "ACA1C895",
        "AGA7G779",
        "AHA8H549",
        "AHA9H739",
        "AIA7I647",
        "AJA2J169",
        "AJA7J641",
    ];

    private const string NegativeLegendaryAnte12 = """
        name: s8-negative-legendary
        deck: Red
        stake: White
        must:
          - legendaryJoker: []
            edition: Negative
            antes: [1, 2]
        """;

    [Fact]
    public void NegativeLegendaryAnte12_ExactJamlRoute_MatchesRealSeeds()
    {
        ProofSearch.MustMatchAll(NegativeLegendaryAnte12, NegativeLegendaryAnte12Seeds);
    }

    [Fact]
    public void NegativeLegendarySimdFront_IsASupersetOfTheExactRoute()
    {
        var matched = new List<string>();
        using var search = new MotelySearchSettings<NegativeLegendaryJokerSimdFilterDesc.FilterStruct>(
            new NegativeLegendaryJokerSimdFilterDesc()
        )
            .WithAdditionalFilter(new LegendaryJokerShopSoulFilterDesc())
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(NegativeLegendaryAnte12Seeds, NegativeLegendaryAnte12Seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(matched.Add)
            .Start();
        search.AwaitCompletion();

        var dropped = NegativeLegendaryAnte12Seeds.Except(matched).ToArray();
        Assert.True(
            dropped.Length == 0,
            $"Prefilter dropped seeds the exact route accepts: {string.Join(", ", dropped)}"
        );
    }

    [Fact]
    public void VectorMask_And_VectorUtils_ShiftLeft()
    {
        var a = VectorMask.AllBitsSet;
        var b = VectorMask.NoBitsSet;
        Assert.True(a.IsAllTrue());
        Assert.True(b.IsAllFalse());
        Assert.Equal(0u, (a & b).Value);
        Assert.Equal(0xFFu, (a | b).Value);
        Assert.True(new VectorMask(0x0F).IsPartiallyTrue());

        var value = Vector256.Create(1, 2, 3, 4, 5, 6, 7, 8);
        var shift = Vector256.Create(1, 1, 1, 1, 1, 1, 1, 1);
        var shifted = MotelyVectorUtils.ShiftLeft(value, shift);
        for (int i = 0; i < 8; i++)
            Assert.Equal(value[i] << 1, shifted[i]);
    }
}
