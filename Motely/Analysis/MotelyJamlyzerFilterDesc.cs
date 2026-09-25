using System.ComponentModel;

namespace Motely.Analysis;

internal readonly struct MotelyJamlyzerWindow(
    int[] antesToAnalyze,
    int eventRolls,
    int shopSlots = 0
)
{
    public readonly int StartAnte =
        antesToAnalyze.Length > 0 && antesToAnalyze[0] == 0 ? 0 : 1;
    public readonly int MaxAnte = antesToAnalyze.Length > 0 ? antesToAnalyze[^1] : 8;
    public readonly int EventRolls = eventRolls;

    public readonly int ShopSlots = shopSlots;
}

public sealed class MotelyJamlyzerFilterDesc(
    int[] antesToAnalyze,
    int eventRolls = 20,
    MotelyJamlyzerStreamStates? resumeFrom = null,
    int shopSlots = 0
) : IMotelySeedFilterDesc<MotelyJamlyzerFilterDesc.JamlyzerFilter>
{
    public MotelyJamlyzerSeedResult? Result { get; private set; }

    public JamlyzerFilter CreateFilter(ref MotelyFilterCreationContext ctx) => new(this);

    public readonly struct JamlyzerFilter(MotelyJamlyzerFilterDesc filterDesc) : IMotelySeedFilter
    {
        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx) =>
            ctx.SearchIndividualSeeds(CheckSeed);

        public readonly int CheckSeed(MotelySingleSearchContext ctx)
        {
            filterDesc.Result = MotelyJamlyzerSeedWalk.Walk(
                ref ctx,
                in filterDesc._window,
                filterDesc._resumeFrom
            );
            return 1;
        }
    }

    private readonly MotelyJamlyzerWindow _window = new(antesToAnalyze, eventRolls, shopSlots);
    private readonly MotelyJamlyzerStreamStates? _resumeFrom = resumeFrom;
}

internal static class MotelyJamlyzerSeedWalk
{
    private ref struct AnteAnalysisState
    {
        public MotelySingleTarotStream ArcanaStream;
        public readonly bool HasArcanaStream => !ArcanaStream.IsNull;
        public MotelySinglePlanetStream CelestialStream;
        public readonly bool HasCelestialStream => !CelestialStream.IsNull;
        public MotelySingleSpectralStream SpectralStream;
        public readonly bool HasSpectralStream => !SpectralStream.IsNull;
        public MotelySingleStandardCardStream StandardStream;
        public readonly bool HasStandardStream => !StandardStream.IsInvalid;
        public MotelySingleJokerStream BuffoonStream;
        public readonly bool HasBuffoonStream => !BuffoonStream.IsNull;
    }

