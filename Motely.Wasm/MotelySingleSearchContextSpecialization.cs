using Bootsharp;
using Motely;

// The declared JS surface of the live single-seed search context (specialization.md): every
// member a JavaScript host can drive. Stream walkers that thread C# byref state
// stay engine-side.
//
// RunState members aren't on this surface yet; nothing blocks adding them. MotelyRunState is a
// sealed record and MotelySingleSearchContext is a class, so both cross as ordinary types.
// Bootsharp serializes records by value, so a member that advances run state returns the new
// state alongside its result — mutating a crossed copy in place changes nothing on the far side.

[SpecializeImport(typeof(MotelySingleSearchContext))]
public abstract class MotelySingleSearchContextImport(int id) : SpecializedImport(id)
{
    public abstract MotelyDeck Deck { get; }
    public abstract MotelyStake Stake { get; }
    public abstract string GetSeed();
    public abstract double PseudoHash(string key, bool isCached = false);

    public abstract MotelySinglePrngStream CreatePrngStream(string key, bool isCached = false);
    public abstract MotelySinglePrngStream ResumeStream(double state);

    public abstract MotelySingleBossStream CreateBossStream();
    public abstract MotelyBossBlind GetBossForAnte(int ante);
    public abstract MotelyVoucher GetAnteFirstVoucher(int ante, bool isCached = false);
    public abstract MotelySingleVoucherStream CreateVoucherStream(int ante, bool isCached = false);
    public abstract MotelySingleTagStream CreateTagStream(int ante, bool isCached = false);

    public abstract MotelySingleShopItemStream CreateShopItemStream(
        int ante,
        MotelyShopStreamFlags flags,
        MotelyJokerStreamFlags jokerFlags,
        bool isCached = false
    );
    public abstract MotelySingleBoosterPackStream CreateBoosterPackStream(
        int ante,
        bool isCached = false
    );
    public abstract MotelySingleBoosterPackStream CreateBoosterPackStream(
        int ante,
        bool generatedFirstPack,
        bool isCached = false
    );

    public abstract MotelySingleJokerStream CreateShopJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerStream CreateBuffoonPackJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerStream CreateJudgementJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerStream CreateWraithJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerFixedRarityStream CreateLegendaryJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerFixedRarityStream CreateRareTagJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerFixedRarityStream CreateUncommonTagJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerFixedRarityStream CreateRiffRaffJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerFixedRarityStream CreateCommonShopJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerFixedRarityStream CreateUncommonShopJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    );
    public abstract MotelySingleJokerFixedRarityStream CreateRareShopJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    );

    public abstract MotelySingleTarotStream CreateArcanaPackTarotStream(
        int ante,
        bool soulOnly,
        bool isCached = false
    );
    public abstract MotelySingleTarotStream CreateShopTarotStream(int ante, bool isCached = false);
    public abstract MotelySingleTarotStream CreateEmperorTarotStream(
        int ante,
        bool isCached = false
    );
    public abstract MotelySingleTarotStream CreatePurpleSealTarotStream(
        int ante,
        bool isCached = false
    );

    public abstract MotelySingleSpectralStream CreateSpectralPackSpectralStream(
        int ante,
        bool soulOnly,
        bool isCached = false
    );
    public abstract MotelySingleSpectralStream CreateShopSpectralStream(
        int ante,
        bool isCached = false
    );
    public abstract MotelySingleSpectralStream CreateSixthSenseSpectralStream(
        int ante,
        bool isCached = false
    );
    public abstract MotelySingleSpectralStream CreateSeanceSpectralStream(
        int ante,
        bool isCached = false
    );

    public abstract MotelySinglePlanetStream CreateCelestialPackPlanetStream(
        int ante,
        bool isCached = false
    );
    public abstract MotelySinglePlanetStream CreateShopPlanetStream(int ante, bool isCached = false);

    public abstract MotelySingleStandardCardStream CreateStandardPackCardStream(
        int ante,
        MotelyStandardCardStreamFlags flags,
        bool isCached = false
    );

    public abstract MotelySinglePrngStream CreateMisprintPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateLuckyCardMoneyStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateLuckyCardMultStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateWheelOfFortuneStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateCavendishPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateGrosMichelPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateSpacePrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateBusinessPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateBloodstonePrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateParkingPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateEightBallPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateGlassPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateOmenGlobePrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateTheWheelPrngStream(bool isCached = false);
    public abstract MotelySinglePrngStream CreateErraticDeckPrngStream(bool isCached = false);
}

