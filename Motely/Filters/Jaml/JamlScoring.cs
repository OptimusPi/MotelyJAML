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

    internal static bool IsExactFilterConfirm(IJamlClause clause) =>
        clause switch
        {
            BossClause => true,
            StartingDrawClause => true,
            PokerHandClause => true,
            StandardCardClause => true,
            LegendaryJokerClause => false,
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
            JokerClause jc => JokerUsesLegendaryExactPath(jc)
                || jc.Sources is { RequireMegaPack: true },
            CommonJokerClause cjc => cjc.Sources is { RequireMegaPack: true },
            UncommonJokerClause ujc => ujc.Sources is { RequireMegaPack: true },
            RareJokerClause rjc => rjc.Sources is { RequireMegaPack: true },
            AndClause a => AllExactFilterConfirm(a.Clauses),
            OrClause o => AllExactFilterConfirm(o.Clauses),
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool MeetsOccurrenceBounds(int raw, IJamlClause clause)
    {
        if (raw < clause.Min)
            return false;
        if (clause.Max is { } max && raw > max)
            return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CapScoreCount(int count, IJamlClause clause)
    {
        var max = clause.Max;
        return max is { } m && count > m ? m : count;
    }

    private static int UnhandledClauseForScoring(IJamlClause clause)
    {
        Debug.Assert(
            false,
            $"JamlScoring.CountOccurrences: unhandled clause type {clause.GetType().Name} (extend switch or exclude from should-clauses)."
        );
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ChildMatches(int count, IJamlClause child) =>
        child is LogicClause ? count > 0 : MeetsOccurrenceBounds(count, child);

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

        int combos = int.MaxValue;
        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountOccurrencesUncapped(ref ctx, clause.Clauses[i], runState);
            if (!ChildMatches(count, clause.Clauses[i]))
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
        bool useMax = clause.Mode == JamlLogicScoreMode.Max;

        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountOccurrencesUncapped(ref ctx, clause.Clauses[i], runState);
            if (!ChildMatches(count, clause.Clauses[i]))
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int OrArmWeight(IJamlClause arm) => arm.Score != 0 ? arm.Score : 1;

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

        int combos = int.MaxValue;
        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountRawOccurrences(ref ctx, clause.Clauses[i], runState);
            if (!ChildMatches(count, clause.Clauses[i]))
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
        bool useMax = clause.Mode == JamlLogicScoreMode.Max;

        for (int i = 0; i < clause.Clauses.Length; i++)
        {
            int count = CountRawOccurrences(ref ctx, clause.Clauses[i], runState);
            if (!ChildMatches(count, clause.Clauses[i]))
                continue;
            matched++;
            total += count;
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
        Debug.Assert(
            clause.Bosses.Length > 0,
            "BossClause.Bosses must be non-empty after JAML load (validator / loader bug)."
        );
        Debug.Assert(
            clause.Antes.Length > 0,
            "BossClause.Antes must be non-empty after JAML load (validator / loader bug)."
        );

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
            for (int i = 0; i < clause.Bosses.Length; i++)
                if (clause.Bosses[i] == bosses[ante])
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

        if (sources.BoosterPacks.Length > 0)
        {
            if (SpectralClauseTargets(clause, MotelySpectralCard.TheSoul))
                count += CountTheSoulInArcanaPacks(ref ctx, clause, runState);
            if (SpectralClauseTargets(clause, MotelySpectralCard.BlackHole))
                count += CountBlackHoleInCelestialPacks(ref ctx, clause, runState);
        }

        return count;
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SpectralClauseTargets(SpectralCardClause clause, MotelySpectralCard card)
    {
        var spectrals = JamlDisc.OrEmpty(clause.Spectrals);
        for (int i = 0; i < spectrals.Length; i++)
            if (spectrals[i] == card)
                return true;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SpectralCardSourceConfig ResolveSpectralSources(SpectralCardClause clause) =>
        clause.Sources
        ?? (
            TargetsSpecialSpectral(clause)
                ? SpectralCardFilterDesc.DefaultSpecialSources
                : SpectralCardFilterDesc.DefaultSources
        );

    public static bool TargetsSpecialSpectral(SpectralCardClause clause) =>
        SpectralClauseTargets(clause, MotelySpectralCard.TheSoul)
        || SpectralClauseTargets(clause, MotelySpectralCard.BlackHole);

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
                if (roll < 0 || roll >= PokerHandFilterDesc.MaxBlindsPerAnte)
                    continue;

                MotelyItem[] deck = new MotelyItem[MotelyEnum<MotelyStandardCard>.ValueCount];
                for (int i = 0; i < deck.Length; i++)
                    deck[i] = new(MotelyEnum<MotelyStandardCard>.Values[i]);

                ctx.Shuffle(shuffleKey, deck, roll);
                int handSize = Math.Min(8, deck.Length);
                Span<MotelyItem> hand = deck.AsSpan(deck.Length - handSize, handSize);
                MotelyPokerHand best = MotelyPokerHandEval.BestScore(hand).Type;

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
