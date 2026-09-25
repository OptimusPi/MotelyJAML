using System.ComponentModel;
using System.Runtime.Intrinsics;
using Motely.Filters;
using Motely.Filters.Jaml;
using Motely.Filters.Native;

namespace Motely.Tests;

public sealed class S8P2SearchGutsTests
{
    private const string PermissiveJaml = """
        name: s8p2-permissive
        deck: Red
        stake: White
        must:
          - joker: []
            antes: [1]
        """;

    private static readonly string[] FixtureSeeds =
        ["ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7"];

    private static JamlConfig Permissive() => ProofSearch.LoadOrThrow(PermissiveJaml);

    [Fact]
    public void SettingsInterfaceChain_RoundTripsEveryKnob()
    {
        var concrete = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        );
        IMotelySearchSettings s = concrete;

        var progress = new List<MotelyProgress>();
        var scored = new List<MotelyScoredSeedResult>();
        s = s.WithThreadCount(2)
            .WithBatchCharacterCount(2)
            .WithStartBatchIndex(3)
            .WithEndBatchIndex(9)
            .WithDeck(MotelyDeck.Ghost)
            .WithStake(MotelyStake.Gold)
            .WithProgressCallback(progress.Add)
            .WithProgressReportIntervalMs(-5)
            .WithCsvOutput(true)
            .WithQuietMode(true)
            .WithSeedMatchCallback(_ => { })
            .WithScoredResultCallback(scored.Add)
            .WithAutoScoreCutoff(true)
            .StopAfter(4)
            .WithSequentialSearch();

        Assert.Same(concrete, s);
        Assert.Same(concrete.BaseFilterDesc, s.BaseFilterDescBase);
        Assert.Equal(2, concrete.ThreadCount);
        Assert.Equal(2, concrete.SequentialBatchCharacterCount);
        Assert.Equal(3, concrete.StartBatchIndex);
        Assert.Equal(9, concrete.EndBatchIndex);
        Assert.Equal(MotelyDeck.Ghost, concrete.Deck);
        Assert.Equal(MotelyStake.Gold, concrete.Stake);
        Assert.Equal(0, concrete.ProgressReportIntervalMs);
        Assert.True(concrete.CsvOutput);
        Assert.True(concrete.QuietMode);
        Assert.True(concrete.AutoScoreCutoff);
        Assert.Equal(4, concrete.StopAfterMatches);
        Assert.Equal(MotelySearchMode.Sequential, concrete.Mode);
        Assert.Null(concrete.SeedProvider);