    internal static MotelyJamlyzerSeedResult Walk(
        ref MotelySingleSearchContext ctx,
        in MotelyJamlyzerWindow window,
        MotelyJamlyzerStreamStates? resumeFrom
    )
    {
        int n = window.EventRolls;
        int offset = resumeFrom?.RollOffset ?? 0;
        int shopOffset = resumeFrom?.ShopOffset ?? 0;

        MotelyRunState voucherState = new();
        MotelySingleBossStream bossStream = ctx.CreateBossStream();

        int startAnte = window.StartAnte;
        if (startAnte == 1
            && ctx.GetAnteFirstVoucher(1, new MotelyRunState()) is MotelyVoucher.Hieroglyph or MotelyVoucher.Petroglyph)
            startAnte = 0;

        var antes = new MotelyJamlyzerAnteResult[window.MaxAnte - startAnte + 1];

        for (int ante = startAnte; ante <= window.MaxAnte; ante++)
        {
            bool isPreRunShop = ante == 0;
            MotelyBossBlind boss;
            if (isPreRunShop)
            {
                MotelyRunState preRunState = new();
                MotelySingleBossStream preRunBossStream = ctx.CreateBossStream();
                boss = ctx.GetBossForAnte(ref preRunBossStream, ante, preRunState);
            }
            else
                boss = ctx.GetBossForAnte(ref bossStream, ante, voucherState);

            MotelyVoucher voucher = ctx.GetAnteFirstVoucher(ante, voucherState);

            AnteAnalysisState state = new()
            {
                ArcanaStream = default,
                CelestialStream = default,
                SpectralStream = default,
                StandardStream = MotelySingleStandardCardStream.Invalid,
                BuffoonStream = default,
            };

            MotelySingleTagStream tagStream = ctx.CreateTagStream(ante);
            MotelyTag smallTag = ctx.GetNextTag(ref tagStream);
            MotelyTag bigTag = ctx.GetNextTag(ref tagStream);

            MotelySingleShopItemStream shopStream = ctx.CreateShopItemStream(ante);
            int maxSlots = window.ShopSlots > 0 ? window.ShopSlots : (ante <= 1 ? 15 : 50);
            MotelyItem[] shopItems = new MotelyItem[maxSlots];
            for (int i = 0; i < shopOffset + maxSlots; i++)
            {
                var item = ctx.GetNextShopItem(ref shopStream);
                if (i >= shopOffset)
                    shopItems[i - shopOffset] = item;
            }

            var packStream = ctx.CreateBoosterPackStream(ante);
            int maxPacks = ante <= 1 ? 4 : 6;
            MotelyJamlyzerPack[] packs = new MotelyJamlyzerPack[maxPacks];
            for (int i = 0; i < maxPacks; i++)
            {
                MotelyBoosterPack pack = ctx.GetNextBoosterPack(ref packStream);
                MotelyItem[] contents = GetPackContents(ref ctx, ante, pack, ref state).AsArray();
                packs[i] = new(pack, contents);
            }

            var pulls = CollectPulls(ref ctx, ante, voucherState, n, offset);

            var shopStreams = CollectShopStreams(ref ctx, ante, n, offset);

            if (!isPreRunShop)
                voucherState.ActivateVoucher(voucher);

            antes[ante - startAnte] = new(
                ante,
                boss,
                voucher,
                smallTag,
                bigTag,
                shopItems,
                packs,
                pulls,
                shopStreams
            );
        }

        int shopSlotsWalked = window.ShopSlots > 0 ? window.ShopSlots : 0;
        var (events, streamStates) = CollectEvents(
            ref ctx,
            n,
            shopSlotsWalked,
            resumeFrom
        );

        MotelyItem[]? erraticDeck = null;
        if (ctx.Deck == MotelyDeck.Erratic)
        {
            var deckStream = ctx.CreateErraticDeckPrngStream(isCached: false);
            erraticDeck = new MotelyItem[52];
            for (int i = 0; i < 52; i++)
                erraticDeck[i] = ctx.GetNextErraticDeckCard(ref deckStream);
        }

        return new(ctx.GetSeed(), 0, antes, events, streamStates, erraticDeck);
    }

