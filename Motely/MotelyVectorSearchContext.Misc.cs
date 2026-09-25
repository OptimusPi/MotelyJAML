using System.Runtime.Intrinsics;

namespace Motely;

ref partial struct MotelyVectorSearchContext
{
    #region Misprint

    public MotelyVectorPrngStream CreateMisprintPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerMisprint, isCached);

    public Vector256<int> GetNextMisprintMult(ref MotelyVectorPrngStream misprintStream) =>
        GetNextRandomInt(
            ref misprintStream,
            MotelyGlobals.JokerMisprintMin,
            MotelyGlobals.JokerMisprintMax + 1
        );
    #endregion

    #region Lucky Cards

    public MotelyVectorPrngStream CreateLuckyCardMoneyStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.CardLuckyMoney, isCached);

    public VectorMask GetNextLuckyMoney(
        ref MotelyVectorPrngStream moneyStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref moneyStream),
            Vector512.Create(baseLuck / MotelyGlobals.EnhancementLuckyMoneyChance)
        );

    public MotelyVectorPrngStream CreateLuckyCardMultStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.CardLuckyMult, isCached);

    public VectorMask GetNextLuckyMult(
        ref MotelyVectorPrngStream multStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref multStream),
            Vector512.Create(baseLuck / MotelyGlobals.EnhancementLuckyMultChance)
        );

    #endregion

    #region Wheel of Fortune
    public MotelyVectorPrngStream CreateWheelOfFortuneStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.TarotWheelOfFortune, isCached);

    public VectorEnum256<MotelyItemEdition> GetNextWheelOfFortune(
        ref MotelyVectorPrngStream wheelStream,
        double baseLuck = 1
    )
    {
        Vector512<double> successMask = Vector512.LessThan(
            GetNextRandom(ref wheelStream),
            Vector512.Create(baseLuck / MotelyGlobals.TarrotWheelChance)
        );

        GetNextPrngState(ref wheelStream, successMask);

        Vector512<double> editionPoll = GetNextRandom(ref wheelStream, successMask);

        return new(
            Vector256.ConditionalSelect(
                MotelyVectorUtils.ShrinkDoubleMaskToInt(successMask),
                Vector256.ConditionalSelect(
                    MotelyVectorUtils.ShrinkDoubleMaskToInt(
                        Vector512.GreaterThan(editionPoll, Vector512.Create(1 - 0.006 * 25))
                    ),
                    Vector256.Create((int)MotelyItemEdition.Polychrome),
                    Vector256.ConditionalSelect(
                        MotelyVectorUtils.ShrinkDoubleMaskToInt(
                            Vector512.GreaterThan(editionPoll, Vector512.Create(1 - 0.02 * 25))
                        ),
                        Vector256.Create((int)MotelyItemEdition.Holographic),
                        Vector256.Create((int)MotelyItemEdition.Foil)
                    )
                ),
                Vector256.Create((int)MotelyItemEdition.None)
            )
        );
    }

    #endregion

    #region Banannanas
    public MotelyVectorPrngStream CreateCavendishPrngStream(bool isCached) =>
        CreatePrngStream(MotelyPrngKeys.JokerCavendish, isCached);

    public VectorMask GetNextCavendishExtinct(
        ref MotelyVectorPrngStream cavendishStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref cavendishStream),
            Vector512.Create(baseLuck / MotelyGlobals.JokerCavendishChance)
        );

    public MotelyVectorPrngStream CreateGrosMichelPrngStream(bool isCached) =>
        CreatePrngStream(MotelyPrngKeys.JokerGrosMichel, isCached);

    public VectorMask GetNextGrosMichelExtinct(
        ref MotelyVectorPrngStream grosMichelStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref grosMichelStream),
            Vector512.Create(baseLuck / MotelyGlobals.JokerGrosMichelChance)
        );

    #endregion

    #region Space Joker

    public MotelyVectorPrngStream CreateSpacePrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerSpace, isCached);

    public VectorMask GetNextSpaceLevelup(
        ref MotelyVectorPrngStream spaceStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref spaceStream),
            Vector512.Create(baseLuck / MotelyGlobals.JokerSpaceChance)
        );

    #endregion

    #region Business Card

    public MotelyVectorPrngStream CreateBusinessPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerBusiness, isCached);

    public VectorMask GetNextBusinessPayout(
        ref MotelyVectorPrngStream businessStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref businessStream),
            Vector512.Create(baseLuck / MotelyGlobals.JokerBusinessChance)
        );

    #endregion

    #region Bloodstone

    public MotelyVectorPrngStream CreateBloodstonePrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerBloodstone, isCached);

    public VectorMask GetNextBloodstoneTrigger(
        ref MotelyVectorPrngStream bloodstoneStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref bloodstoneStream),
            Vector512.Create(baseLuck / MotelyGlobals.JokerBloodstoneChance)
        );

    #endregion

    #region Reserved Parking

    public MotelyVectorPrngStream CreateParkingPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerParking, isCached);

    public VectorMask GetNextParkingPayout(
        ref MotelyVectorPrngStream parkingStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref parkingStream),
            Vector512.Create(baseLuck / MotelyGlobals.JokerParkingChance)
        );

    #endregion

    #region 8-Ball

    public MotelyVectorPrngStream CreateEightBallPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerEightBall, isCached);

    public VectorMask GetNextEightBallTarot(
        ref MotelyVectorPrngStream eightBallStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref eightBallStream),
            Vector512.Create(baseLuck / MotelyGlobals.JokerEightBallChance)
        );

    #endregion

    #region Glass Card

    public MotelyVectorPrngStream CreateGlassPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.CardGlass, isCached);

    public VectorMask GetNextGlassDestroy(
        ref MotelyVectorPrngStream glassStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref glassStream),
            Vector512.Create(baseLuck / MotelyGlobals.CardGlassChance)
        );

    #endregion

    #region Omen Globe

    public MotelyVectorPrngStream CreateOmenGlobePrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.VoucherOmenGlobe, isCached);

    public VectorMask GetNextOmenGlobeSpectral(
        ref MotelyVectorPrngStream omenGlobeStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref omenGlobeStream),
            Vector512.Create(baseLuck / MotelyGlobals.VoucherOmenGlobeChance)
        );

    #endregion

    #region The Wheel boss

    public MotelyVectorPrngStream CreateTheWheelPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.BossTheWheel, isCached);

    public VectorMask GetNextWheelStaysFlipped(
        ref MotelyVectorPrngStream theWheelStream,
        double baseLuck = 1
    ) =>
        Vector512.LessThan(
            GetNextRandom(ref theWheelStream),
            Vector512.Create(baseLuck / MotelyGlobals.BossTheWheelChance)
        );

    #endregion

    #region Erratic

    public MotelyVectorPrngStream CreateErraticDeckPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.DeckErratic, isCached);

    public MotelyItemVector GetNextErraticDeckCard(ref MotelyVectorPrngStream erraticDeckStream) =>
        new(
            Vector256.BitwiseOr(
                GetNextRandomElement(
                    ref erraticDeckStream,
                    MotelyEnum<MotelyStandardCard>.Values
                ).HardwareVector,
                Vector256.Create((int)MotelyItemTypeCategory.Standardcard)
            )
        );

    #endregion
}
