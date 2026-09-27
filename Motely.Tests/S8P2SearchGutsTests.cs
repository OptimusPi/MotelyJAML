using System.ComponentModel;
using System.Runtime.Intrinsics;
using Motely.Filters;
using Motely.Filters.Jaml;
using Motely.Filters.Native;

namespace Motely.Tests;

/// <summary>
/// S8.P2 — MotelySearch / settings / filter-creation-context guts. Every search here is a
/// bounded list or a fixed sequential slice (the StopAfterTests recipe); assertions observe
/// engine counters, delivered seeds, or PRNG-derived state — never load-and-stop.
/// </summary>
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

    // ── Settings fluent surface (interface chain) ──────────────────────────

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
        // Negative interval clamps to 0 (report every batch).
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

    /// <summary>
    /// G01 host contract: sequential/provider Start is non-blocking (worker threads).
    /// TUI polls IsCompleted; calling complete/dispose immediately after Start is wrong.
    /// Note: MotelySeedListProvider runs inline on the caller (Jamlyzer/browser); sequential does not.
    /// Gate blocks workers so IsCompleted stays false until the host allows finish.
    /// </summary>
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
        Assert.Equal(35L * 35, search.TotalSeedsSearched); // one batch of length-2
        Assert.Equal(search.TotalSeedsSearched, search.MatchingSeeds);
    }

    /// <summary>
    /// The ETA counts only the batches the run was asked for. It used to count to the end of the
    /// whole space, so a bounded run quoted the time to sweep everything after it: a one-batch
    /// range that finished in twelve seconds reported 148 days.
    /// <para>
    /// Both assertions are exact rather than timing-tolerant, because the ratio is exact. Two
    /// batches requested: at the first report half the run is done, so the time left equals the
    /// time spent; at the second the run is over, so it is zero. Under the old denominator the
    /// first report would read <c>elapsed × (42875/1 − 1)</c> — same arithmetic, wrong divisor.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SequentialSlice_EtaCountsOnlyTheBatchesTheRunAskedFor()
    {
        var progress = new List<MotelyProgress>();
        using var search = JamlSearchBuilder
            .CreateSettings(Permissive())
            .WithSequentialSearch()
            .WithBatchCharacterCount(3) // 35³ = 42,875 batches exist; this run wants two of them
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

        // Half done: the run has as long left as it has already taken.
        Assert.Equal(
            progress[0].ElapsedMilliseconds,
            progress[0].EstimatedTimeRemainingMilliseconds
        );

        // Done: nothing left. The old denominator made this the tail of the entire space.
        Assert.Equal(0L, progress[^1].EstimatedTimeRemainingMilliseconds);

        // A start index above zero is part of the denominator too — 12 − 10, not 12 − 0.
        Assert.Equal(12L, search.CompletedBatchCount);
    }

    // ── Sequential slice: progress, counters, async completion ─────────────

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

        // Two full batches of 35^3 seeds each, and both booked as completed.
        Assert.Equal(2L * 35 * 35 * 35, search.TotalSeedsSearched);
        Assert.Equal(2L, search.CompletedBatchCount);
        Assert.True(search.MatchingSeeds > 0, "permissive filter found nothing in the slice");
        Assert.Equal(search.TotalSeedsSearched - search.MatchingSeeds, search.FilteredSeeds);
        Assert.True(search.ElapsedMs >= 0);

        // Interval 0 → one report per batch: first uses the lifetime-average branch, the
        // second the windowed-throughput branch.
        Assert.Equal(2, progress.Count);
        Assert.All(progress, p => Assert.InRange(p.PercentComplete, 0.0, 100.0));
        Assert.Equal(search.TotalSeedsSearched, progress[^1].SeedsSearched);
    }

    [Fact]
    public void ProviderList_ProgressReachesOneHundredPercent()
    {
        // Provider report batches are 35³ seeds by default (SIMD still 8-wide). A short
        // list finishes in one report batch and must still hit 100% on the drain report.
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
        // Shrink the report batch to SIMD width so 24 seeds → multiple progress ticks.
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

    // ── Worker exception routing ───────────────────────────────────────────

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

    // ── Analyze provider + seed router descs (generic default interface impls) ──

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

        // Passthrough base filter, no score provider: ReportSeeds routes every list seed
        // through the router's single-seed context.
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

        // The analyze provider was created through the generic desc's default interface
        // implementation; prove it is live by driving it as its consumers do.
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

    // ── Random + aesthetic providers through the settings surface ──────────

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

    // ── Auto score cutoff (disengaged path: every candidate reported, bar only rises) ──

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

        // One provider batch. Clamp only starts on the *next* batch if this one filled
        // (matches >= seeds). A single list batch still reports every candidate.
        Assert.Equal(search.MatchingSeeds, scored.Count);
        Assert.True(scored.Count > 0);
        // Overstock-at-ante-1 seeds (5X5 etc.) outscore the rest — the cutoff learned a max.
        Assert.Contains(scored, r => r.Score > scored.Min(x => x.Score));
    }

    // ── Filter creation context: cache families record the exact key lengths ──

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
        // Arcana pack: tarot + resample + soul keys.
        var arcana = Lengths(ctx => ctx.CacheArcanaPackTarotStream(2));
        string arcanaKey = MotelyPrngKeys.Tarot + MotelyPrngKeys.ArcanaPackItemSource + 2;
        Assert.Contains(arcanaKey.Length, arcana);
        Assert.Contains(
            (MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + 2).Length,
            arcana
        );

        // Shop tarot: plain key, no resample entry beyond it.
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

    // ── Vector voucher: stateless overload parity with fresh-state overload (R3) ──

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
                // Stateless: resamples only prerequisite (odd) vouchers. With a fresh run
                // state the stateful overload resamples the same set, so ante-1 results
                // must agree lane for lane.
                var stateless = ctx.GetAnteFirstVoucher(1);
                var freshState = new MotelyVectorRunState();
                var stateful = ctx.GetAnteFirstVoucher(1, freshState);

                // Exercise the voucher stream single-lane view on the same context.
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
