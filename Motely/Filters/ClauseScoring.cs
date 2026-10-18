using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely.Filters;

public static class ClauseScoring
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PrepareRunState(
        ref MotelySingleSearchContext ctx,
        IMotelyClause[] clauses,
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
        IMotelyClause clause
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
    internal static bool IsExactFilterConfirm(IMotelyClause clause) =>
        clause switch
        {
            BossClause => true,
            StartingDrawClause => true,
            PokerHandClause => true,
            StandardCardClause => true,
            // Legendary filter is pure SIMD edition (or passthrough) — pack/Soul confirm is score-only.
            LegendaryJokerClause => false,
            // soulOrder threads one run-wide Joker4 stream across antes; only the scalar walk knows it.
            LegendarySoulOrderClause => false,
            // Full vector roll walks — same law as scoring counts.
            VoucherClause => true,
            TagClause => true,
            BoosterPackClause => true,
            // The erratic and event SIMD filters count up to min only (and stop early once every
            // lane is decided), so they are the scoring law only while there is no max to enforce.
            ErraticRankClause or ErraticSuitClause => clause.Max is null,
            SpectralCardClause sc => SpecialSpectralCardFilterDesc.Handles(sc)
                || sc.Sources is { RequireMegaPack: true }
                || sc.Sources is { EtherealTag: true }
                || sc.Sources is { OmenGlobe: true },
            TarotCardClause tc => tc.Sources is { CharmTag: true }
                || tc.Sources is { RequireMegaPack: true }
                || tc.Sources is { TagPacks.Length: > 0 },
            PlanetCardClause pc => pc.Sources is { RequireMegaPack: true },
            // The shop/pack SIMD walk over-accepts in-pack duplicates as "undecided" (see
            // BuffoonPackDuplicateTracker), so no named-joker lane is ever confirmed by the
            // vector walk: scoring's scalar re-eval must always see them. Only the legendary /
            // category-any path, which runs SearchIndividualSeeds, is exact. A requireMegaPack
            // joker clause is deliberately NOT exempt — claiming it exact let undecided lanes
            // through `must` and made `mustNot` negate the coarse prefilter.
            JokerClause jc => JokerUsesLegendaryExactPath(jc),
            CommonJokerClause => false,
            UncommonJokerClause => false,
            RareJokerClause => false,
            AndClause a => AllExactFilterConfirm(a.Clauses),
            OrClause o => AllExactFilterConfirm(o.Clauses),
            // Event filters count the same absolute roll indices as scoring, min and max both.
            EventClause => true,
            _ => false,
        };

    private static bool AllExactFilterConfirm(IMotelyClause[] clauses)
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
        if ((clause.Jokers.Length == 0))
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
    internal static bool CanSkipMustReeval(IMotelyClause[] mustClauses)
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
        IMotelyClause clause,
        MotelyRunState runState
    )
    {
        var count = CountOccurrencesUncapped(ref ctx, clause, runState);
        return CapScoreCount(count, clause);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountOccurrencesUncapped(
        ref MotelySingleSearchContext ctx,
        IMotelyClause clause,
        MotelyRunState runState
    )
    {
        // A clause's with: is the run-wide authority for luck + held jokers (incl. Showman); push it
        // into the shared run state so the event counters and the legendary walk read one source.
        if (clause is IWithScopedClause withScoped)
            runState.ApplyWith(withScoped.With);

        return clause switch
        {
            JokerClause c => CountJokerOccurrences(ref ctx, c, runState),
            CommonJokerClause c => CountCommonJokerOccurrences(ref ctx, c, runState),
            UncommonJokerClause c => CountUncommonJokerOccurrences(ref ctx, c, runState),
            RareJokerClause c => CountRareJokerOccurrences(ref ctx, c, runState),
            LegendaryJokerClause c => CountLegendaryJokerOccurrences(ref ctx, c, runState),
            LegendarySoulOrderClause c => CountLegendarySoulOrderOccurrences(ref ctx, c, runState),
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
            WheelOfFortuneClause c => CountWheelOfFortuneOccurrences(ref ctx, c, runState),
            CavendishExtinctClause c => CountCavendishExtinctOccurrences(ref ctx, c, runState),
            GrosMichelExtinctClause c => CountGrosMichelExtinctOccurrences(ref ctx, c, runState),
            SpaceLevelupClause c => CountSpaceLevelupOccurrences(ref ctx, c, runState),
            BusinessPayoutClause c => CountBusinessPayoutOccurrences(ref ctx, c),
            BloodstoneTriggerClause c => CountBloodstoneTriggerOccurrences(ref ctx, c),
            ParkingPayoutClause c => CountParkingPayoutOccurrences(ref ctx, c),
            GlassDestroyClause c => CountGlassDestroyOccurrences(ref ctx, c, runState),
            WheelStaysFlippedClause c => CountWheelStaysFlippedOccurrences(ref ctx, c, runState),
            StartingDrawClause c => CountStartingDrawOccurrences(ref ctx, c),
            PokerHandClause c => CountPokerHandOccurrences(ref ctx, c),
            AndClause c => CountAndOccurrences(ref ctx, c, runState),
            OrClause c => CountOrOccurrences(ref ctx, c, runState),
            _ => UnhandledClauseForScoring(clause),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CapScoreCountForTesting(int count, IMotelyClause clause) =>
        CapScoreCount(count, clause);

    /// <summary>
    /// Match bounds: <see cref="IMotelyClause.Min"/> is the floor and <see cref="IMotelyClause.Max"/>,
    /// when set, the ceiling; null is the only "no ceiling". Score tallies use <see cref="CapScoreCount"/> instead, so a
    /// should column's contribution is capped rather than gated. SIMD prefilters may stay
    /// over-permissive on max; scoring and the exact confirm paths enforce it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool MeetsOccurrenceBounds(int raw, IMotelyClause clause)
    {
        if (raw < clause.Min)
            return false;
        if (clause.Max is { } max && raw > max)
            return false;
        return true;
    }

    /// <summary>
    /// Whether a counted clause matched. A plain clause matches when its occurrence count sits
    /// inside its own <c>min</c>/<c>max</c>. An <c>and:</c>/<c>or:</c> gates itself (it returns
    /// 0 below its arm-count <c>min</c>) and returns an aggregate, not an occurrence count, so
    /// for it any positive return is a match: its <c>min</c> is never re-applied as a floor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Matches(int raw, IMotelyClause clause) =>
        clause is LogicClause ? raw > 0 : MeetsOccurrenceBounds(raw, clause);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CapScoreCount(int count, IMotelyClause clause)
    {
        var max = clause.Max;
        return max is { } m && count > m ? m : count;
    }

    private static int UnhandledClauseForScoring(IMotelyClause clause)
    {
        Debug.Assert(
            false,
            $"ClauseScoring.CountOccurrences: unhandled clause type {clause.GetType().Name} (extend switch or exclude from should-clauses)."
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
            int count = CountOccurrencesUncapped(ref ctx, clause.Clauses[i], runState);
            if (!Matches(count, clause.Clauses[i]))
                return 0;
            count = CapScoreCount(count, clause.Clauses[i]);
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
        bool useMax = clause.Mode == LogicScoreMode.Max;

        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountOccurrencesUncapped(ref ctx, clause.Clauses[i], runState);
            if (!Matches(count, clause.Clauses[i]))
                continue;
            matched++;
            int contribution = CapScoreCount(count, clause.Clauses[i]) * OrArmWeight(clause.Clauses[i]);
            total += contribution;
            if (contribution > best)
                best = contribution;
        }

        if (matched < clause.Min)
            return 0;

        int aggregate = useMax ? best : total;
        return clause.Score != 0 ? aggregate : matched;
    }

    /// <summary>An unscored arm weighs 1, the same default the loader gives a bare should clause.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int OrArmWeight(IMotelyClause arm) => arm.Score != 0 ? arm.Score : 1;

    public static int CountRawOccurrences(
        ref MotelySingleSearchContext ctx,
        IMotelyClause clause,
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
            if (!Matches(count, clause.Clauses[i]))
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
        int bestCount = 0;
        int bestContribution = 0;
        bool useMax = clause.Mode == LogicScoreMode.Max;

        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountRawOccurrences(ref ctx, clause.Clauses[i], runState);
            if (!Matches(count, clause.Clauses[i]))
                continue;
            matched++;
            total += count;
            // mode: max picks the arm with the largest count×weight (the arm CountOrOccurrences
            // scores) and reports that arm's raw count, so tally and score describe one arm.
            int contribution = count * OrArmWeight(clause.Clauses[i]);
            if (contribution > bestContribution)
            {
                bestContribution = contribution;
                bestCount = count;
            }
        }

        if (matched < clause.Min)
            return 0;

        int aggregate = useMax ? bestCount : total;
        return clause.Score != 0 ? aggregate : matched;
    }

    private static int CountBossOccurrences(BossClause clause, MotelyRunState runState)
    {
        // Not Debug.Assert: ApplyPrepareRunState allocates CachedBosses only when some clause
        // named a boss ante >= 1, so a clause that slipped past the loader must fail with the
        // cause in Release too, not with a null dereference.
        var bosses =
            runState.CachedBosses
            ?? throw new InvalidOperationException(
                "Boss scoring ran before PrepareRunState cached any boss: no boss ante >= 1 was in the plan."
            );

        int count = 0;
        foreach (int ante in clause.Antes)
        {
            if (ante < 1 || ante >= bosses.Length)
                throw new InvalidOperationException(
                    $"Boss ante {ante} is outside the cached range 1..{bosses.Length - 1}; bosses start at ante 1."
                );
            if (Names(clause.Bosses, bosses[ante]))
                count++;
        }
        return count;
    }

    /// <summary>An item list names a value when it holds it; an empty list (<c>any</c>) names them all.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Names<T>(T[] wanted, T value)
        where T : struct, Enum =>
        wanted.Length == 0 || Array.IndexOf(wanted, value) >= 0;

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

            var tarotStream = ctx.CreateArcanaPackTarotStream(ante);

            // Charm Tag Mega Arcanas open at blind select on the same ar1 stream: before every
            // shop pack in ante 1, after the previous boss's shop (slots 0..1) in antes 2+.
            bool tagPending = sources.TagPacks.Length > 0;
            int tagSlot = tagPending ? MotelyGlobals.TagPackInsertSlot(ante) : -1;
            if (tagPending && tagSlot == 0)
            {
                tagPending = false;
                count += CountTarotTagPacks(ref ctx, ref tarotStream, sources, clause);
            }

            if (sources.BoosterPacks.Length > 0 || tagPending)
            {
                bool charmWant = sources.CharmTag;

                var packStream = ctx.CreateBoosterPackStream(ante);
                bool hadNaturalArcanaPack = false;
                int weightedShopDrawNumber = 0;

                for (int packIndex = 0; ; packIndex++)
                {
                    if (tagPending && packIndex == tagSlot)
                    {
                        tagPending = false;
                        count += CountTarotTagPacks(ref ctx, ref tarotStream, sources, clause);
                    }

                    bool needForClause =
                        sources.BoosterPacks.Length > 0 && packIndex <= maxPack;
                    bool needForCharmClosure = charmWant && weightedShopDrawNumber < 2;
                    if (!needForClause && !needForCharmClosure && !tagPending)
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

    /// <summary>
    /// The ante's Charm Tag Mega Arcanas, in order (0 = first Charm, 1 = second; listing 1 assumes
    /// Charm 0 was opened too). Each takes the next five cards of <paramref name="tarotStream"/>.
    /// </summary>
    private static int CountTarotTagPacks(
        ref MotelySingleSearchContext ctx,
        ref MotelySingleTarotStream tarotStream,
        TarotCardSourceConfig sources,
        TarotCardClause clause
    )
    {
        int count = 0;
        int maxTagPack = ArrayMax(sources.TagPacks);
        for (int tagPack = 0; tagPack <= maxTagPack; tagPack++)
        {
            var contents = ctx.GetNextArcanaPackContents(ref tarotStream, MotelyBoosterPackSize.Mega);
            if (!ArrayContains(sources.TagPacks, tagPack))
                continue;
            for (int i = 0; i < contents.Length; i++)
                count += MatchTarot(contents[i], clause);
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
        var spectrals = clause.Spectrals;
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
        int maxVoucherRoll = clause.Rolls.Max();
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
                    if (Names(clause.Vouchers, rolled))
                        count++;
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
        int maxDraw = clause.Rolls.Max();
        Span<MotelyTag> draws = stackalloc MotelyTag[maxDraw + 1];

        foreach (int ante in clause.Antes)
        {
            var tagStream = ctx.CreateTagStream(ante);
            for (int i = 0; i <= maxDraw; i++)
                draws[i] = ctx.GetNextTag(ref tagStream);

            foreach (var drawIndex in clause.Rolls)
                if (Names(clause.Tags, draws[drawIndex]))
                    count++;
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
        int userMaxPack = (clause.Rolls.Length == 0 ? -1 : clause.Rolls.Max());
        bool anyPack = (clause.Packs.Length == 0);

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

    // ── Event clauses ──
    // `rolls` are absolute indices into the event's own PRNG stream: roll i is the i-th pull,
    // the same index the SIMD event filters walk (sorted, 0..max) and the Jamlyzer's event queues
    // report. Each requested index counts once, and the whole count is returned: the tally
    // reports every hit, and min/max are judged by the caller (MeetsOccurrenceBounds / should
    // min-gating), never cut short here.

    private static int CountLuckyMoneyOccurrences(
        ref MotelySingleSearchContext ctx,
        LuckyMoneyClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateLuckyCardMoneyStream(isCached: false);
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextLuckyMoney(ref stream, luck) && ArrayContains(clause.Rolls, roll))
                count++;
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
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextLuckyMult(ref stream, luck) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountMisprintMultOccurrences(
        ref MotelySingleSearchContext ctx,
        MisprintMultClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateMisprintPrngStream();
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextMisprintMult(ref stream) >= clause.Mult && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountWheelOfFortuneOccurrences(
        ref MotelySingleSearchContext ctx,
        WheelOfFortuneClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateWheelOfFortuneStream();
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (
                ctx.GetNextWheelOfFortune(ref stream, luck) != MotelyItemEdition.None
                && ArrayContains(clause.Rolls, roll)
            )
                count++;
        return count;
    }

    private static int CountCavendishExtinctOccurrences(
        ref MotelySingleSearchContext ctx,
        CavendishExtinctClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateCavendishPrngStream(false);
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextCavendishExtinct(ref stream, luck) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountGrosMichelExtinctOccurrences(
        ref MotelySingleSearchContext ctx,
        GrosMichelExtinctClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateGrosMichelPrngStream(false);
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextGrosMichelExtinct(ref stream, luck) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountSpaceLevelupOccurrences(
        ref MotelySingleSearchContext ctx,
        SpaceLevelupClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateSpacePrngStream();
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextSpaceLevelup(ref stream, luck) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountBusinessPayoutOccurrences(
        ref MotelySingleSearchContext ctx,
        BusinessPayoutClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateBusinessPrngStream();
        // Flat 50/50 (Chance = 2) — no luck/Oops multiplier.
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextBusinessPayout(ref stream) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountBloodstoneTriggerOccurrences(
        ref MotelySingleSearchContext ctx,
        BloodstoneTriggerClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateBloodstonePrngStream();
        // Flat 50/50 (Chance = 2) — no luck/Oops multiplier.
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextBloodstoneTrigger(ref stream) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountParkingPayoutOccurrences(
        ref MotelySingleSearchContext ctx,
        ParkingPayoutClause clause
    )
    {
        int count = 0;
        var stream = ctx.CreateParkingPrngStream();
        // Flat 50/50 (Chance = 2) — no luck/Oops multiplier.
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextParkingPayout(ref stream) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountGlassDestroyOccurrences(
        ref MotelySingleSearchContext ctx,
        GlassDestroyClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateGlassPrngStream();
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextGlassDestroy(ref stream, luck) && ArrayContains(clause.Rolls, roll))
                count++;
        return count;
    }

    private static int CountWheelStaysFlippedOccurrences(
        ref MotelySingleSearchContext ctx,
        WheelStaysFlippedClause clause,
        MotelyRunState runState
    )
    {
        int count = 0;
        var stream = ctx.CreateTheWheelPrngStream();
        double luck = (double)runState.Luck;
        int maxRoll = ArrayMax(clause.Rolls);
        for (int roll = 0; roll <= maxRoll; roll++)
            if (ctx.GetNextWheelStaysFlipped(ref stream, luck) && ArrayContains(clause.Rolls, roll))
                count++;
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

        // One Joker4 walk for the whole run: Souls are opened in ante order, so antes go ascending
        // whatever order the clause lists them in.
        var walk = new LegendarySoulMatcher.SoulWalk(ctx.CreateRunLegendaryJokerStream());
        int lastAnte = ArrayMax(clause.Antes);
        for (int ante = 0; ante <= lastAnte && !walk.Done; ante++)
        {
            if (!ArrayContains(clause.Antes, ante))
                continue;
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            count += LegendarySoulMatcher.CountAnte(ref ctx, ante, clause, maxPack, ref walk, runState);
        }

        return count;
    }

    /// <summary>
    /// <c>soulOrder:</c> — opens the run's Souls in ante then pack order with ONE run-wide
    /// <c>Joker4</c> stream (the same walk <see cref="LegendarySoulMatcher"/> uses), and checks the
    /// Nth Soul against the Nth listed entry. Returns 1 when the whole ordered list matches, else 0.
    /// A legendary already opened (or in <c>with: { jokers: [...] }</c>) stays owned (resample to
    /// <c>Joker4_resample{n}</c>) unless <c>with:</c> holds Showman.
    /// </summary>
    private static int CountLegendarySoulOrderOccurrences(
        ref MotelySingleSearchContext ctx,
        LegendarySoulOrderClause clause,
        MotelyRunState runState
    )
    {
        if (clause.Entries.Length == 0)
            return 0;

        int[] sourcePacks =
            clause.BoosterPacks.Length > 0
                ? clause.BoosterPacks
                : LegendaryJokerFilterDesc.DefaultSources.BoosterPacks;
        int userMaxPack = ArrayMax(sourcePacks);
        int maxAnte = clause.MaxAnte > 0 ? clause.MaxAnte : 8;

        var faces = ctx.CreateRunLegendaryJokerStream();
        MotelyJoker[] withJokers = clause.With?.Jokers ?? [];
        Span<MotelyJoker> owned = stackalloc MotelyJoker[MotelyGlobals.MaxVectorWidth];
        int ownedCount = 0;
        Span<MotelyJoker> held = stackalloc MotelyJoker[
            withJokers.Length + MotelyGlobals.MaxVectorWidth
        ];
        withJokers.CopyTo(held);
        int entry = 0;

        // Ante 0 is in the run only after Hieroglyph or Petroglyph: its Souls are opened only when
        // an entry names it, as the Jamlyzer does (MotelyJamlyzer.OpensAnteZeroSouls).
        int firstAnte = Array.Exists(clause.Entries, e => Array.IndexOf(e.Antes, 0) >= 0) ? 0 : 1;
        for (int ante = firstAnte; ante <= maxAnte && entry < clause.Entries.Length; ante++)
        {
            int maxPack = ClampBoosterPackSlotForAnte(ante, userMaxPack, runState);
            var packStream = ctx.CreateBoosterPackStream(ante);
            MotelySingleTarotStream tarotStream = default;
            MotelySingleSpectralStream spectralStream = default;
            bool tarotInit = false;
            bool spectralInit = false;

            for (int p = 0; p <= maxPack && entry < clause.Entries.Length; p++)
            {
                var pack = ctx.GetNextBoosterPack(ref packStream);
                var type = pack.GetPackType();

                bool hasSoul;
                if (type == MotelyBoosterPackType.Arcana)
                {
                    if (!tarotInit)
                    {
                        tarotInit = true;
                        tarotStream = ctx.CreateArcanaPackTarotStream(ante, true);
                    }

                    hasSoul = ctx.GetNextArcanaPackHasTheSoul(ref tarotStream, pack.GetPackSize());
                }
                else if (type == MotelyBoosterPackType.Spectral)
                {
                    if (!spectralInit)
                    {
                        spectralInit = true;
                        spectralStream = ctx.CreateSpectralPackSpectralStream(ante, soulOnly: ante != 1);
                    }

                    hasSoul = ctx.GetNextSpectralPackHasTheSoul(
                        ref spectralStream,
                        pack.GetPackSize()
                    );
                }
                else
                {
                    continue;
                }

                if (!hasSoul || !ArrayContains(sourcePacks, p))
                    continue;

                owned.Slice(0, ownedCount).CopyTo(held[withJokers.Length..]);
                ReadOnlySpan<MotelyJoker> heldNow = held[..(withJokers.Length + ownedCount)];
                MotelyJoker face = ctx.GetNextLegendaryJoker(
                    ref faces,
                    heldNow,
                    runState.ShowmanActive
                );
                if (ownedCount < owned.Length)
                    owned[ownedCount++] = face;

                var expected = clause.Entries[entry];
                if (face != expected.Legendary)
                    return 0;
                if (expected.Antes.Length > 0 && !ArrayContains(expected.Antes, ante))
                    return 0;
                entry++;
            }
        }

        return entry == clause.Entries.Length ? 1 : 0;
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
        if ((clause.Jokers.Length == 0))
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
        if ((clause.Jokers.Length == 0))
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
        if ((clause.Jokers.Length == 0))
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
        if ((clause.Jokers.Length == 0))
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

            var wildcardLegendarySources = LegendarySourcesFor(clause);
            if (wildcardLegendarySources is null)
                return normalWildcard;

            var legendaryWildcard = new LegendaryJokerClause
            {
                Label = clause.Label,
                Score = clause.Score,
                Jokers = [],
                Edition = clause.Edition,
                Sources = wildcardLegendarySources,
                Antes = clause.Antes,
                Min = clause.Min,
                Max = clause.Max,
            };

            return normalWildcard
                + CountLegendaryJokerOccurrences(ref ctx, legendaryWildcard, runState);
        }

        var (nonLegendaryTypes, legendary) = SplitByRarity(clause.Jokers);

        int count = 0;

        if (nonLegendaryTypes.Length > 0)
        {
            count += CountJokerOccurrencesOfTypes(
                ref ctx,
                clause.Antes,
                sources,
                nonLegendaryTypes,
                clause.Edition,
                clause.Stickers,
                runState
            );
        }

        if (legendary.Length > 0 && LegendarySourcesFor(clause) is { } legendarySources)
        {
            var legendaryClause = new LegendaryJokerClause
            {
                Label = clause.Label,
                Score = clause.Score,
                Jokers = legendary,
                Edition = clause.Edition,
                Sources = legendarySources,
                Antes = clause.Antes,
                Min = clause.Min,
                Max = clause.Max,
            };

            count += CountLegendaryJokerOccurrences(ref ctx, legendaryClause, runState);
        }

        return count;
    }

    /// <summary>
    /// Where a <c>joker:</c> clause looks for legendaries (Soul cards in Arcana/Spectral packs).
    /// <c>legendarySources:</c> wins when given. With no <c>sources:</c> block at all, the legendary
    /// default applies. An explicit <c>sources:</c> block is taken literally: its <c>boosterPacks</c>
    /// slots are the Soul slots, and a block that lists no packs asks for no Souls (null) — a clause
    /// never pulls from a source it did not name.
    /// </summary>
    private static LegendaryJokerSourceConfig? LegendarySourcesFor(JokerClause clause)
    {
        if (clause.LegendarySources is { } explicitLegendary)
            return explicitLegendary;
        if (clause.Sources is null)
            return LegendaryJokerFilterDesc.DefaultSources;
        if (clause.Sources.BoosterPacks.Length == 0)
            return null;
        return new LegendaryJokerSourceConfig
        {
            BoosterPacks = clause.Sources.BoosterPacks,
            RequireMegaPack = clause.Sources.RequireMegaPack,
        };
    }

    /// <summary>
    /// A clause's joker list as item types, mapped once per array rather than once per seed
    /// (the mapping goes through the enum names). Keyed on the clause's own array instance, so a
    /// host that assigns a new <c>Jokers</c> array gets a fresh mapping.
    /// </summary>
    private static class JokerItemTypes<TJoker>
        where TJoker : struct, Enum
    {
        private static readonly ConditionalWeakTable<TJoker[], MotelyItemType[]> Cache = new();

        public static MotelyItemType[] Of(TJoker[] jokers) => Cache.GetValue(jokers, Map);

        private static MotelyItemType[] Map(TJoker[] jokers)
        {
            var types = new MotelyItemType[jokers.Length];
            for (int i = 0; i < jokers.Length; i++)
                types[i] = Enum.Parse<MotelyItemType>(jokers[i].ToString(), true);
            return types;
        }
    }

    /// <summary>A <see cref="JokerClause"/>'s list split by rarity, once per array: legendaries are
    /// counted through the soul path, the rest through shop and buffoon packs.</summary>
    private sealed record JokerRaritySplit(MotelyItemType[] NonLegendaryTypes, MotelyJoker[] Legendary);

    private static readonly ConditionalWeakTable<MotelyJoker[], JokerRaritySplit> JokerSplits = new();

    private static JokerRaritySplit SplitByRarity(MotelyJoker[] jokers) =>
        JokerSplits.GetValue(
            jokers,
            static all =>
            {
                static bool IsLegendary(MotelyJoker j) =>
                    ((MotelyJokerRarity)((int)j & MotelyGlobals.JokerRarityMask))
                    == MotelyJokerRarity.Legendary;

                MotelyJoker[] nonLegendary = Array.FindAll(all, static j => !IsLegendary(j));
                return new(
                    JokerItemTypes<MotelyJoker>.Of(nonLegendary),
                    Array.FindAll(all, IsLegendary)
                );
            }
        );

    private static int CountJokerOccurrencesGeneric<TJoker>(
        ref MotelySingleSearchContext ctx,
        int[] antes,
        JokerSourceConfig sources,
        TJoker[] jokers,
        MotelyItemEdition? edition,
        MotelyJokerSticker[] stickers,
        MotelyRunState runState
    )
        where TJoker : struct, Enum =>
        CountJokerOccurrencesOfTypes(
            ref ctx,
            antes,
            sources,
            JokerItemTypes<TJoker>.Of(jokers),
            edition,
            stickers,
            runState
        );

    private static int CountJokerOccurrencesOfTypes(
        ref MotelySingleSearchContext ctx,
        int[] antes,
        JokerSourceConfig sources,
        MotelyItemType[] targetTypes,
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
        var tarots = clause.Tarots;
        if ((tarots.Length == 0))
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
        var spectrals = clause.Spectrals;
        if ((spectrals.Length == 0))
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
        var planets = clause.Planets;
        if ((planets.Length == 0))
            return item.TypeCategory == MotelyItemTypeCategory.PlanetCard ? 1 : 0;
        for (int i = 0; i < planets.Length; i++)
            if (
                item.Type
                == (MotelyItemType)((int)MotelyItemTypeCategory.PlanetCard | (int)planets[i])
            )
                return 1;
        return 0;
    }

    private static int GetMaxAnte(IMotelyClause clause)
    {
        return clause switch
        {
            JokerClause c => ArrayMax(c.Antes),
            CommonJokerClause c => ArrayMax(c.Antes),
            UncommonJokerClause c => ArrayMax(c.Antes),
            RareJokerClause c => ArrayMax(c.Antes),
            LegendaryJokerClause c => ArrayMax(c.Antes),
            LegendarySoulOrderClause c => c.MaxAnte,
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

    private static int MaxNestedAnte(IMotelyClause[] clauses)
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
    private static int GetMaxBossAnte(IMotelyClause clause) =>
        clause switch
        {
            BossClause c => ArrayMax(c.Antes),
            AndClause c => MaxNestedBossAnte(c.Clauses),
            OrClause c => MaxNestedBossAnte(c.Clauses),
            _ => 0,
        };

    private static int MaxNestedBossAnte(IMotelyClause[] clauses)
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
        // Ante 1 has two shops; a Hieroglyph/Petroglyph bought in ante 2's first shop sends the
        // run back through both of them, so the extended ante 1 offers its normal packs twice over.
        int anteMaxPack =
            ante != 1 ? MotelyGlobals.LateAntesMaxPackSlot
            : runState.IsExtendedPackAnteActive(ante)
                ? 2 * (MotelyGlobals.EarlyAnteMaxPackSlot + 1) - 1
                : MotelyGlobals.EarlyAnteMaxPackSlot;
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