    private static MotelyJamlyzerPulls CollectPulls(
        ref MotelySingleSearchContext ctx,
        int ante,
        MotelyRunState voucherState,
        int n,
        int offset
    )
    {
        if (n == 0 && offset == 0)
            return new([], [], [], [], [], [], [], [], [], [], []);

        var judgementStream = ctx.CreateJudgementJokerStream(ante);
        var wraithStream = ctx.CreateWraithJokerStream(ante);
        var riffRaffStream = ctx.CreateRiffRaffJokerStream(ante);
        var rareTagStream = ctx.CreateRareTagJokerStream(ante);
        var uncommonTagStream = ctx.CreateUncommonTagJokerStream(ante);
        var legendaryStream = ctx.CreateLegendaryJokerStream(ante);

        var emperorStream = ctx.CreateEmperorTarotStream(ante);
        var purpleSealStream = ctx.CreatePurpleSealTarotStream(ante);

        var sixthSenseStream = ctx.CreateSixthSenseSpectralStream(ante);
        var seanceStream = ctx.CreateSeanceSpectralStream(ante);

        var voucherStream = ctx.CreateVoucherStream(ante);

        MotelyItem[] judgement = new MotelyItem[n];
        MotelyItem[] wraith = new MotelyItem[n];
        MotelyItem[] riffRaff = new MotelyItem[n];
        MotelyItem[] rareTag = new MotelyItem[n];
        MotelyItem[] uncommonTag = new MotelyItem[n];
        MotelyItem[] legendary = new MotelyItem[n];
        MotelyItem[] emperor = new MotelyItem[n * 2];
        MotelyItem[] purpleSeal = new MotelyItem[n];
        MotelyItem[] sixthSense = new MotelyItem[n];
        MotelyItem[] seance = new MotelyItem[n];
        MotelyVoucher[] vouchers = new MotelyVoucher[n];

        for (int i = 0; i < offset + n; i++)
        {
            var j = ctx.GetNextJoker(ref judgementStream);
            var wr = ctx.GetNextJoker(ref wraithStream);
            var rr = ctx.GetNextJoker(ref riffRaffStream);
            var rt = ctx.GetNextJoker(ref rareTagStream);
            var ut = ctx.GetNextJoker(ref uncommonTagStream);
            var lg = ctx.GetNextJoker(ref legendaryStream);

            var e0 = ctx.GetNextTarot(ref emperorStream);
            var e1 = ctx.GetNextTarot(ref emperorStream, new(e0));

            var ps = ctx.GetNextTarot(ref purpleSealStream);
            var ss = ctx.GetNextSpectral(ref sixthSenseStream);
            var se = ctx.GetNextSpectral(ref seanceStream);
            var vc = ctx.GetNextVoucher(ref voucherStream, voucherState);

            if (i < offset)
                continue;

            int w = i - offset;
            judgement[w] = j;
            wraith[w] = wr;
            riffRaff[w] = rr;
            rareTag[w] = rt;
            uncommonTag[w] = ut;
            legendary[w] = lg;
            emperor[w * 2] = e0;
            emperor[w * 2 + 1] = e1;
            purpleSeal[w] = ps;
            sixthSense[w] = ss;
            seance[w] = se;
            vouchers[w] = vc;
        }

        return new(
            judgement,
            wraith,
            emperor,
            purpleSeal,
            sixthSense,
            seance,
            riffRaff,
            rareTag,
            uncommonTag,
            legendary,
            vouchers
        );
    }

    private static MotelyJamlyzerShopStreams CollectShopStreams(
        ref MotelySingleSearchContext ctx,
        int ante,
        int n,
        int offset
    )
    {
        if (n == 0 && offset == 0)
            return new([], [], [], [], [], [], []);

        var shopJokerStream = ctx.CreateShopJokerStream(ante);
        var commonJokerStream = ctx.CreateCommonShopJokerStream(ante);
        var uncommonJokerStream = ctx.CreateUncommonShopJokerStream(ante);
        var rareJokerStream = ctx.CreateRareShopJokerStream(ante);
        var shopTarotStream = ctx.CreateShopTarotStream(ante);
        var shopPlanetStream = ctx.CreateShopPlanetStream(ante);
        var shopSpectralStream = ctx.CreateShopSpectralStream(ante);

        MotelyItem[] shopJokers = new MotelyItem[n];
        MotelyItem[] commonJokers = new MotelyItem[n];
        MotelyItem[] uncommonJokers = new MotelyItem[n];
        MotelyItem[] rareJokers = new MotelyItem[n];
        MotelyItem[] shopTarots = new MotelyItem[n];
        MotelyItem[] shopPlanets = new MotelyItem[n];
        MotelyItem[] shopSpectrals = new MotelyItem[n];

        for (int i = 0; i < offset + n; i++)
        {
            var sj = ctx.GetNextJoker(ref shopJokerStream);
            var cj = ctx.GetNextJoker(ref commonJokerStream);
            var uj = ctx.GetNextJoker(ref uncommonJokerStream);
            var rj = ctx.GetNextJoker(ref rareJokerStream);
            var st = ctx.GetNextTarot(ref shopTarotStream);
            var sp = ctx.GetNextPlanet(ref shopPlanetStream);
            var ss = ctx.GetNextSpectral(ref shopSpectralStream);

            if (i < offset)
                continue;

            int w = i - offset;
            shopJokers[w] = sj;
            commonJokers[w] = cj;
            uncommonJokers[w] = uj;
            rareJokers[w] = rj;
            shopTarots[w] = st;
            shopPlanets[w] = sp;
            shopSpectrals[w] = ss;
        }

        return new(
            shopJokers,
            commonJokers,
            uncommonJokers,
            rareJokers,
            shopTarots,
            shopPlanets,
            shopSpectrals
        );
    }