[SpecializeExport(typeof(MotelySingleSearchContext))]
public sealed class MotelySingleSearchContextExport(MotelySingleSearchContext ctx)
    : SpecializedExport(ctx)
{
    public MotelyDeck Deck => ctx.Deck;
    public MotelyStake Stake => ctx.Stake;

    public string GetSeed() => ctx.GetSeed();

    public double PseudoHash(string key, bool isCached = false) => ctx.PseudoHash(key, isCached);

    public MotelySinglePrngStream CreatePrngStream(string key, bool isCached = false) =>
        ctx.CreatePrngStream(key, isCached);

    public MotelySinglePrngStream ResumeStream(double state) => ctx.ResumeStream(state);

    public MotelySingleBossStream CreateBossStream() => ctx.CreateBossStream();

    public MotelyBossBlind GetBossForAnte(int ante)
    {
        var stream = ctx.CreateBossStream();
        var state = new MotelyRunState();
        return ctx.GetBossForAnte(ref stream, ante, state);
    }

    public MotelyVoucher GetAnteFirstVoucher(int ante, bool isCached = false) =>
        ctx.GetAnteFirstVoucher(ante, isCached);

    public MotelySingleVoucherStream CreateVoucherStream(int ante, bool isCached = false) =>
        ctx.CreateVoucherStream(ante, isCached);

    public MotelySingleTagStream CreateTagStream(int ante, bool isCached = false) =>
        ctx.CreateTagStream(ante, isCached);

    public MotelySingleShopItemStream CreateShopItemStream(
        int ante,
        MotelyShopStreamFlags flags,
        MotelyJokerStreamFlags jokerFlags,
        bool isCached = false
    ) => ctx.CreateShopItemStream(ante, flags, jokerFlags, isCached);

    public MotelySingleBoosterPackStream CreateBoosterPackStream(int ante, bool isCached = false) =>
        ctx.CreateBoosterPackStream(ante, isCached);

    public MotelySingleBoosterPackStream CreateBoosterPackStream(
        int ante,
        bool generatedFirstPack,
        bool isCached = false
    ) => ctx.CreateBoosterPackStream(ante, generatedFirstPack, isCached);

    public MotelySingleJokerStream CreateShopJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateShopJokerStream(ante, flags, isCached);

    public MotelySingleJokerStream CreateBuffoonPackJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateBuffoonPackJokerStream(ante, flags, isCached);

    public MotelySingleJokerStream CreateJudgementJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateJudgementJokerStream(ante, flags, isCached);

    public MotelySingleJokerStream CreateWraithJokerStream(
        int ante,
        MotelyJokerStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateWraithJokerStream(ante, flags, isCached);

    public MotelySingleJokerFixedRarityStream CreateLegendaryJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateLegendaryJokerStream(ante, flags, isCached);

    public MotelySingleJokerFixedRarityStream CreateRareTagJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateRareTagJokerStream(ante, flags, isCached);

    public MotelySingleJokerFixedRarityStream CreateUncommonTagJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateUncommonTagJokerStream(ante, flags, isCached);

    public MotelySingleJokerFixedRarityStream CreateRiffRaffJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateRiffRaffJokerStream(ante, flags, isCached);

    public MotelySingleJokerFixedRarityStream CreateCommonShopJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateCommonShopJokerStream(ante, flags, isCached);

    public MotelySingleJokerFixedRarityStream CreateUncommonShopJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateUncommonShopJokerStream(ante, flags, isCached);

    public MotelySingleJokerFixedRarityStream CreateRareShopJokerStream(
        int ante,
        MotelyJokerFixedRarityStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateRareShopJokerStream(ante, flags, isCached);

    public MotelySingleTarotStream CreateArcanaPackTarotStream(
        int ante,
        bool soulOnly,
        bool isCached = false
    ) => ctx.CreateArcanaPackTarotStream(ante, soulOnly, isCached);

    public MotelySingleTarotStream CreateShopTarotStream(int ante, bool isCached = false) =>
        ctx.CreateShopTarotStream(ante, isCached);

    public MotelySingleTarotStream CreateEmperorTarotStream(int ante, bool isCached = false) =>
        ctx.CreateEmperorTarotStream(ante, isCached);

    public MotelySingleTarotStream CreatePurpleSealTarotStream(int ante, bool isCached = false) =>
        ctx.CreatePurpleSealTarotStream(ante, isCached);

    public MotelySingleSpectralStream CreateSpectralPackSpectralStream(
        int ante,
        bool soulOnly,
        bool isCached = false
    ) => ctx.CreateSpectralPackSpectralStream(ante, soulOnly, isCached);

    public MotelySingleSpectralStream CreateShopSpectralStream(int ante, bool isCached = false) =>
        ctx.CreateShopSpectralStream(ante, isCached);

    public MotelySingleSpectralStream CreateSixthSenseSpectralStream(
        int ante,
        bool isCached = false
    ) => ctx.CreateSixthSenseSpectralStream(ante, isCached);

    public MotelySingleSpectralStream CreateSeanceSpectralStream(int ante, bool isCached = false) =>
        ctx.CreateSeanceSpectralStream(ante, isCached);

    public MotelySinglePlanetStream CreateCelestialPackPlanetStream(
        int ante,
        bool isCached = false
    ) => ctx.CreateCelestialPackPlanetStream(ante, isCached);

    public MotelySinglePlanetStream CreateShopPlanetStream(int ante, bool isCached = false) =>
        ctx.CreateShopPlanetStream(ante, isCached);

    public MotelySingleStandardCardStream CreateStandardPackCardStream(
        int ante,
        MotelyStandardCardStreamFlags flags,
        bool isCached = false
    ) => ctx.CreateStandardPackCardStream(ante, flags, isCached);

    public MotelySinglePrngStream CreateMisprintPrngStream(bool isCached = false) =>
        ctx.CreateMisprintPrngStream(isCached);

    public MotelySinglePrngStream CreateLuckyCardMoneyStream(bool isCached = false) =>
        ctx.CreateLuckyCardMoneyStream(isCached);

    public MotelySinglePrngStream CreateLuckyCardMultStream(bool isCached = false) =>
        ctx.CreateLuckyCardMultStream(isCached);

    public MotelySinglePrngStream CreateWheelOfFortuneStream(bool isCached = false) =>
        ctx.CreateWheelOfFortuneStream(isCached);

    public MotelySinglePrngStream CreateCavendishPrngStream(bool isCached = false) =>
        ctx.CreateCavendishPrngStream(isCached);

    public MotelySinglePrngStream CreateGrosMichelPrngStream(bool isCached = false) =>
        ctx.CreateGrosMichelPrngStream(isCached);

    public MotelySinglePrngStream CreateSpacePrngStream(bool isCached = false) =>
        ctx.CreateSpacePrngStream(isCached);

    public MotelySinglePrngStream CreateBusinessPrngStream(bool isCached = false) =>
        ctx.CreateBusinessPrngStream(isCached);

    public MotelySinglePrngStream CreateBloodstonePrngStream(bool isCached = false) =>
        ctx.CreateBloodstonePrngStream(isCached);

    public MotelySinglePrngStream CreateParkingPrngStream(bool isCached = false) =>
        ctx.CreateParkingPrngStream(isCached);

    public MotelySinglePrngStream CreateEightBallPrngStream(bool isCached = false) =>
        ctx.CreateEightBallPrngStream(isCached);

    public MotelySinglePrngStream CreateGlassPrngStream(bool isCached = false) =>
        ctx.CreateGlassPrngStream(isCached);

    public MotelySinglePrngStream CreateOmenGlobePrngStream(bool isCached = false) =>
        ctx.CreateOmenGlobePrngStream(isCached);

    public MotelySinglePrngStream CreateTheWheelPrngStream(bool isCached = false) =>
        ctx.CreateTheWheelPrngStream(isCached);

    public MotelySinglePrngStream CreateErraticDeckPrngStream(bool isCached = false) =>
        ctx.CreateErraticDeckPrngStream(isCached);
}
