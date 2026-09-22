namespace Motely.Filters.Jaml;

/// <summary>
/// Runtime dispatch from a populated clause instance to its <see cref="IJamlClauseDesc{TClause}"/>.
/// One switch, next to the descs — not a string phone book of keys.
/// </summary>
internal static class JamlClauseDescDispatch
{
    private static bool SetDisc<TDesc, TClause>(TClause clause, IJamlValueReader value)
        where TDesc : IJamlClauseDesc<TClause>
        where TClause : class, IJamlClause =>
        TDesc.SetDiscriminatorValue(clause, value);

    private static bool SetKey<TDesc, TClause>(TClause clause, string key, IJamlValueReader value)
        where TDesc : IJamlClauseDesc<TClause>
        where TClause : class, IJamlClause =>
        TDesc.Set(clause, key, value);

    private static double Estimate<TDesc, TClause>(TClause clause, in JamlRarityContext ctx)
        where TDesc : IJamlClauseDesc<TClause>
        where TClause : class, IJamlClause =>
        TDesc.EstimateRarity(clause, in ctx);

    /// <summary>
    /// Apply a non-common clause key via the family's <c>Set</c>.
    /// Returns <c>false</c> when this family does not claim the key (caller may fall back).
    /// </summary>
    public static bool TrySet(IJamlClause clause, string key, IJamlValueReader value) =>
        clause switch
        {
            JokerClause c => SetKey<JokerFilterDesc, JokerClause>(c, key, value),
            CommonJokerClause c => SetKey<CommonJokerFilterDesc, CommonJokerClause>(c, key, value),
            UncommonJokerClause c => SetKey<UncommonJokerFilterDesc, UncommonJokerClause>(c, key, value),
            RareJokerClause c => SetKey<RareJokerFilterDesc, RareJokerClause>(c, key, value),
            LegendaryJokerClause c => SetKey<LegendaryJokerFilterDesc, LegendaryJokerClause>(c, key, value),
            VoucherClause c => SetKey<VoucherFilterDesc, VoucherClause>(c, key, value),
            TarotCardClause c => SetKey<TarotCardFilterDesc, TarotCardClause>(c, key, value),
            SpectralCardClause c => SetKey<SpectralCardFilterDesc, SpectralCardClause>(c, key, value),
            PlanetCardClause c => SetKey<PlanetCardFilterDesc, PlanetCardClause>(c, key, value),
            StandardCardClause c => SetKey<StandardCardFilterDesc, StandardCardClause>(c, key, value),
            BossClause c => SetKey<BossFilterDesc, BossClause>(c, key, value),
            TagClause c => SetKey<TagFilterDesc, TagClause>(c, key, value),
            BoosterPackClause c => SetKey<BoosterPackFilterDesc, BoosterPackClause>(c, key, value),
            ErraticRankClause c => SetKey<ErraticRankFilterDesc, ErraticRankClause>(c, key, value),
            ErraticSuitClause c => SetKey<ErraticSuitFilterDesc, ErraticSuitClause>(c, key, value),
            StartingDrawClause c => SetKey<StartingDrawFilterDesc, StartingDrawClause>(c, key, value),
            PokerHandClause c => SetKey<PokerHandFilterDesc, PokerHandClause>(c, key, value),
            LuckyMoneyClause c => SetKey<LuckyMoneyFilterDesc, LuckyMoneyClause>(c, key, value),
            LuckyMultClause c => SetKey<LuckyMultFilterDesc, LuckyMultClause>(c, key, value),
            MisprintMultClause c => SetKey<MisprintMultFilterDesc, MisprintMultClause>(c, key, value),
            WheelOfFortuneClause c => SetKey<WheelOfFortuneFilterDesc, WheelOfFortuneClause>(c, key, value),
            GrosMichelExtinctClause c => SetKey<GrosMichelExtinctFilterDesc, GrosMichelExtinctClause>(c, key, value),
            CavendishExtinctClause c => SetKey<CavendishExtinctFilterDesc, CavendishExtinctClause>(c, key, value),
            SpaceLevelupClause c => SetKey<SpaceLevelupFilterDesc, SpaceLevelupClause>(c, key, value),
            BusinessPayoutClause c => SetKey<BusinessPayoutFilterDesc, BusinessPayoutClause>(c, key, value),
            BloodstoneTriggerClause c => SetKey<BloodstoneTriggerFilterDesc, BloodstoneTriggerClause>(c, key, value),
            ParkingPayoutClause c => SetKey<ParkingPayoutFilterDesc, ParkingPayoutClause>(c, key, value),
            GlassDestroyClause c => SetKey<GlassDestroyFilterDesc, GlassDestroyClause>(c, key, value),
            WheelStaysFlippedClause c => SetKey<WheelStaysFlippedFilterDesc, WheelStaysFlippedClause>(c, key, value),
            _ => false,
        };