    private static (MotelyJamlyzerEvents, MotelyJamlyzerStreamStates) CollectEvents(
        ref MotelySingleSearchContext ctx,
        int N,
        int shopSlotsWalked,
        MotelyJamlyzerStreamStates? resume
    )
    {
        var luckyMoney = resume is null
            ? ctx.CreateLuckyCardMoneyStream()
            : ctx.ResumeStream(resume.LuckyMoney);
        var luckyMult = resume is null
            ? ctx.CreateLuckyCardMultStream()
            : ctx.ResumeStream(resume.LuckyMult);
        var wheel = resume is null
            ? ctx.CreateWheelOfFortuneStream()
            : ctx.ResumeStream(resume.WheelOfFortune);
        var cavendish = resume is null
            ? ctx.CreateCavendishPrngStream()
            : ctx.ResumeStream(resume.Cavendish);
        var grosMichel = resume is null
            ? ctx.CreateGrosMichelPrngStream()
            : ctx.ResumeStream(resume.GrosMichel);
        var space = resume is null
            ? ctx.CreateSpacePrngStream()
            : ctx.ResumeStream(resume.Space);
        var business = resume is null
            ? ctx.CreateBusinessPrngStream()
            : ctx.ResumeStream(resume.Business);
        var bloodstone = resume is null
            ? ctx.CreateBloodstonePrngStream()
            : ctx.ResumeStream(resume.Bloodstone);
        var parking = resume is null
            ? ctx.CreateParkingPrngStream()
            : ctx.ResumeStream(resume.Parking);
        var eightBall = resume is null
            ? ctx.CreateEightBallPrngStream()
            : ctx.ResumeStream(resume.EightBall);
        var glass = resume is null
            ? ctx.CreateGlassPrngStream()
            : ctx.ResumeStream(resume.Glass);
        var omenGlobe = resume is null
            ? ctx.CreateOmenGlobePrngStream()
            : ctx.ResumeStream(resume.OmenGlobe);
        var theWheel = resume is null
            ? ctx.CreateTheWheelPrngStream()
            : ctx.ResumeStream(resume.TheWheel);
        var misprint = resume is null
            ? ctx.CreateMisprintPrngStream()
            : ctx.ResumeStream(resume.Misprint);

        bool[] luckyMoneyRolls = new bool[N];
        bool[] luckyMultRolls = new bool[N];
        MotelyItemEdition[] wheelRolls = new MotelyItemEdition[N];
        bool[] cavendishRolls = new bool[N];
        bool[] grosMichelRolls = new bool[N];
        bool[] spaceRolls = new bool[N];
        bool[] businessRolls = new bool[N];
        bool[] bloodstoneRolls = new bool[N];
        bool[] parkingRolls = new bool[N];
        bool[] eightBallRolls = new bool[N];
        bool[] glassRolls = new bool[N];
        bool[] omenGlobeRolls = new bool[N];
        bool[] theWheelRolls = new bool[N];
        int[] misprintRolls = new int[N];

        for (int i = 0; i < N; i++)
        {
            luckyMoneyRolls[i] = ctx.GetNextLuckyMoney(ref luckyMoney);
            luckyMultRolls[i] = ctx.GetNextLuckyMult(ref luckyMult);
            wheelRolls[i] = ctx.GetNextWheelOfFortune(ref wheel);
            cavendishRolls[i] = ctx.GetNextCavendishExtinct(ref cavendish);
            grosMichelRolls[i] = ctx.GetNextGrosMichelExtinct(ref grosMichel);
            spaceRolls[i] = ctx.GetNextSpaceLevelup(ref space);
            businessRolls[i] = ctx.GetNextBusinessPayout(ref business);
            bloodstoneRolls[i] = ctx.GetNextBloodstoneTrigger(ref bloodstone);
            parkingRolls[i] = ctx.GetNextParkingPayout(ref parking);
            eightBallRolls[i] = ctx.GetNextEightBallTarot(ref eightBall);
            glassRolls[i] = ctx.GetNextGlassDestroy(ref glass);
            omenGlobeRolls[i] = ctx.GetNextOmenGlobeSpectral(ref omenGlobe);
            theWheelRolls[i] = ctx.GetNextWheelStaysFlipped(ref theWheel);
            misprintRolls[i] = ctx.GetNextMisprintMult(ref misprint);
        }

        var events = new MotelyJamlyzerEvents(
            luckyMoneyRolls,
            luckyMultRolls,
            wheelRolls,
            cavendishRolls,
            grosMichelRolls,
            spaceRolls,
            businessRolls,
            bloodstoneRolls,
            parkingRolls,
            eightBallRolls,
            glassRolls,
            omenGlobeRolls,
            theWheelRolls,
            misprintRolls
        );

        var states = new MotelyJamlyzerStreamStates(
            (resume?.RollOffset ?? 0) + N,
            (resume?.ShopOffset ?? 0) + shopSlotsWalked,
            luckyMoney.State,
            luckyMult.State,
            wheel.State,
            cavendish.State,
            grosMichel.State,
            space.State,
            business.State,
            bloodstone.State,
            parking.State,
            eightBall.State,
            glass.State,
            omenGlobe.State,
            theWheel.State,
            misprint.State
        );

        return (events, states);
    }

