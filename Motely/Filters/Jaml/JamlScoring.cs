using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

public static class JamlScoring
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PrepareRunState(
        ref MotelySingleSearchContext ctx,
        IJamlClause[] clauses,
        MotelyRunState runState
    )
    {
        Debug.Assert(
            clauses.Length > 0,
            "PrepareRunState requires a non-empty should-clause array (CreatePlan / search wiring bug)."
        );

        int maxAnte = 0;
        int maxBossAnte = 0;

        for (int i = 0; i < clauses.Length; i++)
        {
            int clauseMaxAnte = GetMaxAnte(clauses[i]);
            if (clauseMaxAnte > maxAnte)
                maxAnte = clauseMaxAnte;

            int clauseMaxBossAnte = GetMaxBossAnte(clauses[i]);
            if (clauseMaxBossAnte > maxBossAnte)
                maxBossAnte = clauseMaxBossAnte;
        }

        ApplyPrepareRunState(ref ctx, runState, maxAnte, maxBossAnte);
    }

    /// <summary>
    /// Single match core for SIMD filter confirmation (<c>SearchIndividualSeeds</c> arms).
    /// Same voucher/boss prepare + raw occurrence count as should-scoring for this clause, so
    /// FilterDesc scalar confirm paths cannot drift from <see cref="CountRawOccurrences"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ClauseMeetsMinForFilter(
        ref MotelySingleSearchContext ctx,
        IJamlClause clause
    )
    {
        Debug.Assert(clause.Min > 0, "Clause.Min must be > 0 — loader bug.");
        var runState = new MotelyRunState();
        ApplyPrepareRunState(
            ref ctx,
            runState,
            GetMaxAnte(clause),
            GetMaxBossAnte(clause)
        );
        int raw = CountRawOccurrences(ref ctx, clause, runState);
        return MeetsOccurrenceBounds(raw, clause);
    }

    /// <summary>
    /// True when the SIMD filter confirm path is the same law as scoring (no over-permissive
    /// vector prefilter). Scoring may skip must re-eval when every must clause is exact.
    /// </summary>
    internal static bool IsExactFilterConfirm(IJamlClause clause) =>
        clause switch
        {
            BossClause => true,
            StartingDrawClause => true,
            PokerHandClause => true,
            StandardCardClause => true,
            // Legendary filter is pure SIMD edition (or passthrough) — pack/Soul confirm is score-only.
            LegendaryJokerClause => false,
            // Full vector roll walks — same law as scoring counts.
            VoucherClause => true,
            TagClause => true,
            BoosterPackClause => true,
            ErraticRankClause => true,
            ErraticSuitClause => true,
            SpectralCardClause sc => SpecialSpectralCardFilterDesc.Handles(sc)
                || sc.Sources is { RequireMegaPack: true }
                || sc.Sources is { EtherealTag: true }
                || sc.Sources is { OmenGlobe: true },
            TarotCardClause tc => tc.Sources is { CharmTag: true }
                || tc.Sources is { RequireMegaPack: true },
            PlanetCardClause pc => pc.Sources is { RequireMegaPack: true },
            // StandardCardClause is already exact (true above).
            JokerClause jc => JokerUsesLegendaryExactPath(jc)
                || jc.Sources is { RequireMegaPack: true },
            CommonJokerClause cjc => cjc.Sources is { RequireMegaPack: true },
            UncommonJokerClause ujc => ujc.Sources is { RequireMegaPack: true },
            RareJokerClause rjc => rjc.Sources is { RequireMegaPack: true },
            AndClause a => AllExactFilterConfirm(a.Clauses),
            OrClause o => AllExactFilterConfirm(o.Clauses),
            // Roll-scoped event filters are full vector counts (no coarse pack walk).
            LuckyMoneyClause
            or LuckyMultClause
            or MisprintMultClause
            or WheelOfFortuneClause
            or CavendishExtinctClause
            or GrosMichelExtinctClause
            or SpaceLevelupClause
            or BusinessPayoutClause
            or BloodstoneTriggerClause
            or ParkingPayoutClause
            or GlassDestroyClause
            or WheelStaysFlippedClause => true,
            _ => false,
        };

    private static bool AllExactFilterConfirm(IJamlClause[] clauses)
    {
        if (clauses.Length == 0)
            return false;
        for (int i = 0; i < clauses.Length; i++)
            if (!IsExactFilterConfirm(clauses[i]))
                return false;
        return true;
    }

    private static bool JokerUsesLegendaryExactPath(JokerClause clause)
    {
        if (JamlDisc.IsCategoryAny(clause.Jokers))
            return true;
        var jokers = clause.Jokers!;
        for (int i = 0; i < jokers.Length; i++)
        {
            if (
                ((MotelyJokerRarity)((int)jokers[i] & MotelyGlobals.JokerRarityMask))
                == MotelyJokerRarity.Legendary
            )
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when every must clause already had an exact SIMD confirm — scoring re-eval of must
    /// would only re-pay the same CountRawOccurrences law.
    /// </summary>
    internal static bool CanSkipMustReeval(IJamlClause[] mustClauses)
    {
        if (mustClauses.Length == 0)
            return true;
        return AllExactFilterConfirm(mustClauses);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyPrepareRunState(
        ref MotelySingleSearchContext ctx,
        MotelyRunState runState,
        int maxAnte,
        int maxBossAnte
    )
    {
        int maxVoucherAnte = maxAnte < 8 ? maxAnte + 1 : maxAnte;
        for (int ante = 1; ante <= maxVoucherAnte; ante++)
        {
            var voucher = ctx.GetAnteFirstVoucher(ante, runState);
            runState.ActivateVoucher(voucher);

            if (voucher is MotelyVoucher.Hieroglyph or MotelyVoucher.Petroglyph)
            {
                runState.ActivateExtendedPackAnte(ante - 1);
                var voucherStream = ctx.CreateVoucherStream(ante);
                var bonusVoucher = ctx.GetNextVoucher(ref voucherStream, runState);
                runState.ActivateVoucher(bonusVoucher);
            }
        }

        if (maxBossAnte > 0)
        {
            runState.CachedBosses = new MotelyBossBlind[maxBossAnte + 1];
            var bossStream = ctx.CreateBossStream();
            var bossState = new MotelyRunState();
            for (int ante = 1; ante <= maxBossAnte; ante++)
                runState.CachedBosses[ante] = ctx.GetBossForAnte(
                    ref bossStream,
                    ante,
                    bossState
                );
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CountOccurrences(
        ref MotelySingleSearchContext ctx,
        IJamlClause clause,
        MotelyRunState runState
    )
    {
        var count = CountOccurrencesUncapped(ref ctx, clause, runState);
        return CapScoreCount(count, clause);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountOccurrencesUncapped(
        ref MotelySingleSearchContext ctx,
        IJamlClause clause,
        MotelyRunState runState
    )
    {
        return clause switch
        {
            JokerClause c => CountJokerOccurrences(ref ctx, c, runState),
            CommonJokerClause c => CountCommonJokerOccurrences(ref ctx, c, runState),
            UncommonJokerClause c => CountUncommonJokerOccurrences(ref ctx, c, runState),
            RareJokerClause c => CountRareJokerOccurrences(ref ctx, c, runState),
            LegendaryJokerClause c => CountLegendaryJokerOccurrences(ref ctx, c, runState),
            VoucherClause c => CountVoucherOccurrences(ref ctx, c, runState),
            TarotCardClause c => CountTarotCardOccurrences(ref ctx, c, runState),
            SpectralCardClause c => CountSpectralCardOccurrences(ref ctx, c, runState),
            PlanetCardClause c => CountPlanetCardOccurrences(ref ctx, c, runState),
            BossClause c => CountBossOccurrences(c, runState),
            TagClause c => CountTagOccurrences(ref ctx, c, runState),
            BoosterPackClause c => CountBoosterPackOccurrences(ref ctx, c, runState),
            StandardCardClause c => CountStandardCardOccurrences(ref ctx, c, runState),
            ErraticRankClause c => CountErraticRankOccurrences(ref ctx, c),
            ErraticSuitClause c => CountErraticSuitOccurrences(ref ctx, c),
            LuckyMoneyClause c => CountLuckyMoneyOccurrences(ref ctx, c, runState),
            LuckyMultClause c => CountLuckyMultOccurrences(ref ctx, c, runState),
            MisprintMultClause c => CountMisprintMultOccurrences(ref ctx, c),
            WheelOfFortuneClause c => CountWheelOfFortuneOccurrences(ref ctx, c),
            CavendishExtinctClause c => CountCavendishExtinctOccurrences(ref ctx, c),
            GrosMichelExtinctClause c => CountGrosMichelExtinctOccurrences(ref ctx, c),
            SpaceLevelupClause c => CountSpaceLevelupOccurrences(ref ctx, c),
            BusinessPayoutClause c => CountBusinessPayoutOccurrences(ref ctx, c),
            BloodstoneTriggerClause c => CountBloodstoneTriggerOccurrences(ref ctx, c),
            ParkingPayoutClause c => CountParkingPayoutOccurrences(ref ctx, c),
            GlassDestroyClause c => CountGlassDestroyOccurrences(ref ctx, c),
            WheelStaysFlippedClause c => CountWheelStaysFlippedOccurrences(ref ctx, c),
            StartingDrawClause c => CountStartingDrawOccurrences(ref ctx, c),
            PokerHandClause c => CountPokerHandOccurrences(ref ctx, c),
            AndClause c => CountAndOccurrences(ref ctx, c, runState),
            OrClause c => CountOrOccurrences(ref ctx, c, runState),
            _ => UnhandledClauseForScoring(clause),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CapScoreCountForTesting(int count, IJamlClause clause) =>
        CapScoreCount(count, clause);

    /// <summary>
    /// Match bounds contract: <see cref="IJamlClause.Min"/> is the lower gate;
    /// <see cref="IJamlClause.Max"/> when set is the upper gate for must / filter confirm.
    /// Score tallies still use <see cref="CapScoreCount"/> so should columns cap contribution.
    /// SIMD prefilters may stay over-permissive on Max; scoring / exact confirm enforce it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool MeetsOccurrenceBounds(int raw, IJamlClause clause)
    {
        if (raw < clause.Min)
            return false;
        if (clause.Max is { } max && max > 0 && raw > max)
            return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CapScoreCount(int count, IJamlClause clause)
    {
        var max = clause.Max;
        return max is > 0 && count > max.Value ? max.Value : count;
    }

    private static int UnhandledClauseForScoring(IJamlClause clause)
    {
        Debug.Assert(
            false,
            $"JamlScoring.CountOccurrences: unhandled clause type {clause.GetType().Name} (extend switch or exclude from should-clauses)."
        );
        return 0;
    }

    private static int CountAndOccurrences(
        ref MotelySingleSearchContext ctx,
        AndClause clause,
        MotelyRunState runState
    )
    {
        Debug.Assert(
            clause.Clauses.Length > 0,
            "AndClause should not be empty after JAML load (validator / loader bug)."
        );

        // An AND counts COMPLETE conjunctions: min over children, not their sum.
        // Summing double-paid the clause score (tag=1 + joker=1 → tally 2 → 2×score).
        int combos = int.MaxValue;
        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountOccurrences(ref ctx, clause.Clauses[i], runState);
            if (count <= 0)
                return 0;
            if (count < combos)
                combos = count;
        }

        return clause.Score != 0 ? combos : 1;
    }

    private static int CountOrOccurrences(
        ref MotelySingleSearchContext ctx,
        OrClause clause,
        MotelyRunState runState
    )
    {
        Debug.Assert(
            clause.Clauses.Length > 0,
            "OrClause should not be empty after JAML load (validator / loader bug)."
        );
        Debug.Assert(
            clause.Min >= 1,
            "OrClause.Min must be >= 1 after JAML load (validator / loader bug)."
        );

        int matched = 0;
        int total = 0;
        int best = 0;
        bool useMax = clause.Mode == JamlLogicScoreMode.Max;

        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountOccurrences(ref ctx, clause.Clauses[i], runState);
            if (count > 0)
            {
                matched++;
                int w = clause.Clauses[i].Score;
                if (w == 0)
                    w = 1;
                int contribution = count * w;
                total += contribution;
                if (contribution > best)
                    best = contribution;
            }
        }

        if (matched < clause.Min)
            return 0;

        int aggregate = useMax ? best : total;
        return clause.Score != 0 ? aggregate : matched;
    }

    public static int CountRawOccurrences(
        ref MotelySingleSearchContext ctx,
        IJamlClause clause,
        MotelyRunState runState
    )
    {
        return clause switch
        {
            AndClause c => CountRawAndOccurrences(ref ctx, c, runState),
            OrClause c => CountRawOrOccurrences(ref ctx, c, runState),
            _ => CountOccurrencesUncapped(ref ctx, clause, runState),
        };
    }

    private static int CountRawAndOccurrences(
        ref MotelySingleSearchContext ctx,
        AndClause clause,
        MotelyRunState runState
    )
    {
        Debug.Assert(
            clause.Clauses.Length > 0,
            "AndClause should not be empty after JAML load (validator / loader bug)."
        );

        // Same min-of-children semantics as CountAndOccurrences: complete conjunctions only.
        int combos = int.MaxValue;
        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountRawOccurrences(ref ctx, clause.Clauses[i], runState);
            if (count <= 0)
                return 0;
            if (count < combos)
                combos = count;
        }

        return clause.Score != 0 ? combos : 1;
    }

    private static int CountRawOrOccurrences(
        ref MotelySingleSearchContext ctx,
        OrClause clause,
        MotelyRunState runState
    )
    {
        Debug.Assert(
            clause.Clauses.Length > 0,
            "OrClause should not be empty after JAML load (validator / loader bug)."
        );
        Debug.Assert(
            clause.Min >= 1,
            "OrClause.Min must be >= 1 after JAML load (validator / loader bug)."
        );

        int matched = 0;
        int total = 0;
        int best = 0;
        bool useMax = clause.Mode == JamlLogicScoreMode.Max;

        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountRawOccurrences(ref ctx, clause.Clauses[i], runState);
            if (count > 0)
            {
                matched++;
                total += count;
                if (count > best)
                    best = count;
            }
        }

        if (matched < clause.Min)
            return 0;

        int aggregate = useMax ? best : total;
        return clause.Score != 0 ? aggregate : matched;
    }

    private static int CountBossOccurrences(BossClause clause, MotelyRunState runState)
    {
        Debug.Assert(
            runState.CachedBosses != null,
            "Boss scoring requires PrepareRunState to populate CachedBosses (loader / run-state bug)."
        );
        Debug.Assert(
            clause.Bosses.Length > 0,
            "BossClause.Bosses must be non-empty after JAML load (validator / loader bug)."
        );
        Debug.Assert(
            clause.Antes.Length > 0,
            "BossClause.Antes must be non-empty after JAML load (validator / loader bug)."
        );

        int count = 0;
        foreach (int ante in clause.Antes)
        {
            Debug.Assert(
                ante >= 1 && ante < runState.CachedBosses!.Length,
                $"BossClause ante {ante} is out of range for CachedBosses (validator / loader bug)."
            );
            for (int i = 0; i < clause.Bosses.Length; i++)
                if (clause.Bosses[i] == runState.CachedBosses[ante])
                {
                    count++;
                }
        }
        return count;
    }

    private static int CountStandardCardOccurrences(
        ref MotelySingleSearchContext ctx,
        StandardCardClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var sources = clause.Sources ?? StandardCardFilterDesc.DefaultSources;
        int maxShop = ArrayMax(sources.ShopItems);
        int userMaxPack = ArrayMax(sources.BoosterPacks);

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            if (sources.ShopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (ArrayContains(sources.ShopItems, slot))
                        count += MatchStandardCard(item, clause);
                }
            }

            if (sources.BoosterPacks.Length > 0)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                var cardStream = ctx.CreateStandardPackCardStream(ante);
                for (int packIndex = 0; packIndex <= maxPack; packIndex++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    if (pack.GetPackType() != MotelyBoosterPackType.Standard)
                        continue;
                    var packSize = pack.GetPackSize();
                    var contents = ctx.GetNextStandardPackContents(
                        ref cardStream,
                        packSize
                    );
                    if (
                        !ArrayContains(sources.BoosterPacks, packIndex)
                        || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                    )
                        continue;
                    for (int i = 0; i < contents.Length; i++)
                        count += MatchStandardCard(contents[i], clause);
                }
            }
        }

        return count;
    }

    private static int CountTarotCardOccurrences(
        ref MotelySingleSearchContext ctx,
        TarotCardClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        // Scoring is authoritative, so it resolves the same defaults the SIMD desc applies.
        var sources = clause.Sources ?? TarotCardFilterDesc.DefaultSources;
        int maxShop = ArrayMax(sources.ShopItems);
        int userMaxPack = ArrayMax(sources.BoosterPacks);
        int maxEmperor = ArrayMax(sources.Emperor);
        int maxSeal = ArrayMax(sources.PurpleSealOrEightBall);

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            if (sources.ShopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (ArrayContains(sources.ShopItems, slot))
                        count += MatchTarot(item, clause);
                }
            }

            if (sources.BoosterPacks.Length > 0)
            {
                bool charmWant = sources.CharmTag;

                var packStream = ctx.CreateBoosterPackStream(ante);
                var tarotStream = ctx.CreateArcanaPackTarotStream(ante);
                bool hadNaturalArcanaPack = false;
                int weightedShopDrawNumber = 0;

                for (int packIndex = 0; ; packIndex++)
                {
                    bool needForClause = packIndex <= maxPack;
                    bool needForCharmClosure = charmWant && weightedShopDrawNumber < 2;
                    if (!needForClause && !needForCharmClosure)
                        break;

                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    var packType = pack.GetPackType();
                    if (packType == MotelyBoosterPackType.Buffoon)
                        continue;

                    weightedShopDrawNumber++;

                    if (packType == MotelyBoosterPackType.Arcana)
                    {
                        hadNaturalArcanaPack = true;
                        // Charm Tag opens Mega Arcana (5 cards). Shop Arcana uses rolled size.
                        // requireMega only gates whether the open counts, not stream advance.
                        var packSize = pack.GetPackSize();
                        var contents = ctx.GetNextArcanaPackContents(
                            ref tarotStream,
                            packSize
                        );
                        if (
                            !ArrayContains(sources.BoosterPacks, packIndex)
                            || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                        )
                            continue;
                        for (int i = 0; i < contents.Length; i++)
                            count += MatchTarot(contents[i], clause);
                        continue;
                    }

                    // Charm: extra Arcana on the second real shop pack (after Buffoon) only if the two
                    // weighted rolls had no Arcana — uses pack stream order, not ante-scaled indices.
                    // Game law: Charm is always Mega Arcana (size 5 / pick 2).
                    if (charmWant && !hadNaturalArcanaPack && weightedShopDrawNumber == 2)
                    {
                        var packSize = MotelyBoosterPackSize.Mega;
                        var contents = ctx.GetNextArcanaPackContents(
                            ref tarotStream,
                            packSize
                        );
                        if (
                            !ArrayContains(sources.BoosterPacks, packIndex)
                            || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                        )
                            continue;
                        for (int i = 0; i < contents.Length; i++)
                            count += MatchTarot(contents[i], clause);
                    }
                }
            }

            if (sources.Emperor.Length > 0)
            {
                var emperorStream = ctx.CreateEmperorTarotStream(ante);
                for (int roll = 0; roll <= maxEmperor; roll++)
                {
                    var (t1, t2) = ctx.GetNextEmperorTarots(ref emperorStream);
                    if (!ArrayContains(sources.Emperor, roll))
                        continue;
                    count += MatchTarot(t1, clause);
                    count += MatchTarot(t2, clause);
                }
            }

            if (sources.PurpleSealOrEightBall.Length > 0)
            {
                var sealStream = ctx.CreatePurpleSealTarotStream(ante);
                for (int roll = 0; roll <= maxSeal; roll++)
                {
                    var item = ctx.GetNextTarot(ref sealStream);
                    if (ArrayContains(sources.PurpleSealOrEightBall, roll))
                        count += MatchTarot(item, clause);
                }
            }
        }

        return count;
    }

    private static int CountSpectralCardOccurrences(
        ref MotelySingleSearchContext ctx,
        SpectralCardClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var sources = ResolveSpectralSources(clause);
        int maxShop = ArrayMax(sources.ShopItems);
        int userMaxPack = ArrayMax(sources.BoosterPacks);
        int maxSixthSense = ArrayMax(sources.SixthSense);
        int maxSeance = ArrayMax(sources.Seance);

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            if (sources.ShopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (ArrayContains(sources.ShopItems, slot))
                        count += MatchSpectral(item, clause);
                }
            }

            if (sources.BoosterPacks.Length > 0)
            {
                bool etherealWant = sources.EtherealTag;

                var packStream = ctx.CreateBoosterPackStream(ante);
                var spectralStream = ctx.CreateSpectralPackSpectralStream(ante);
                bool hadNaturalSpectralPack = false;
                int weightedShopDrawNumber = 0;

                for (int packIndex = 0; ; packIndex++)
                {
                    bool needForClause = packIndex <= maxPack;
                    bool needForEtherealClosure = etherealWant && weightedShopDrawNumber < 2;
                    if (!needForClause && !needForEtherealClosure)
                        break;

                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    var packType = pack.GetPackType();
                    if (packType == MotelyBoosterPackType.Buffoon)
                        continue;

                    weightedShopDrawNumber++;

                    if (packType == MotelyBoosterPackType.Spectral)
                    {
                        hadNaturalSpectralPack = true;
                        var packSize = pack.GetPackSize();
                        var contents = ctx.GetNextSpectralPackContents(
                            ref spectralStream,
                            packSize
                        );
                        if (
                            !ArrayContains(sources.BoosterPacks, packIndex)
                            || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                        )
                            continue;
                        for (int i = 0; i < contents.Length; i++)
                            count += MatchSpectral(contents[i], clause);
                        continue;
                    }

                    if (etherealWant && !hadNaturalSpectralPack && weightedShopDrawNumber == 2)
                    {
                        var packSize = pack.GetPackSize();
                        var contents = ctx.GetNextSpectralPackContents(
                            ref spectralStream,
                            packSize
                        );
                        if (
                            !ArrayContains(sources.BoosterPacks, packIndex)
                            || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                        )
                            continue;
                        for (int i = 0; i < contents.Length; i++)
                            count += MatchSpectral(contents[i], clause);
                    }
                }
            }

            // Omen Globe: 20% of Arcana pack cards become Spectral (voucher assumed when source is set).
            if (sources.OmenGlobe)
                count += CountOmenGlobeArcanaSpectrals(ref ctx, clause, sources, ante, runState);

            if (sources.SixthSense.Length > 0)
            {
                var sixthSenseStream = ctx.CreateSixthSenseSpectralStream(ante);
                for (int roll = 0; roll <= maxSixthSense; roll++)
                {
                    var item = ctx.GetNextSpectral(ref sixthSenseStream);
                    if (ArrayContains(sources.SixthSense, roll))
                        count += MatchSpectral(item, clause);
                }
            }

            if (sources.Seance.Length > 0)
            {
                var seanceStream = ctx.CreateSeanceSpectralStream(ante);
                for (int roll = 0; roll <= maxSeance; roll++)
                {
                    var item = ctx.GetNextSpectral(ref seanceStream);
                    if (ArrayContains(sources.Seance, roll))
                        count += MatchSpectral(item, clause);
                }
            }
        }

        // The two "special" spectral cards spawn from a dedicated soul/black-hole roll in pack types
        // the loop above never reads: TheSoul also appears in Arcana packs (tarot stream), BlackHole
        // also in Celestial packs (planet stream). The spectral-pack walk above already counts both
        // when they land in a SPECTRAL pack; this adds the missing Arcana/Celestial sources so
        // `spectralCard: TheSoul` / `spectralCard: BlackHole` see everywhere the card can actually
        // spawn — the same blind spot that made the spectralCard path wrong for these two.
        if (sources.BoosterPacks.Length > 0)
        {
            if (SpectralClauseTargets(clause, MotelySpectralCard.TheSoul))
                count += CountTheSoulInArcanaPacks(ref ctx, clause, runState);
            if (SpectralClauseTargets(clause, MotelySpectralCard.BlackHole))
                count += CountBlackHoleInCelestialPacks(ref ctx, clause, runState);
        }

        return count;
    }

    /// <summary>
    /// Omen Globe: each Arcana pack card has a 1/5 chance to be generated as Spectral instead of
    /// Tarot. Source flag assumes the voucher is owned. Pack slots follow <paramref name="sources"/>.BoosterPacks
    /// when set; otherwise the full late-ante pack range.
    /// </summary>
    private static int CountOmenGlobeArcanaSpectrals(
        ref MotelySingleSearchContext ctx,
        SpectralCardClause clause,
        SpectralCardSourceConfig sources,
        int ante,
        MotelyRunState runState
    )
    {
        int userMaxPack =
            sources.BoosterPacks.Length > 0
                ? ArrayMax(sources.BoosterPacks)
                : MotelyGlobals.LateAntesMaxPackSlot;
        int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);

        var packStream = ctx.CreateBoosterPackStream(ante);
        var tarotStream = ctx.CreateArcanaPackTarotStream(ante);
        var spectralStream = ctx.CreateArcanaOmenSpectralStream(ante);
        var omenStream = ctx.CreateOmenGlobePrngStream();

        int count = 0;

        for (int packIndex = 0; packIndex <= maxPack; packIndex++)
        {
            var pack = ctx.GetNextBoosterPack(ref packStream);
            if (pack.GetPackType() != MotelyBoosterPackType.Arcana)
                continue;

            bool slotWanted =
                sources.BoosterPacks.Length == 0
                || ArrayContains(sources.BoosterPacks, packIndex);
            var packSize = pack.GetPackSize();
            int cardCount = MotelyBoosterPackType.Arcana.GetCardCount(packSize);
            var packSet = new MotelySingleItemSet();

            for (int c = 0; c < cardCount; c++)
            {
                // Balatro: type Tarot → 20% becomes Spectral via omen_globe before create_card.
                if (ctx.GetNextOmenGlobeSpectral(ref omenStream))
                {
                    var spectral = ctx.GetNextSpectral(ref spectralStream, packSet);
                    packSet.Append(spectral);
                    if (slotWanted)
                        count += MatchSpectral(spectral, clause);
                }
                else
                {
                    var tarot = ctx.GetNextTarot(ref tarotStream, packSet);
                    packSet.Append(tarot);
                }
            }
        }

        return count;
    }

    /// <summary>Whether a spectral clause names <paramref name="card"/> as a target.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SpectralClauseTargets(SpectralCardClause clause, MotelySpectralCard card)
    {
        var spectrals = JamlDisc.OrEmpty(clause.Spectrals);
        for (int i = 0; i < spectrals.Length; i++)
            if (spectrals[i] == card)
                return true;
        return false;
    }

    /// <summary>
    /// Explicit <c>sources:</c> wins. Null sources: shop-only for ordinary spectrals; pack slots
    /// for Soul/BlackHole (they never appear in shop).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SpectralCardSourceConfig ResolveSpectralSources(SpectralCardClause clause) =>
        clause.Sources
        ?? (
            TargetsSpecialSpectral(clause)
                ? SpectralCardFilterDesc.DefaultSpecialSources
                : SpectralCardFilterDesc.DefaultSources
        );

    /// <summary>
    /// True when the clause names TheSoul and/or BlackHole — the "special" spectrals that need the
    /// Arcana/Celestial pack sources. Used to route <c>spectralCard:</c> to <see cref="SpecialSpectralCardFilterDesc"/>.
    /// </summary>
    public static bool TargetsSpecialSpectral(SpectralCardClause clause) =>
        SpectralClauseTargets(clause, MotelySpectralCard.TheSoul)
        || SpectralClauseTargets(clause, MotelySpectralCard.BlackHole);

    /// <summary>
    /// Scalar spectral count for the SIMD filter's per-seed confirmation pass.
    /// Same prepare + count as <see cref="ClauseMeetsMinForFilter"/> / should-scoring.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CountSpectralCardOccurrencesForFilter(
        ref MotelySingleSearchContext ctx,
        SpectralCardClause clause
    )
    {
        var runState = new MotelyRunState();
        ApplyPrepareRunState(ref ctx, runState, GetMaxAnte(clause), GetMaxBossAnte(clause));
        return CountSpectralCardOccurrences(ref ctx, clause, runState);
    }

    /// <summary>
    /// TheSoul appearing in Arcana packs (tarot stream soul roll). Same booster-pack indexing as the
    /// spectral-pack walk in <see cref="CountSpectralCardOccurrences"/> so source slot numbers line up;
    /// reads each Arcana pack's contents to keep the tarot stream aligned, counts only targeted slots.
    /// </summary>
    private static int CountTheSoulInArcanaPacks(
        ref MotelySingleSearchContext ctx,
        SpectralCardClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var sources = ResolveSpectralSources(clause);
        int userMaxPack = ArrayMax(sources.BoosterPacks);

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);

            var packStream = ctx.CreateBoosterPackStream(ante);
            var tarotStream = ctx.CreateArcanaPackTarotStream(ante);
            for (int packIndex = 0; packIndex <= maxPack; packIndex++)
            {
                var pack = ctx.GetNextBoosterPack(ref packStream);
                if (pack.GetPackType() != MotelyBoosterPackType.Arcana)
                    continue;
                var packSize = pack.GetPackSize();
                var contents = ctx.GetNextArcanaPackContents(ref tarotStream, packSize);
                if (
                    !ArrayContains(sources.BoosterPacks, packIndex)
                    || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                )
                    continue;
                for (int i = 0; i < contents.Length; i++)
                    count += MatchSpectral(contents[i], clause);
            }
        }

        return count;
    }

    /// <summary>
    /// BlackHole appearing in Celestial packs (planet stream black-hole roll). Mirror of
    /// <see cref="CountPlanetCardOccurrences"/>'s celestial walk; counts only the BlackHole item.
    /// </summary>
    private static int CountBlackHoleInCelestialPacks(
        ref MotelySingleSearchContext ctx,
        SpectralCardClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var sources = ResolveSpectralSources(clause);
        int userMaxPack = ArrayMax(sources.BoosterPacks);

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);

            var packStream = ctx.CreateBoosterPackStream(ante);
            var planetStream = ctx.CreateCelestialPackPlanetStream(ante);
            for (int packIndex = 0; packIndex <= maxPack; packIndex++)
            {
                var pack = ctx.GetNextBoosterPack(ref packStream);
                if (pack.GetPackType() != MotelyBoosterPackType.Celestial)
                    continue;
                var packSize = pack.GetPackSize();
                var contents = ctx.GetNextCelestialPackContents(ref planetStream, packSize);
                if (
                    !ArrayContains(sources.BoosterPacks, packIndex)
                    || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                )
                    continue;
                for (int i = 0; i < contents.Length; i++)
                    count += MatchSpectral(contents[i], clause);
            }
        }

        return count;
    }

    private static int CountPlanetCardOccurrences(
        ref MotelySingleSearchContext ctx,
        PlanetCardClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        // Scoring is authoritative, so it must see the same defaults the SIMD desc applies.
        var sources = clause.Sources ?? PlanetCardFilterDesc.DefaultSources;
        int maxShop = ArrayMax(sources.ShopItems);
        int userMaxPack = ArrayMax(sources.BoosterPacks);

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            if (sources.ShopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (ArrayContains(sources.ShopItems, slot))
                        count += MatchPlanet(item, clause);
                }
            }

            if (sources.BoosterPacks.Length > 0)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                var planetStream = ctx.CreateCelestialPackPlanetStream(ante);
                for (int packIndex = 0; packIndex <= maxPack; packIndex++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    if (pack.GetPackType() != MotelyBoosterPackType.Celestial)
                        continue;
                    var packSize = pack.GetPackSize();
                    var contents = ctx.GetNextCelestialPackContents(
                        ref planetStream,
                        packSize
                    );
                    if (
                        !ArrayContains(sources.BoosterPacks, packIndex)
                        || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                    )
                        continue;
                    for (int i = 0; i < contents.Length; i++)
                        count += MatchPlanet(contents[i], clause);
                }
            }
        }

        return count;
    }

    private static int CountVoucherOccurrences(
        ref MotelySingleSearchContext ctx,
        VoucherClause clause,
        MotelyRunState runState
    )
    {
        // Start from a fresh state — PrepareRunState already activated vouchers into runState,
        // which would cause GetAnteFirstVoucher to skip them and return wrong results.
        var localState = new MotelyRunState();
        int count = 0;
        int maxAnte = GetMaxAnte(clause);
        int maxVoucherRoll = MapFeatureRolls.MaxRollIndex(clause.Rolls);
        Span<MotelyVoucher> streamDraws = stackalloc MotelyVoucher[maxVoucherRoll + 1];

        for (int ante = 1; ante <= maxAnte; ante++)
        {
            var voucher = ctx.GetAnteFirstVoucher(ante, localState);
            if (ArrayContains(clause.Antes, ante))
            {
                int maxRoll = maxVoucherRoll;
                if (maxRoll >= 1)
                {
                    var voucherStream = ctx.CreateVoucherStream(ante);
                    for (int i = 1; i <= maxRoll; i++)
                        streamDraws[i] = ctx.GetNextVoucher(ref voucherStream, localState);
                }

                foreach (var roll in clause.Rolls)
                {
                    var rolled = roll == 0 ? voucher : streamDraws[roll];
                    for (int i = 0; i < clause.Vouchers.Length; i++)
                    {
                        if (rolled == clause.Vouchers[i])
                        {
                            count++;
                        }
                    }
                }
            }

            localState.ActivateVoucher(voucher);
        }

        return count;
    }

    private static int CountTagOccurrences(
        ref MotelySingleSearchContext ctx,
        TagClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        int maxDraw = MapFeatureRolls.MaxRollIndex(clause.Rolls);
        Span<MotelyTag> draws = stackalloc MotelyTag[maxDraw + 1];

        foreach (int ante in clause.Antes)
        {
            var tagStream = ctx.CreateTagStream(ante);
            for (int i = 0; i <= maxDraw; i++)
                draws[i] = ctx.GetNextTag(ref tagStream);

            foreach (var drawIndex in clause.Rolls)
            {
                var rolled = draws[drawIndex];
                for (int i = 0; i < clause.Tags.Length; i++)
                {
                    if (rolled == clause.Tags[i])
                    {
                        count++;
                    }
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Shop pack offers (kind+size enum). Rolls = pack slot indices. Empty Packs = any pack.
    /// </summary>
    private static int CountBoosterPackOccurrences(
        ref MotelySingleSearchContext ctx,
        BoosterPackClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        int userMaxPack = MapFeatureRolls.MaxRollIndex(clause.Rolls);
        bool anyPack = JamlDisc.IsCategoryAny(clause.Packs);

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            var packStream = ctx.CreateBoosterPackStream(ante);
            for (int packIndex = 0; packIndex <= maxPack; packIndex++)
            {
                var pack = ctx.GetNextBoosterPack(ref packStream);
                if (!ArrayContains(clause.Rolls, packIndex))
                    continue;
                if (anyPack)
                {
                    count++;
                    continue;
                }
                for (int i = 0; i < clause.Packs.Length; i++)
                {
                    if (pack == clause.Packs[i])
                    {
                        count++;
                        break;
                    }
                }
            }
        }
        return count;
    }

    private static int CountStartingDrawOccurrences(
        ref MotelySingleSearchContext ctx,
        StartingDrawClause clause
    )
    {
        int count = 0;
        foreach (int ante in clause.Antes)
        {
            MotelyItem[] deck = new MotelyItem[MotelyEnum<MotelyStandardCard>.ValueCount];
            for (int i = 0; i < deck.Length; i++)
                deck[i] = new(MotelyEnum<MotelyStandardCard>.Values[i]);

            ctx.Shuffle(MotelyPokerHandEval.ShuffleKeyForAnte(ante), deck);
            int handSize = Math.Min(8, deck.Length);
            for (int i = 0; i < handSize; i++)
            {
                var card = deck[deck.Length - handSize + i];
                bool matchRank =
                    !clause.Rank.HasValue || card.StandardcardRank == clause.Rank.Value;
                bool matchSuit =
                    !clause.Suit.HasValue || card.StandardcardSuit == clause.Suit.Value;
                if (matchRank && matchSuit)
                    count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Count antes/rounds whose starting 8-card hand's best poker category is in the clause list.
    /// Empty <c>antes</c> defaults to round 1 (same as a single starting-hand check).
    /// </summary>
    private static int CountPokerHandOccurrences(
        ref MotelySingleSearchContext ctx,
        PokerHandClause clause
    )
    {
        int count = 0;
        int[] antes = clause.Antes.Length > 0 ? clause.Antes : [1];
        int[] rolls = clause.Rolls.Length > 0 ? clause.Rolls : [0];
        foreach (int ante in antes)
        {
            string shuffleKey = MotelyPokerHandEval.ShuffleKeyForAnte(ante);
            foreach (int roll in rolls)
            {
                // Only a blind that is actually played advances nr{ante}; at most three per ante.
                if (roll < 0 || roll >= PokerHandFilterDesc.MaxBlindsPerAnte)
                    continue;

                MotelyItem[] deck = new MotelyItem[MotelyEnum<MotelyStandardCard>.ValueCount];
                for (int i = 0; i < deck.Length; i++)
                    deck[i] = new(MotelyEnum<MotelyStandardCard>.Values[i]);

                ctx.Shuffle(shuffleKey, deck, roll);
                int handSize = Math.Min(8, deck.Length);
                Span<MotelyItem> hand = deck.AsSpan(deck.Length - handSize, handSize);
                MotelyPokerHand best = MotelyPokerHandEval.BestScore(hand).Type;

                // Empty = any hand (BoosterPackClause.Packs convention). Every draw has a best
                // hand, so an "any" clause counts every blind in scope.
                if (clause.PokerHands.Length == 0)
                {
                    count++;
                    continue;
                }

                for (int i = 0; i < clause.PokerHands.Length; i++)
                {
                    if (clause.PokerHands[i] == best)
                    {
                        count++;
                        break;
                    }
                }
            }
        }
        return count;
    }

    private static int CountErraticRankOccurrences(
        ref MotelySingleSearchContext ctx,
        ErraticRankClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateErraticDeckPrngStream();
        for (int i = 0; i < 52; i++)
            if (ctx.GetNextErraticDeckCard(ref stream).StandardcardRank == clause.Rank)
                count++;
        return count;
    }

    private static int CountErraticSuitOccurrences(
        ref MotelySingleSearchContext ctx,
        ErraticSuitClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateErraticDeckPrngStream();
        for (int i = 0; i < 52; i++)
            if (ctx.GetNextErraticDeckCard(ref stream).StandardcardSuit == clause.Suit)
                count++;
        return count;
    }

    private static int CountLuckyMoneyOccurrences(
        ref MotelySingleSearchContext ctx,
        LuckyMoneyClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateLuckyCardMoneyStream(isCached: false);
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextLuckyMoney(ref stream, luck);
            if (ctx.GetNextLuckyMoney(ref stream, luck))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountLuckyMultOccurrences(
        ref MotelySingleSearchContext ctx,
        LuckyMultClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateLuckyCardMultStream(isCached: false);
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextLuckyMult(ref stream, luck);
            if (ctx.GetNextLuckyMult(ref stream, luck))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountMisprintMultOccurrences(
        ref MotelySingleSearchContext ctx,
        MisprintMultClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateMisprintPrngStream();
        var min = clause.Min;
        var max = clause.Max;
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextMisprintMult(ref stream);
            if (ctx.GetNextMisprintMult(ref stream) >= clause.Mult)
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountWheelOfFortuneOccurrences(
        ref MotelySingleSearchContext ctx,
        WheelOfFortuneClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateWheelOfFortuneStream();
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextWheelOfFortune(ref stream, luck);
            if (ctx.GetNextWheelOfFortune(ref stream, luck) != MotelyItemEdition.None)
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountCavendishExtinctOccurrences(
        ref MotelySingleSearchContext ctx,
        CavendishExtinctClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateCavendishPrngStream(false);
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextCavendishExtinct(ref stream, luck);
            if (ctx.GetNextCavendishExtinct(ref stream, luck))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountGrosMichelExtinctOccurrences(
        ref MotelySingleSearchContext ctx,
        GrosMichelExtinctClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateGrosMichelPrngStream(false);
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextGrosMichelExtinct(ref stream, luck);
            if (ctx.GetNextGrosMichelExtinct(ref stream, luck))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountSpaceLevelupOccurrences(
        ref MotelySingleSearchContext ctx,
        SpaceLevelupClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateSpacePrngStream();
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextSpaceLevelup(ref stream, luck);
            if (ctx.GetNextSpaceLevelup(ref stream, luck))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountBusinessPayoutOccurrences(
        ref MotelySingleSearchContext ctx,
        BusinessPayoutClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateBusinessPrngStream();
        var min = clause.Min;
        var max = clause.Max;
        // Flat 50/50 (Chance = 2) — no luck/Oops multiplier.
        for (int i = 0; i < clause.Rolls.Length; i++)
        {
            var rollIndex = clause.Rolls[i];
            for (int j = 0; j < rollIndex; j++)
                ctx.GetNextBusinessPayout(ref stream);
            if (ctx.GetNextBusinessPayout(ref stream))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }

            int rollsRemaining = clause.Rolls.Length - 1 - i;
            if (count + rollsRemaining < min)
                return 0;
        }
        return count;
    }

    private static int CountBloodstoneTriggerOccurrences(
        ref MotelySingleSearchContext ctx,
        BloodstoneTriggerClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateBloodstonePrngStream();
        var min = clause.Min;
        var max = clause.Max;
        // Flat 50/50 (Chance = 2) — no luck/Oops multiplier.
        foreach (var rollIndex in clause.Rolls)
        {
            for (int i = 0; i < rollIndex; i++)
                ctx.GetNextBloodstoneTrigger(ref stream);
            if (ctx.GetNextBloodstoneTrigger(ref stream))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }
        }
        return count;
    }

    private static int CountParkingPayoutOccurrences(
        ref MotelySingleSearchContext ctx,
        ParkingPayoutClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateParkingPrngStream();
        var min = clause.Min;
        var max = clause.Max;
        // Flat 50/50 (Chance = 2) — no luck/Oops multiplier.
        foreach (var rollIndex in clause.Rolls)
        {
            for (int i = 0; i < rollIndex; i++)
                ctx.GetNextParkingPayout(ref stream);
            if (ctx.GetNextParkingPayout(ref stream))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }
        }
        return count;
    }

    private static int CountGlassDestroyOccurrences(
        ref MotelySingleSearchContext ctx,
        GlassDestroyClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateGlassPrngStream();
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        foreach (var rollIndex in clause.Rolls)
        {
            for (int i = 0; i < rollIndex; i++)
                ctx.GetNextGlassDestroy(ref stream, luck);
            if (ctx.GetNextGlassDestroy(ref stream, luck))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }
        }
        return count;
    }

    private static int CountWheelStaysFlippedOccurrences(
        ref MotelySingleSearchContext ctx,
        WheelStaysFlippedClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateTheWheelPrngStream();
        var min = clause.Min;
        var max = clause.Max;
        double luck = (double)clause.With.Luck;
        foreach (var rollIndex in clause.Rolls)
        {
            for (int i = 0; i < rollIndex; i++)
                ctx.GetNextWheelStaysFlipped(ref stream, luck);
            if (ctx.GetNextWheelStaysFlipped(ref stream, luck))
            {
                count++;
                if (max is null && count >= min)
                    return count;
            }
        }
        return count;
    }

    private static int CountLegendaryJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        LegendaryJokerClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var sources = clause.Sources ?? LegendaryJokerFilterDesc.DefaultSources;
        int userMaxPack = sources.MaxReferencedBoosterSlot();

        foreach (int ante in clause.Antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            count += LegendarySoulMatcher.CountAnte(
                ref ctx,
                ante,
                clause,
                maxPack,
                stopAfterFirstMatch: false
            );
        }

        return count;
    }

    private static int CountJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        JokerClause clause,
        MotelyRunState runState
    )
    {
        return CountJokerClauseOccurrences(ref ctx, clause, runState);
    }

    private static int CountCommonJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        CommonJokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? CommonJokerFilterDesc.DefaultSources;
        if (JamlDisc.IsCategoryAny(clause.Jokers))
            return CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                MotelyJokerRarity.Common,
                clause.Edition,
                clause.Stickers,
                runState
            );
        return CountJokerOccurrencesGeneric(
            ref ctx,
            clause.Antes,
            sources,
            clause.Jokers,
            clause.Edition,
            clause.Stickers,
            runState
        );
    }

    private static int CountUncommonJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        UncommonJokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? UncommonJokerFilterDesc.DefaultSources;
        if (JamlDisc.IsCategoryAny(clause.Jokers))
            return CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                MotelyJokerRarity.Uncommon,
                clause.Edition,
                clause.Stickers,
                runState
            );
        return CountJokerOccurrencesGeneric(
            ref ctx,
            clause.Antes,
            sources,
            clause.Jokers,
            clause.Edition,
            clause.Stickers,
            runState
        );
    }

    private static int CountRareJokerOccurrences(
        ref MotelySingleSearchContext ctx,
        RareJokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? RareJokerFilterDesc.DefaultSources;
        if (JamlDisc.IsCategoryAny(clause.Jokers))
            return CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                MotelyJokerRarity.Rare,
                clause.Edition,
                clause.Stickers,
                runState
            );
        return CountJokerOccurrencesGeneric(
            ref ctx,
            clause.Antes,
            sources,
            clause.Jokers,
            clause.Edition,
            clause.Stickers,
            runState
        );
    }

    /// <summary>
    /// Scalar joker count for the SIMD filter's per-seed confirmation pass.
    /// Same prepare + count as <see cref="ClauseMeetsMinForFilter"/> / should-scoring.
    /// </summary>
    internal static int CountJokerClauseOccurrencesForFilter(
        ref MotelySingleSearchContext ctx,
        JokerClause clause
    )
    {
        var runState = new MotelyRunState();
        ApplyPrepareRunState(ref ctx, runState, GetMaxAnte(clause), GetMaxBossAnte(clause));
        return CountJokerClauseOccurrences(ref ctx, clause, runState);
    }

    private static int CountJokerClauseOccurrences(
        ref MotelySingleSearchContext ctx,
        JokerClause clause,
        MotelyRunState runState
    )
    {
        var sources = clause.Sources ?? JokerFilterDesc.DefaultSources;
        if (JamlDisc.IsCategoryAny(clause.Jokers))
        {
            int normalWildcard = CountJokerOccurrencesWildcard(
                ref ctx,
                clause.Antes,
                sources,
                wildcardRarity: null,
                clause.Edition,
                clause.Stickers,
                runState
            );

            var legendaryWildcard = new LegendaryJokerClause
            {
                Label = clause.Label,
                Score = clause.Score,
                Jokers = [],
                Edition = clause.Edition,
                Sources = clause.LegendarySources ?? LegendaryJokerFilterDesc.DefaultSources,
                Antes = clause.Antes,
                Min = clause.Min,
                Max = clause.Max,
            };

            return normalWildcard
                + CountLegendaryJokerOccurrences(ref ctx, legendaryWildcard, runState);
        }

        var jokers = JamlDisc.OrEmpty(clause.Jokers);
        var nonLegendary = jokers
            .Where(static j =>
                ((MotelyJokerRarity)((int)j & MotelyGlobals.JokerRarityMask))
                != MotelyJokerRarity.Legendary
            )
            .ToArray();

        var legendary = jokers
            .Where(static j =>
                ((MotelyJokerRarity)((int)j & MotelyGlobals.JokerRarityMask))
                == MotelyJokerRarity.Legendary
            )
            .ToArray();

        int count = 0;

        if (nonLegendary.Length > 0)
        {
            count += CountJokerOccurrencesGeneric(
                ref ctx,
                clause.Antes,
                sources,
                nonLegendary,
                clause.Edition,
                clause.Stickers,
                runState
            );
        }

        if (legendary.Length > 0)
        {
            var legendaryClause = new LegendaryJokerClause
            {
                Label = clause.Label,
                Score = clause.Score,
                Jokers = legendary,
                Edition = clause.Edition,
                Sources = clause.LegendarySources ?? LegendaryJokerFilterDesc.DefaultSources,
                Antes = clause.Antes,
                Min = clause.Min,
                Max = clause.Max,
            };

            count += CountLegendaryJokerOccurrences(ref ctx, legendaryClause, runState);
        }

        return count;
    }

    private static int CountJokerOccurrencesGeneric<TJoker>(
        ref MotelySingleSearchContext ctx,
        int[] antes,
        JokerSourceConfig sources,
        TJoker[] jokers,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState
    )
        where TJoker : struct, Enum
    {
        int count = 0;
        var shopItems = sources.ShopItems;
        var boosterPacks = sources.BoosterPacks;

        int maxShop = ArrayMax(shopItems);
        int userMaxPack = ArrayMax(boosterPacks);
        var targetTypes = new MotelyItemType[jokers.Length];
        for (int i = 0; i < jokers.Length; i++)
            targetTypes[i] = Enum.Parse<MotelyItemType>(jokers[i].ToString(), true);

        foreach (int ante in antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);

            if (shopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (!ArrayContains(shopItems, slot))
                        continue;
                    count += MatchJoker(item, targetTypes, edition, stickers);
                }
            }

            if (boosterPacks.Length > 0)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                var jokerStream = ctx.CreateBuffoonPackJokerStream(ante);
                for (int packIndex = 0; packIndex <= maxPack; packIndex++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    if (pack.GetPackType() != MotelyBoosterPackType.Buffoon)
                        continue;
                    var packSize = pack.GetPackSize();
                    var contents = ctx.GetNextBuffoonPackContents(
                        ref jokerStream,
                        packSize
                    );
                    if (
                        !ArrayContains(boosterPacks, packIndex)
                        || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                    )
                        continue;
                    for (int i = 0; i < contents.Length; i++)
                    {
                        count += MatchJoker(contents[i], targetTypes, edition, stickers);
                    }
                }
            }

            count += CountSpecialtyJokerSources(
                ref ctx,
                ante,
                sources,
                targetTypes,
                edition,
                stickers,
                runState
            );
        }

        return count;
    }

    private static int CountSpecialtyJokerSources(
        ref MotelySingleSearchContext ctx,
        int ante,
        JokerSourceConfig sources,
        MotelyItemType[] targetTypes,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState
    )
    {
        int count = 0;

        if (sources.Judgement.Length > 0)
        {
            int max = ArrayMax(sources.Judgement);
            var stream = ctx.CreateJudgementJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Judgement, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.Wraith.Length > 0)
        {
            int max = ArrayMax(sources.Wraith);
            var stream = ctx.CreateWraithJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Wraith, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.RiffRaff.Length > 0)
        {
            int max = ArrayMax(sources.RiffRaff);
            var stream = ctx.CreateRiffRaffJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RiffRaff, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.RareTag.Length > 0)
        {
            int max = ArrayMax(sources.RareTag);
            var stream = ctx.CreateRareTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareTag, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.UncommonTag.Length > 0)
        {
            int max = ArrayMax(sources.UncommonTag);
            var stream = ctx.CreateUncommonTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonTag, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.CommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.CommonShopJokers);
            var stream = ctx.CreateCommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.CommonShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.UncommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.UncommonShopJokers);
            var stream = ctx.CreateUncommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.RareShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.RareShopJokers);
            var stream = ctx.CreateRareShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        if (sources.AllShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.AllShopJokers);
            var stream = ctx.CreateShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.AllShopJokers, roll))
                    count += MatchJoker(item, targetTypes, edition, stickers);
            }
        }

        return count;
    }

    private static int CountJokerOccurrencesWildcard(
        ref MotelySingleSearchContext ctx,
        int[] antes,
        JokerSourceConfig sources,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState
    )
    {
        int count = 0;
        var shopItems = sources.ShopItems;
        var boosterPacks = sources.BoosterPacks;

        int maxShop = ArrayMax(shopItems);
        int userMaxPack = ArrayMax(boosterPacks);

        foreach (int ante in antes)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);

            if (shopItems.Length > 0)
            {
                var shopStream = ctx.CreateShopItemStream(ante);
                for (int slot = 0; slot <= maxShop; slot++)
                {
                    var item = ctx.GetNextShopItem(ref shopStream);
                    if (!ArrayContains(shopItems, slot))
                        continue;
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                }
            }

            if (boosterPacks.Length > 0)
            {
                var packStream = ctx.CreateBoosterPackStream(ante);
                var jokerStream = ctx.CreateBuffoonPackJokerStream(ante);
                for (int packIndex = 0; packIndex <= maxPack; packIndex++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    if (pack.GetPackType() != MotelyBoosterPackType.Buffoon)
                        continue;
                    var packSize = pack.GetPackSize();
                    var contents = ctx.GetNextBuffoonPackContents(
                        ref jokerStream,
                        packSize
                    );
                    if (
                        !ArrayContains(boosterPacks, packIndex)
                        || (sources.RequireMegaPack && packSize != MotelyBoosterPackSize.Mega)
                    )
                        continue;
                    for (int i = 0; i < contents.Length; i++)
                    {
                        count += MatchJokerWildcard(contents[i], wildcardRarity, edition, stickers);
                    }
                }
            }

            count += CountSpecialtyJokerSourcesWildcard(
                ref ctx,
                ante,
                sources,
                wildcardRarity,
                edition,
                stickers,
                runState
            );
        }

        return count;
    }

    private static int CountSpecialtyJokerSourcesWildcard(
        ref MotelySingleSearchContext ctx,
        int ante,
        JokerSourceConfig sources,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState
    )
    {
        int count = 0;

        if (sources.Judgement.Length > 0)
        {
            int max = ArrayMax(sources.Judgement);
            var stream = ctx.CreateJudgementJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Judgement, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.Wraith.Length > 0)
        {
            int max = ArrayMax(sources.Wraith);
            var stream = ctx.CreateWraithJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.Wraith, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.RiffRaff.Length > 0)
        {
            int max = ArrayMax(sources.RiffRaff);
            var stream = ctx.CreateRiffRaffJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RiffRaff, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.RareTag.Length > 0)
        {
            int max = ArrayMax(sources.RareTag);
            var stream = ctx.CreateRareTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareTag, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.UncommonTag.Length > 0)
        {
            int max = ArrayMax(sources.UncommonTag);
            var stream = ctx.CreateUncommonTagJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonTag, roll))
                {
                    int matches = MatchJokerWildcard(item, wildcardRarity, edition, stickers);
                    count += matches;
                }
            }
        }

        if (sources.CommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.CommonShopJokers);
            var stream = ctx.CreateCommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.CommonShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        if (sources.UncommonShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.UncommonShopJokers);
            var stream = ctx.CreateUncommonShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.UncommonShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        if (sources.RareShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.RareShopJokers);
            var stream = ctx.CreateRareShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.RareShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        if (sources.AllShopJokers.Length > 0)
        {
            int max = ArrayMax(sources.AllShopJokers);
            var stream = ctx.CreateShopJokerStream(ante);
            for (int roll = 0; roll <= max; roll++)
            {
                var item = ctx.GetNextJoker(ref stream);
                if (ArrayContains(sources.AllShopJokers, roll))
                    count += MatchJokerWildcard(item, wildcardRarity, edition, stickers);
            }
        }

        return count;
    }

    private static int MatchJoker(
        MotelyItem item,
        MotelyItemType[] targetTypes,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers
    )
    {
        if (item.TypeCategory != MotelyItemTypeCategory.Joker)
            return 0;
        if (edition.HasValue && item.Edition != edition.Value)
            return 0;
        for (int i = 0; i < stickers.Length; i++)
        {
            bool hasSticker = stickers[i] switch
            {
                MotelyJokerSticker.Eternal => item.IsEternal,
                MotelyJokerSticker.Perishable => item.IsPerishable,
                MotelyJokerSticker.Rental => item.IsRental,
                _ => true,
            };
            if (!hasSticker)
                return 0;
        }

        int matches = 0;
        for (int i = 0; i < targetTypes.Length; i++)
            if (item.Type == targetTypes[i])
                matches++;
        return matches;
    }

    private static int MatchJokerWildcard(
        MotelyItem item,
        MotelyJokerRarity? wildcardRarity,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers
    )
    {
        if (item.TypeCategory != MotelyItemTypeCategory.Joker)
            return 0;
        if (
            wildcardRarity.HasValue
            && (MotelyJokerRarity)(item.Value & MotelyGlobals.JokerRarityMask)
                != wildcardRarity.Value
        )
            return 0;
        if (edition.HasValue && item.Edition != edition.Value)
            return 0;
        for (int i = 0; i < stickers.Length; i++)
        {
            bool hasSticker = stickers[i] switch
            {
                MotelyJokerSticker.Eternal => item.IsEternal,
                MotelyJokerSticker.Perishable => item.IsPerishable,
                MotelyJokerSticker.Rental => item.IsRental,
                _ => true,
            };
            if (!hasSticker)
                return 0;
        }
        return 1;
    }

    private static int MatchStandardCard(MotelyItem item, StandardCardClause clause)
    {
        if (item.TypeCategory != MotelyItemTypeCategory.Standardcard)
            return 0;
        if (clause.Rank.HasValue && item.StandardcardRank != clause.Rank.Value)
            return 0;
        if (clause.Suit.HasValue && item.StandardcardSuit != clause.Suit.Value)
            return 0;
        if (clause.Enhancement.HasValue && item.Enhancement != clause.Enhancement.Value)
            return 0;
        if (clause.Seal.HasValue && item.Seal != clause.Seal.Value)
            return 0;
        if (clause.Edition.HasValue && item.Edition != clause.Edition.Value)
            return 0;
        return 1;
    }

    private static int MatchTarot(MotelyItem item, TarotCardClause clause)
    {
        var tarots = JamlDisc.OrEmpty(clause.Tarots);
        if (JamlDisc.IsCategoryAny(tarots))
            return item.TypeCategory == MotelyItemTypeCategory.TarotCard ? 1 : 0;
        for (int i = 0; i < tarots.Length; i++)
            if (
                item.Type
                == (MotelyItemType)((int)MotelyItemTypeCategory.TarotCard | (int)tarots[i])
            )
                return 1;
        return 0;
    }

    private static int MatchSpectral(MotelyItem item, SpectralCardClause clause)
    {
        var spectrals = JamlDisc.OrEmpty(clause.Spectrals);
        if (JamlDisc.IsCategoryAny(spectrals))
            return item.TypeCategory == MotelyItemTypeCategory.SpectralCard ? 1 : 0;
        for (int i = 0; i < spectrals.Length; i++)
        {
            var Spectral = spectrals[i];
            if (
                item.Type
                == (MotelyItemType)((int)MotelyItemTypeCategory.SpectralCard | (int)Spectral)
            )
                return 1;
            if (Spectral == MotelySpectralCard.TheSoul && item.Type == MotelyItemType.TheSoul)
                return 1;
            if (Spectral == MotelySpectralCard.BlackHole && item.Type == MotelyItemType.BlackHole)
                return 1;
        }
        return 0;
    }

    private static int MatchPlanet(MotelyItem item, PlanetCardClause clause)
    {
        var planets = JamlDisc.OrEmpty(clause.Planets);
        if (JamlDisc.IsCategoryAny(planets))
            return item.TypeCategory == MotelyItemTypeCategory.PlanetCard ? 1 : 0;
        for (int i = 0; i < planets.Length; i++)
            if (
                item.Type
                == (MotelyItemType)((int)MotelyItemTypeCategory.PlanetCard | (int)planets[i])
            )
                return 1;
        return 0;
    }

    private static int GetMaxAnte(IJamlClause clause)
    {
        return clause switch
        {
            JokerClause c => ArrayMax(c.Antes),
            CommonJokerClause c => ArrayMax(c.Antes),
            UncommonJokerClause c => ArrayMax(c.Antes),
            RareJokerClause c => ArrayMax(c.Antes),
            LegendaryJokerClause c => ArrayMax(c.Antes),
            VoucherClause c => ArrayMax(c.Antes),
            TarotCardClause c => ArrayMax(c.Antes),
            SpectralCardClause c => ArrayMax(c.Antes),
            PlanetCardClause c => ArrayMax(c.Antes),
            BossClause c => ArrayMax(c.Antes),
            TagClause c => ArrayMax(c.Antes),
            BoosterPackClause c => ArrayMax(c.Antes),
            StandardCardClause c => ArrayMax(c.Antes),
            ErraticRankClause c => ArrayMax(c.Antes),
            ErraticSuitClause c => ArrayMax(c.Antes),
            StartingDrawClause c => ArrayMax(c.Antes),
            PokerHandClause c => ArrayMax(c.Antes),
            AndClause c => MaxNestedAnte(c.Clauses),
            OrClause c => MaxNestedAnte(c.Clauses),
            _ => 0,
        };
    }

    private static int MaxNestedAnte(IJamlClause[] clauses)
    {
        int max = 0;
        for (int i = 0; i < clauses.Length; i++)
        {
            int nestedMax = GetMaxAnte(clauses[i]);
            if (nestedMax > max)
                max = nestedMax;
        }
        return max;
    }

    // The highest ante any BOSS clause in this tree asks about, or 0 for a tree holding none.
    // PrepareRunState sizes CachedBosses from this, so it has to descend into and:/or: the same
    // way GetMaxAnte does — a boss clause only ever reachable through a conjunction still gets
    // its ante scored, and CountBossOccurrences indexes CachedBosses by that ante directly.
    private static int GetMaxBossAnte(IJamlClause clause) =>
        clause switch
        {
            BossClause c => ArrayMax(c.Antes),
            AndClause c => MaxNestedBossAnte(c.Clauses),
            OrClause c => MaxNestedBossAnte(c.Clauses),
            _ => 0,
        };

    private static int MaxNestedBossAnte(IJamlClause[] clauses)
    {
        int max = 0;
        for (int i = 0; i < clauses.Length; i++)
        {
            int nestedMax = GetMaxBossAnte(clauses[i]);
            if (nestedMax > max)
                max = nestedMax;
        }
        return max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampBoosterPackSlotForAnte(
        int ante,
        int requestedMaxPack,
        MotelyRunState runState
    )
    {
        int anteMaxPack =
            ante == 1 && !runState.IsExtendedPackAnteActive(ante)
                ? MotelyGlobals.EarlyAnteMaxPackSlot
                : MotelyGlobals.LateAntesMaxPackSlot;
        return requestedMaxPack < anteMaxPack ? requestedMaxPack : anteMaxPack;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ArrayMax(int[] array)
    {
        if (array.Length == 0)
            return 0;
        int max = array[0];
        for (int i = 1; i < array.Length; i++)
            if (array[i] > max)
                max = array[i];
        return max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ArrayContains(int[] array, int value)
    {
        for (int i = 0; i < array.Length; i++)
            if (array[i] == value)
                return true;
        return false;
    }
}
