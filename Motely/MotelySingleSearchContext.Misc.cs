namespace Motely;

public partial class MotelySingleSearchContext
{
    #region Misprint

    public MotelySinglePrngStream CreateMisprintPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerMisprint, isCached);

    public int GetNextMisprintMult(ref MotelySinglePrngStream misprintStream) =>
        GetNextRandomInt(
            ref misprintStream,
            MotelyGlobals.JokerMisprintMin,
            MotelyGlobals.JokerMisprintMax + 1
        );
    #endregion

    #region Lucky Cards

    public MotelySinglePrngStream CreateLuckyCardMoneyStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.CardLuckyMoney, isCached);

    public bool GetNextLuckyMoney(ref MotelySinglePrngStream moneyStream, double baseLuck = 1) =>
        GetNextRandom(ref moneyStream) < baseLuck / MotelyGlobals.EnhancementLuckyMoneyChance;

    public MotelySinglePrngStream CreateLuckyCardMultStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.CardLuckyMult, isCached);

    public bool GetNextLuckyMult(ref MotelySinglePrngStream multStream, double baseLuck = 1) =>
        GetNextRandom(ref multStream) < baseLuck / MotelyGlobals.EnhancementLuckyMultChance;

    #endregion

    #region Wheel of Fortune
    public MotelySinglePrngStream CreateWheelOfFortuneStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.TarotWheelOfFortune, isCached);

    public MotelyItemEdition GetNextWheelOfFortune(
        ref MotelySinglePrngStream wheelStream,
        double baseLuck = 1
    )
    {
        if (GetNextRandom(ref wheelStream) >= baseLuck / MotelyGlobals.TarrotWheelChance)
            return MotelyItemEdition.None;

        GetNextPrngState(ref wheelStream);

        double editionPoll = GetNextRandom(ref wheelStream);

        if (editionPoll > 1 - 0.006 * 25)
            return MotelyItemEdition.Polychrome;

        if (editionPoll > 1 - 0.02 * 25)
            return MotelyItemEdition.Holographic;

        return MotelyItemEdition.Foil;
    }

    #endregion

    #region Banannanas
    public MotelySinglePrngStream CreateCavendishPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerCavendish, isCached);

    public bool GetNextCavendishExtinct(
        ref MotelySinglePrngStream cavendishStream,
        double baseLuck = 1
    ) => GetNextRandom(ref cavendishStream) < baseLuck / MotelyGlobals.JokerCavendishChance;

    public MotelySinglePrngStream CreateGrosMichelPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerGrosMichel, isCached);

    public bool GetNextGrosMichelExtinct(
        ref MotelySinglePrngStream grosMichelStream,
        double baseLuck = 1
    ) => GetNextRandom(ref grosMichelStream) < baseLuck / MotelyGlobals.JokerGrosMichelChance;

    #endregion

    #region Space Joker

    public MotelySinglePrngStream CreateSpacePrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerSpace, isCached);

    public bool GetNextSpaceLevelup(ref MotelySinglePrngStream spaceStream, double baseLuck = 1) =>
        GetNextRandom(ref spaceStream) < baseLuck / MotelyGlobals.JokerSpaceChance;

    #endregion

    #region Business Card

    public MotelySinglePrngStream CreateBusinessPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerBusiness, isCached);

    public bool GetNextBusinessPayout(
        ref MotelySinglePrngStream businessStream,
        double baseLuck = 1
    ) => GetNextRandom(ref businessStream) < baseLuck / MotelyGlobals.JokerBusinessChance;

    #endregion

    #region Bloodstone

    public MotelySinglePrngStream CreateBloodstonePrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerBloodstone, isCached);

    public bool GetNextBloodstoneTrigger(
        ref MotelySinglePrngStream bloodstoneStream,
        double baseLuck = 1
    ) => GetNextRandom(ref bloodstoneStream) < baseLuck / MotelyGlobals.JokerBloodstoneChance;

    #endregion

    #region Reserved Parking

    public MotelySinglePrngStream CreateParkingPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerParking, isCached);

    public bool GetNextParkingPayout(
        ref MotelySinglePrngStream parkingStream,
        double baseLuck = 1
    ) => GetNextRandom(ref parkingStream) < baseLuck / MotelyGlobals.JokerParkingChance;

    #endregion

    #region 8-Ball

    public MotelySinglePrngStream CreateEightBallPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.JokerEightBall, isCached);

    public bool GetNextEightBallTarot(
        ref MotelySinglePrngStream eightBallStream,
        double baseLuck = 1
    ) => GetNextRandom(ref eightBallStream) < baseLuck / MotelyGlobals.JokerEightBallChance;

    #endregion

    #region Glass Card

    public MotelySinglePrngStream CreateGlassPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.CardGlass, isCached);

    public bool GetNextGlassDestroy(ref MotelySinglePrngStream glassStream, double baseLuck = 1) =>
        GetNextRandom(ref glassStream) < baseLuck / MotelyGlobals.CardGlassChance;

    #endregion

    #region Omen Globe

    public MotelySinglePrngStream CreateOmenGlobePrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.VoucherOmenGlobe, isCached);

    public bool GetNextOmenGlobeSpectral(
        ref MotelySinglePrngStream omenGlobeStream,
        double baseLuck = 1
    ) => GetNextRandom(ref omenGlobeStream) < baseLuck / MotelyGlobals.VoucherOmenGlobeChance;

    #endregion

    #region The Wheel boss

    public MotelySinglePrngStream CreateTheWheelPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.BossTheWheel, isCached);

    public bool GetNextWheelStaysFlipped(
        ref MotelySinglePrngStream theWheelStream,
        double baseLuck = 1
    ) => GetNextRandom(ref theWheelStream) < baseLuck / MotelyGlobals.BossTheWheelChance;

    #endregion

    #region Erratic

    public MotelySinglePrngStream CreateErraticDeckPrngStream(bool isCached = false) =>
        CreatePrngStream(MotelyPrngKeys.DeckErratic, isCached);

    public MotelyItem GetNextErraticDeckCard(ref MotelySinglePrngStream erraticDeckStream) =>
        new(GetNextRandomElement(ref erraticDeckStream, MotelyEnum<MotelyStandardCard>.Values));

    #endregion
}