    /// <summary>
    /// Apply the discriminator wire value via the family's <c>SetDiscriminatorValue</c>.
    /// Returns <c>false</c> when the clause type is not a desc family (e.g. And/Or).
    /// </summary>
    public static bool TrySetDiscriminatorValue(IJamlClause clause, IJamlValueReader value) =>
        clause switch
        {
            JokerClause c => SetDisc<JokerFilterDesc, JokerClause>(c, value),
            CommonJokerClause c => SetDisc<CommonJokerFilterDesc, CommonJokerClause>(c, value),
            UncommonJokerClause c => SetDisc<UncommonJokerFilterDesc, UncommonJokerClause>(c, value),
            RareJokerClause c => SetDisc<RareJokerFilterDesc, RareJokerClause>(c, value),
            LegendaryJokerClause c => SetDisc<LegendaryJokerFilterDesc, LegendaryJokerClause>(c, value),
            VoucherClause c => SetDisc<VoucherFilterDesc, VoucherClause>(c, value),
            TarotCardClause c => SetDisc<TarotCardFilterDesc, TarotCardClause>(c, value),
            SpectralCardClause c => SetDisc<SpectralCardFilterDesc, SpectralCardClause>(c, value),
            PlanetCardClause c => SetDisc<PlanetCardFilterDesc, PlanetCardClause>(c, value),
            StandardCardClause c => SetDisc<StandardCardFilterDesc, StandardCardClause>(c, value),
            BossClause c => SetDisc<BossFilterDesc, BossClause>(c, value),
            TagClause c => SetDisc<TagFilterDesc, TagClause>(c, value),
            BoosterPackClause c => SetDisc<BoosterPackFilterDesc, BoosterPackClause>(c, value),
            ErraticRankClause c => SetDisc<ErraticRankFilterDesc, ErraticRankClause>(c, value),
            ErraticSuitClause c => SetDisc<ErraticSuitFilterDesc, ErraticSuitClause>(c, value),
            StartingDrawClause c => SetDisc<StartingDrawFilterDesc, StartingDrawClause>(c, value),
            PokerHandClause c => SetDisc<PokerHandFilterDesc, PokerHandClause>(c, value),
            LuckyMoneyClause c => SetDisc<LuckyMoneyFilterDesc, LuckyMoneyClause>(c, value),
            LuckyMultClause c => SetDisc<LuckyMultFilterDesc, LuckyMultClause>(c, value),
            MisprintMultClause c => SetDisc<MisprintMultFilterDesc, MisprintMultClause>(c, value),
            WheelOfFortuneClause c => SetDisc<WheelOfFortuneFilterDesc, WheelOfFortuneClause>(c, value),
            GrosMichelExtinctClause c => SetDisc<GrosMichelExtinctFilterDesc, GrosMichelExtinctClause>(c, value),
            CavendishExtinctClause c => SetDisc<CavendishExtinctFilterDesc, CavendishExtinctClause>(c, value),
            SpaceLevelupClause c => SetDisc<SpaceLevelupFilterDesc, SpaceLevelupClause>(c, value),
            BusinessPayoutClause c => SetDisc<BusinessPayoutFilterDesc, BusinessPayoutClause>(c, value),
            BloodstoneTriggerClause c => SetDisc<BloodstoneTriggerFilterDesc, BloodstoneTriggerClause>(c, value),
            ParkingPayoutClause c => SetDisc<ParkingPayoutFilterDesc, ParkingPayoutClause>(c, value),
            GlassDestroyClause c => SetDisc<GlassDestroyFilterDesc, GlassDestroyClause>(c, value),
            WheelStaysFlippedClause c => SetDisc<WheelStaysFlippedFilterDesc, WheelStaysFlippedClause>(c, value),
            _ => false,
        };