    private static MotelySingleItemSet GetPackContents(
        ref MotelySingleSearchContext ctx,
        int ante,
        MotelyBoosterPack pack,
        ref AnteAnalysisState state
    )
    {
        var packType = pack.GetPackType();
        var packSize = pack.GetPackSize();

        switch (packType)
        {
            case MotelyBoosterPackType.Arcana:
                if (!state.HasArcanaStream)
                    state.ArcanaStream = ctx.CreateArcanaPackTarotStream(ante);
                return ctx.GetNextArcanaPackContents(ref state.ArcanaStream, packSize);

            case MotelyBoosterPackType.Celestial:
                if (!state.HasCelestialStream)
                    state.CelestialStream = ctx.CreateCelestialPackPlanetStream(ante);
                return ctx.GetNextCelestialPackContents(ref state.CelestialStream, packSize);

            case MotelyBoosterPackType.Spectral:
                if (!state.HasSpectralStream)
                    state.SpectralStream = ctx.CreateSpectralPackSpectralStream(ante);
                return ctx.GetNextSpectralPackContents(ref state.SpectralStream, packSize);

            case MotelyBoosterPackType.Buffoon:
                if (!state.HasBuffoonStream)
                    state.BuffoonStream = ctx.CreateBuffoonPackJokerStream(ante);
                return ctx.GetNextBuffoonPackContents(ref state.BuffoonStream, packSize);

            case MotelyBoosterPackType.Standard:
                if (!state.HasStandardStream)
                    state.StandardStream = ctx.CreateStandardPackCardStream(ante);
                return ctx.GetNextStandardPackContents(ref state.StandardStream, packSize);

            default:
                throw new InvalidEnumArgumentException();
        }
    }
}