        s = s.WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length);
        Assert.Equal(MotelySearchMode.Provider, concrete.Mode);
        Assert.NotNull(concrete.SeedProvider);
    }

    [Fact]
    public void SettingsInvalidMode_ThrowsFromSearchConstructor()
    {
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
        {
            Mode = (MotelySearchMode)42,
        };
        Assert.Throws<InvalidEnumArgumentException>(() => settings.CreateSearch());
    }

    [Fact]
    public void SearchCannotBeStartedTwice()
    {
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .CreateSearch();
        search.Start();
        Assert.Throws<InvalidOperationException>(() => search.Start());
        search.AwaitCompletion();
    }

    [Fact]
    public void Start_IsNonBlocking_IsCompletedFalseUntilWorkersFinish()
    {
        using var gate = new ManualResetEventSlim(false);
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSequentialSearch()
            .WithBatchCharacterCount(2)
            .WithStartBatchIndex(0)
            .WithEndBatchIndex(1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(_ =>
            {
                gate.Wait();
                return 1;
            })
            .Start();

        Assert.False(
            search.IsCompleted,
            "Start must return before workers finish — hosts poll or await completion"
        );

        gate.Set();
        search.AwaitCompletion();
        Assert.True(search.IsCompleted);
        Assert.Equal(35L * 35, search.TotalSeedsSearched);
        Assert.Equal(search.TotalSeedsSearched, search.MatchingSeeds);
    }

    [Fact]
    public async Task SequentialSlice_EtaCountsOnlyTheBatchesTheRunAskedFor()
    {
        var progress = new List<MotelyProgress>();
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(10)
            .WithEndBatchIndex(12)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithProgressCallback(p =>
            {
                lock (progress)
                    progress.Add(p);
            })
            .WithProgressReportIntervalMs(0)
            .CreateSearch();

        var task = search.RunSearchAsync();
        await search.WaitForCompletionAsync();
        await task;

        Assert.Equal(2, progress.Count);

        Assert.Equal(
            progress[0].ElapsedMilliseconds,
            progress[0].EstimatedTimeRemainingMilliseconds
        );

        Assert.Equal(0L, progress[^1].EstimatedTimeRemainingMilliseconds);

        Assert.Equal(12L, search.CompletedBatchCount);
    }

    [Fact]
    public async Task SequentialSlice_ProgressCountersAndAsyncCompletion()
    {
        var progress = new List<MotelyProgress>();
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSequentialSearch()
            .WithBatchCharacterCount(3)
            .WithStartBatchIndex(0)
            .WithEndBatchIndex(2)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithProgressCallback(p => { lock (progress) progress.Add(p); })
            .WithProgressReportIntervalMs(0)
            .CreateSearch();

        var task = search.RunSearchAsync();
        await search.WaitForCompletionAsync();
        await task;

        Assert.True(search.IsCompleted);
        Assert.True(search.IsSequentialBatchSearch);
        Assert.False(search.StoppedOnMatchLimit);

        Assert.Equal(2L * 35 * 35 * 35, search.TotalSeedsSearched);
        Assert.Equal(2L, search.CompletedBatchCount);
        Assert.True(search.MatchingSeeds > 0, "permissive filter found nothing in the slice");
        Assert.Equal(search.TotalSeedsSearched - search.MatchingSeeds, search.FilteredSeeds);
        Assert.True(search.ElapsedMs >= 0);

        Assert.Equal(2, progress.Count);
        Assert.All(progress, p => Assert.InRange(p.PercentComplete, 0.0, 100.0));
        Assert.Equal(search.TotalSeedsSearched, progress[^1].SeedsSearched);
    }

    [Fact]
    public void ProviderList_ProgressReachesOneHundredPercent()
    {
        string[] seeds =
        [
            "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
            "99", "CC", "F", "Q", "R", "VV", "H", "I",
            "Z", "88", "AAAAAAAA", "MOTELY", "474", "3X3", "GHG", "4C4",
        ];
        var progress = new List<MotelyProgress>();
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithProgressCallback(progress.Add)
            .WithProgressReportIntervalMs(0)
            .Start();
        search.AwaitCompletion();

        Assert.False(search.IsSequentialBatchSearch);
        Assert.Equal(seeds.Length, (int)search.TotalSeedsSearched);
        Assert.True(progress.Count >= 1, "at least one report batch for a non-empty list");
        Assert.Equal(100.0, progress[^1].PercentComplete, 3);
        Assert.True(search.CompletedBatchCount >= 1);
    }

    [Fact]
    public void ProviderList_SmallReportBatch_EmitsMultipleProgressTicks()
    {
        string[] seeds =
        [
            "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
            "99", "CC", "F", "Q", "R", "VV", "H", "I",
            "Z", "88", "AAAAAAAA", "MOTELY", "474", "3X3", "GHG", "4C4",
        ];
        var progress = new List<MotelyProgress>();
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSeedGenerator(seeds, seeds.Length)
            .WithProviderBatchSeedCount(MotelyGlobals.MaxVectorWidth)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithProgressCallback(progress.Add)
            .WithProgressReportIntervalMs(0)
            .Start();
        search.AwaitCompletion();

        Assert.Equal(seeds.Length, (int)search.TotalSeedsSearched);
        Assert.InRange(progress.Count, 3, 24);
        Assert.Equal(100.0, progress[^1].PercentComplete, 3);
    }

    private struct ThrowingFilterDesc : IMotelySeedFilterDesc<ThrowingFilterDesc.ThrowingFilter>
    {
        public readonly ThrowingFilter CreateFilter(ref MotelyFilterCreationContext ctx) => new();

        public struct ThrowingFilter : IMotelySeedFilter
        {
            public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx) =>
                throw new InvalidDataException("s8p2 worker boom");
        }
    }

    [Fact]
    public async Task WorkerException_SurfacesThroughCompletionTask()
    {
        using var search = new MotelySearchSettings<ThrowingFilterDesc.ThrowingFilter>(
            new ThrowingFilterDesc()
        )
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .CreateSearch();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => search.RunSearchAsync());
        Assert.Equal("s8p2 worker boom", ex.Message);
    }

    private sealed class CountingAnalyzeDesc
        : IMotelySeedAnalyzeDesc<CountingAnalyzeDesc.CountingAnalyzeProvider>
    {
        public int LanesSeen;

        public CountingAnalyzeProvider CreateAnalyzeProvider(
            ref MotelyFilterCreationContext ctx
        ) => new(this);

        public readonly struct CountingAnalyzeProvider(CountingAnalyzeDesc owner)
            : IMotelySeedAnalyzeProvider
        {
            public void Analyze(
                ref MotelyVectorSearchContext ctx,
                VectorMask reportedMask,
                Motely.Filters.MotelyScoredSeedResult[]? scores
            )
            {
                for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
                    if (reportedMask[lane])
                        owner.LanesSeen++;
            }
        }
    }

    private sealed class CountingRouterDesc
        : IMotelySeedRouterDesc<CountingRouterDesc.CountingRouter>
    {
        public readonly List<string> RoutedSeeds = [];

        public CountingRouter CreateSeedRouter(ref MotelyFilterCreationContext ctx) => new(this);

        public readonly struct CountingRouter(CountingRouterDesc owner) : IMotelySeedRouter
        {
            public void InjectSingleSeedContext(in MotelySingleSearchContext ctx)
            {
                owner.RoutedSeeds.Add(ctx.GetSeed());
            }
        }
    }

    [Fact]
    public void AnalyzeAndRouterDescs_AreCreatedAndRouterReceivesEverySeed()
    {
        var analyzeDesc = new CountingAnalyzeDesc();
        var routerDesc = new CountingRouterDesc();

        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithSeedAnalyzeProvider(analyzeDesc)
            .WithSeedRouter(routerDesc)
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true);

        using var search = (MotelySearch<PassthroughFilterDesc.PassthroughFilter>)
            settings.CreateSearch();

        Assert.True(search.TryGetAnalyzeProvider(out var analyzeProvider));
        Assert.True(search.TryGetSingleSeedRouter(out _));
        Assert.False(search.TryGetScoreProvider(out _));

        search.Start();
        search.AwaitCompletion();

        Assert.Equal(
            FixtureSeeds.OrderBy(s => s, StringComparer.Ordinal),
            routerDesc.RoutedSeeds.OrderBy(s => s, StringComparer.Ordinal)
        );

        Assert.NotNull(analyzeProvider);
    }

    [Fact]
    public void SearchWithoutOptionalProviders_TryGettersReportAbsence()
    {
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithSeedGenerator(["ALEEB"], 1)
            .WithThreadCount(1)
            .WithQuietMode(true);
        using var search = (MotelySearch<PassthroughFilterDesc.PassthroughFilter>)
            settings.CreateSearch();
        Assert.False(search.TryGetAnalyzeProvider(out _));
        Assert.False(search.TryGetSingleSeedRouter(out _));
        search.Start();
        search.AwaitCompletion();
    }

    [Fact]
    public void RandomSearch_SearchesExactlyTheRequestedCount()
    {
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithRandomSearch(40)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();
        Assert.Equal(40L, search.TotalSeedsSearched);
    }

    [Fact]
    public void AestheticSearch_StopsOnFirstMatch()
    {
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithAestheticSearch(JamlAesthetic.Palindrome)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .StopAfter(1)
            .Start();
        search.AwaitCompletion();
        Assert.True(search.MatchingSeeds >= 1, "no palindrome seed matched a permissive filter");
        Assert.True(search.StoppedOnMatchLimit);
    }

    [Fact]
    public void SearchIntent_AppliesBoundedAestheticSearchThroughSettings()
    {
        var intent = new MotelySearchIntent(
            Mode: MotelySearchInputMode.Aesthetic,
            Aesthetic: JamlAesthetic.Palindrome,
            ThreadCount: 1,
            StopAfterMatches: 1
        );

        using var search = intent.ApplyTo(JamlSearchBuilder.CreateSettings(Permissive()))
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();

        Assert.True(search.MatchingSeeds >= 1, "no palindrome seed matched a permissive filter");
        Assert.True(search.StoppedOnMatchLimit);
    }

    [Fact]
    public void SearchIntent_AppliesBoundedKeywordSearchThroughSettings()
    {
        var intent = new MotelySearchIntent(
            Mode: MotelySearchInputMode.Keyword,
            Keywords: ["ALEEB"],
            PaddingAlphabet: "1",
            ThreadCount: 1,
            StopAfterMatches: 1
        );

        using var search = intent.ApplyTo(JamlSearchBuilder.CreateSettings(Permissive()))
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();

        Assert.True(search.MatchingSeeds >= 1, "no keyword seed matched a permissive filter");
        Assert.True(search.StoppedOnMatchLimit);
    }

    [Fact]
    public void AutoScoreCutoff_ReportsCandidatesWhileDisengaged()
    {
        const string jaml = """
            name: s8p2-should
            deck: Red
            stake: White
            must:
              - joker: []
                antes: [1]
            should:
              - voucher: Overstock
                antes: [1]
            """;
        var scored = new List<MotelyScoredSeedResult>();
        using var search = JamlSearchBuilder
            .CreateSettings(ProofSearch.LoadOrThrow(jaml))
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithAutoScoreCutoff(true)
            .WithScoredResultCallback(scored.Add)
            .Start();
        search.AwaitCompletion();

        Assert.Equal(search.MatchingSeeds, scored.Count);
        Assert.True(scored.Count > 0);
        Assert.Contains(scored, r => r.Score > scored.Min(x => x.Score));
    }

    private static HashSet<int> Lengths(Action<MotelyFilterCreationContext> record)
    {
        var ctx = new MotelyFilterCreationContext();
        record(ctx);
        return [.. ctx.CachedPseudohashKeyLengths];
    }

    [Fact]
    public void CreationContext_DefaultCtorUsesRedWhite()
    {
        var ctx = new MotelyFilterCreationContext();
        Assert.Equal(MotelyDeck.Red, ctx.Deck);
        Assert.Equal(MotelyStake.White, ctx.Stake);
        Assert.Equal([0], ctx.CachedPseudohashKeyLengths);
    }

    [Fact]
    public void CreationContext_VoucherAndResampleKeys()
    {
        string key = MotelyPrngKeys.Voucher + 1;
        var lengths = Lengths(ctx => ctx.CacheAnteFirstVoucher(1));
        Assert.Contains(key.Length, lengths);
        Assert.Contains((key + MotelyPrngKeys.Resample + "X").Length, lengths);
    }

    [Fact]
    public void CreationContext_RemoveCachedPseudoHash_BothOverloads()
    {
        var ctx = new MotelyFilterCreationContext();
        ctx.CachePseudoHash("abcd");
        Assert.Contains(4, ctx.CachedPseudohashKeyLengths);
        ctx.RemoveCachedPseudoHash("abcd");
        Assert.DoesNotContain(4, ctx.CachedPseudohashKeyLengths);
        ctx.CachePseudoHash(7);
        ctx.RemoveCachedPseudoHash(7);
        Assert.DoesNotContain(7, ctx.CachedPseudohashKeyLengths);
    }

    [Fact]
    public void CreationContext_AdditionalFilterSkipsUnforcedCaches()
    {
        var ctx = new MotelyFilterCreationContext { IsAdditionalFilter = true };
        ctx.CachePseudoHash(11);
        Assert.DoesNotContain(11, ctx.CachedPseudohashKeyLengths);
        ctx.CachePseudoHash(11, force: true);
        Assert.Contains(11, ctx.CachedPseudohashKeyLengths);
    }

    [Fact]
    public void CreationContext_TarotPlanetSpectralFamilies()
    {
        var arcana = Lengths(ctx => ctx.CacheArcanaPackTarotStream(2));
        string arcanaKey = MotelyPrngKeys.Tarot + MotelyPrngKeys.ArcanaPackItemSource + 2;
        Assert.Contains(arcanaKey.Length, arcana);
        Assert.Contains(
            (MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + 2).Length,
            arcana
        );

        var shopTarot = Lengths(ctx => ctx.CacheShopTarotStream(2));
        string shopTarotKey = MotelyPrngKeys.Tarot + MotelyPrngKeys.ShopItemSource + 2;
        Assert.Contains(shopTarotKey.Length, shopTarot);

        var celestial = Lengths(ctx => ctx.CacheCelestialPackPlanetStream(3));
        string celestialKey =
            MotelyPrngKeys.Planet + MotelyPrngKeys.CelestialPackItemSource + 3;
        Assert.Contains(celestialKey.Length, celestial);
        Assert.Contains(
            (MotelyPrngKeys.PlanetBlackHole + MotelyPrngKeys.Planet + 3).Length,
            celestial
        );

        var spectralPack = Lengths(ctx => ctx.CacheSpectralPackSpectralStream(1));
        Assert.Contains(
            (MotelyPrngKeys.Spectral + MotelyPrngKeys.SpectralPackItemSource + 1).Length,
            spectralPack
        );
        Assert.Contains(
            (MotelyPrngKeys.SpectralSoulBlackHole + MotelyPrngKeys.Spectral + 1).Length,
            spectralPack
        );

        var shopSpectral = Lengths(ctx => ctx.CacheShopSpectralStream(1));
        Assert.Contains(
            (MotelyPrngKeys.Spectral + MotelyPrngKeys.ShopItemSource + 1).Length,
            shopSpectral
        );
    }

    [Fact]
    public void CreationContext_StandardPackFlagsGateTheirKeys()
    {
        var full = Lengths(ctx => ctx.CacheStandardPackStream(1));
        Assert.Contains((MotelyPrngKeys.StandardCardEdition + 1).Length, full);
        Assert.Contains((MotelyPrngKeys.StandardCardHasSeal + 1).Length, full);
        Assert.Contains((MotelyPrngKeys.StandardCardHasEnhancement + 1).Length, full);

        var bare = Lengths(ctx =>
            ctx.CacheStandardPackStream(
                1,
                MotelyStandardCardStreamFlags.ExcludeEnhancement
                    | MotelyStandardCardStreamFlags.ExcludeEdition
                    | MotelyStandardCardStreamFlags.ExcludeSeal
            )
        );
        Assert.DoesNotContain((MotelyPrngKeys.StandardCardEdition + 1).Length, bare);
        Assert.DoesNotContain((MotelyPrngKeys.StandardCardHasSeal + 1).Length, bare);
    }

    [Fact]
    public void CreationContext_ShopAndTagAndErraticKeys()
    {
        var shop = Lengths(ctx => ctx.CacheShopStream(1));
        Assert.Contains((MotelyPrngKeys.ShopItemType + 1).Length, shop);

        var tags = Lengths(ctx => ctx.CacheTagStream(4));
        Assert.Contains((MotelyPrngKeys.Tags + 4).Length, tags);

        var packs = Lengths(ctx => ctx.CacheBoosterPackStream(2));
        Assert.Contains((MotelyPrngKeys.ShopPack + 2).Length, packs);

        var erratic = Lengths(ctx => ctx.CacheErraticDeckPrngStream());
        Assert.Contains(MotelyPrngKeys.DeckErratic.Length, erratic);
    }

    [Fact]
    public void CreationContext_GoldStakeCachesStickerStreams()
    {
        var parameters = new MotelySearchParameters
        {
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.Gold,
        };
        var ctx = new MotelyFilterCreationContext(in parameters);
        Assert.Equal(MotelyStake.Gold, ctx.Stake);
        ctx.CacheShopJokerStream(1);
        Assert.Contains(
            (MotelyPrngKeys.DefaultJokerEternalPerishableSource + 1).Length,
            ctx.CachedPseudohashKeyLengths
        );
        Assert.Contains(
            (MotelyPrngKeys.DefaultJokerRentalSource + 1).Length,
            ctx.CachedPseudohashKeyLengths
        );

        var fixedRarity = new MotelyFilterCreationContext(in parameters);
        fixedRarity.CacheLegendaryJokerStream(2);
        fixedRarity.CacheCommonShopJokerStream(2);
        fixedRarity.CacheUncommonShopJokerStream(2);
        fixedRarity.CacheRareShopJokerStream(2);
        Assert.Contains(
            MotelyPrngKeys
                .FixedRarityJoker(MotelyJokerRarity.Rare, MotelyPrngKeys.ShopItemSource, 2)
                .Length,
            fixedRarity.CachedPseudohashKeyLengths
        );
    }

    private struct VoucherParityDesc : IMotelySeedFilterDesc<VoucherParityDesc.VoucherParityFilter>
    {
        public static readonly List<string> Mismatches = [];
        public static int LanesCompared;

        public readonly VoucherParityFilter CreateFilter(ref MotelyFilterCreationContext ctx)
        {
            ctx.CacheAnteFirstVoucher(1);
            return new();
        }

        public struct VoucherParityFilter : IMotelySeedFilter
        {
            public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
            {
                var stateless = ctx.GetAnteFirstVoucher(1);
                var freshState = new MotelyVectorRunState();
                var stateful = ctx.GetAnteFirstVoucher(1, freshState);

                var stream = ctx.CreateVoucherStream(1);
                _ = stream.CreateSingleStream(0);

                for (int lane = 0; lane < Vector256<int>.Count; lane++)
                {
                    LanesCompared++;
                    if (stateless[lane] != stateful[lane])
                        Mismatches.Add($"lane{lane}: {stateless[lane]} != {stateful[lane]}");
                }
                return VectorMask.AllBitsSet;
            }
        }
    }

    [Fact]
    public void VoucherStatelessOverload_MatchesFreshStateOverload()
    {
        VoucherParityDesc.Mismatches.Clear();
        VoucherParityDesc.LanesCompared = 0;
        using var search = new MotelySearchSettings<VoucherParityDesc.VoucherParityFilter>(
            new VoucherParityDesc()
        )
            .WithSeedGenerator(FixtureSeeds, FixtureSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .Start();
        search.AwaitCompletion();

        Assert.True(VoucherParityDesc.LanesCompared >= FixtureSeeds.Length);
        Assert.Empty(VoucherParityDesc.Mismatches);
        Assert.Equal(FixtureSeeds.Length, (int)search.MatchingSeeds);
    }
}