    /// <summary>
    /// The share of seeds this clause lets through, asked of the family that owns the odds.
    /// <para>
    /// Every family gets an arm, including the ones with no model yet: those inherit
    /// <see cref="IJamlClauseDesc{TClause}.EstimateRarity"/>'s NaN default, so coverage grows by a
    /// family declaring its own odds rather than by anyone remembering to add it here. A clause
    /// type that is not a desc family at all — And/Or/Not — is also NaN; composing those is the
    /// caller's job, not a lookup.
    /// </para>
    /// <para>
    /// Returns double rather than the <c>bool</c>-and-<c>out</c> the neighbouring <c>Try*</c>
    /// methods use, because NaN is already the declared "not modelled" sentinel and a second way
    /// to say it would only invite the two to disagree.
    /// </para>
    /// </summary>
    public static double EstimateRarity(IJamlClause clause, in JamlRarityContext ctx) =>
        clause switch
        {
            JokerClause c => Estimate<JokerFilterDesc, JokerClause>(c, in ctx),
            CommonJokerClause c => Estimate<CommonJokerFilterDesc, CommonJokerClause>(c, in ctx),
            UncommonJokerClause c => Estimate<UncommonJokerFilterDesc, UncommonJokerClause>(c, in ctx),
            RareJokerClause c => Estimate<RareJokerFilterDesc, RareJokerClause>(c, in ctx),
            LegendaryJokerClause c => Estimate<LegendaryJokerFilterDesc, LegendaryJokerClause>(c, in ctx),
            VoucherClause c => Estimate<VoucherFilterDesc, VoucherClause>(c, in ctx),
            TarotCardClause c => Estimate<TarotCardFilterDesc, TarotCardClause>(c, in ctx),
            SpectralCardClause c => Estimate<SpectralCardFilterDesc, SpectralCardClause>(c, in ctx),
            PlanetCardClause c => Estimate<PlanetCardFilterDesc, PlanetCardClause>(c, in ctx),
            StandardCardClause c => Estimate<StandardCardFilterDesc, StandardCardClause>(c, in ctx),
            BossClause c => Estimate<BossFilterDesc, BossClause>(c, in ctx),
            TagClause c => Estimate<TagFilterDesc, TagClause>(c, in ctx),
            BoosterPackClause c => Estimate<BoosterPackFilterDesc, BoosterPackClause>(c, in ctx),
            ErraticRankClause c => Estimate<ErraticRankFilterDesc, ErraticRankClause>(c, in ctx),
            ErraticSuitClause c => Estimate<ErraticSuitFilterDesc, ErraticSuitClause>(c, in ctx),
            StartingDrawClause c => Estimate<StartingDrawFilterDesc, StartingDrawClause>(c, in ctx),
            PokerHandClause c => Estimate<PokerHandFilterDesc, PokerHandClause>(c, in ctx),
            LuckyMoneyClause c => Estimate<LuckyMoneyFilterDesc, LuckyMoneyClause>(c, in ctx),
            LuckyMultClause c => Estimate<LuckyMultFilterDesc, LuckyMultClause>(c, in ctx),
            MisprintMultClause c => Estimate<MisprintMultFilterDesc, MisprintMultClause>(c, in ctx),
            WheelOfFortuneClause c => Estimate<WheelOfFortuneFilterDesc, WheelOfFortuneClause>(c, in ctx),
            GrosMichelExtinctClause c => Estimate<GrosMichelExtinctFilterDesc, GrosMichelExtinctClause>(c, in ctx),
            CavendishExtinctClause c => Estimate<CavendishExtinctFilterDesc, CavendishExtinctClause>(c, in ctx),
            SpaceLevelupClause c => Estimate<SpaceLevelupFilterDesc, SpaceLevelupClause>(c, in ctx),
            BusinessPayoutClause c => Estimate<BusinessPayoutFilterDesc, BusinessPayoutClause>(c, in ctx),
            BloodstoneTriggerClause c => Estimate<BloodstoneTriggerFilterDesc, BloodstoneTriggerClause>(c, in ctx),
            ParkingPayoutClause c => Estimate<ParkingPayoutFilterDesc, ParkingPayoutClause>(c, in ctx),
            GlassDestroyClause c => Estimate<GlassDestroyFilterDesc, GlassDestroyClause>(c, in ctx),
            WheelStaysFlippedClause c => Estimate<WheelStaysFlippedFilterDesc, WheelStaysFlippedClause>(c, in ctx),
            _ => double.NaN,
        };
}
