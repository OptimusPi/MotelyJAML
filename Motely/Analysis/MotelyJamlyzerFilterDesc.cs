using System.ComponentModel;

namespace Motely.Analysis;

/// <summary>
/// The window the Jamlyzer reads for one seed: which antes (the JAML's ante scope, see
/// <see cref="MotelyJamlyzer.ComputeAntes"/>) and how many rolls per stream. One value, shared by
/// the standalone filter and the analyze provider, so both walk exactly the same ground.
/// </summary>
internal readonly struct MotelyJamlyzerWindow(
    int[] antesToAnalyze,
    int eventRolls,
    int shopSlots = 0
)
{
    // Ante 0 (the pre-run shop a JAML clause can target with `antes: [0]`) is real data
    // the search already matches on — emit it when scoped instead of silently dropping it.
    public readonly int StartAnte =
        antesToAnalyze.Length > 0 && antesToAnalyze[0] == 0 ? 0 : 1;
    public readonly int MaxAnte = antesToAnalyze.Length > 0 ? antesToAnalyze[^1] : 8;
    public readonly int EventRolls = eventRolls;

    // How deep to walk each ante's shop queue, independent of EventRolls. 0 keeps the historical
    // defaults (15 for the pre-run shop and ante 1, 50 beyond). The shop stream never runs dry --
    // GetNextShopItem yields forever -- so this bound is the only thing that ends the walk.
    // It is its own dial because EventRolls sizes eighteen other arrays; hanging shop depth on it
    // makes a 5000-slot shop allocate 5000 entries per pull and shop-source stream as well.
    public readonly int ShopSlots = shopSlots;
}

/// <summary>
/// JAMLyzer's filter: walks every ante's boss/voucher/tags/shop/packs and every relevant PRNG
/// stream for one seed, answering "what does this seed actually contain?". The walk itself is
/// <see cref="MotelyJamlyzerSeedWalk"/>; this desc is how
/// <see cref="MotelyJamlyzer.Analyze(Motely.Filters.Jaml.JamlConfig, int)"/> runs it standalone,
/// one seed per search, with a resume bag. To run the same walk on a search's own finds instead,
/// see <see cref="MotelyJamlyzerRiderDesc"/>. Unrelated to the CLI's legacy <c>--analyze</c>
/// flag (<see cref="MotelyUnitTestAnalyzer"/>).
/// </summary>
public sealed class MotelyJamlyzerFilterDesc(
    int[] antesToAnalyze,
    int eventRolls = 20,
    MotelyJamlyzerStreamStates? resumeFrom = null,
    int shopSlots = 0
) : IMotelySeedFilterDesc<MotelyJamlyzerFilterDesc.JamlyzerFilter>
{
    /// <summary>
    /// The last walked seed's breakdown, Score 0 and Tally null (the caller scores). Null until
    /// the filter has run — a seed the engine never handed over (invalid) leaves it null.
    /// </summary>
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

/// <summary>
/// The Jamlyzer's seed walk: one live seed context in, that seed's whole breakdown out. Pure —
/// every call builds a fresh result and touches no shared state, so it is safe from any worker
/// thread and any SIMD lane. Score is left 0 and Tally null because the caller already knows what
/// the seed scored: the standalone Jamlyzer scores it in the same search, and the analyze
/// provider copies the search's own scored row.
/// </summary>
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
        // Composite (pulls/shop) streams resume by replaying this many rolls (see state bag).
        int offset = resumeFrom?.RollOffset ?? 0;
        // The shop walks ShopSlots per window while the pull/shop-source queues walk EventRolls,
        // so the two consume their streams at different rates and cannot share one cursor.
        int shopOffset = resumeFrom?.ShopOffset ?? 0;

        MotelyRunState voucherState = new();
        MotelySingleBossStream bossStream = ctx.CreateBossStream();

        // Ante 0 is walked when the window names it, or when this seed's ante-1 voucher is
        // Hieroglyph/Petroglyph — buying it eases the run back to ante 0, so that round is real.
        int startAnte = window.StartAnte;
        if (startAnte == 1
            && ctx.GetAnteFirstVoucher(1, new MotelyRunState()) is MotelyVoucher.Hieroglyph or MotelyVoucher.Petroglyph)
            startAnte = 0;

        var antes = new MotelyJamlyzerAnteResult[window.MaxAnte - startAnte + 1];

        for (int ante = startAnte; ante <= window.MaxAnte; ante++)
        {
            // Ante 0 is the pre-run shop, reached by ante reduction (Hieroglyph / Petroglyph):
            // its shop, packs and tags are exactly what a JAML `antes: [0]` clause searches.
            // Its boss reads off an isolated stream + run state, because the run state the
            // search chains (voucher tiers, seen-boss pool) opens at ante 1 — sharing it here
            // would shift every ante 1..8 boss and upgrade ante 1's voucher.
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

            // Tags
            MotelySingleTagStream tagStream = ctx.CreateTagStream(ante);
            MotelyTag smallTag = ctx.GetNextTag(ref tagStream);
            MotelyTag bigTag = ctx.GetNextTag(ref tagStream);

            // Shop
            MotelySingleShopItemStream shopStream = ctx.CreateShopItemStream(ante);
            int maxSlots = window.ShopSlots > 0 ? window.ShopSlots : (ante <= 1 ? 15 : 50);
            MotelyItem[] shopItems = new MotelyItem[maxSlots];
            for (int i = 0; i < shopOffset + maxSlots; i++)
            {
                var item = ctx.GetNextShopItem(ref shopStream);
                if (i >= shopOffset)
                    shopItems[i - shopOffset] = item;
            }

            // Packs
            var packStream = ctx.CreateBoosterPackStream(ante);
            int maxPacks = ante <= 1 ? 4 : 6;
            MotelyJamlyzerPack[] packs = new MotelyJamlyzerPack[maxPacks];
            for (int i = 0; i < maxPacks; i++)
            {
                MotelyBoosterPack pack = ctx.GetNextBoosterPack(ref packStream);
                MotelyItem[] contents = GetPackContents(ref ctx, ante, pack, ref state).AsArray();
                packs[i] = new(pack, contents);
            }

            // pulls streams — card/joker-activated streams beyond shops/packs.
            // offset rolls are replayed-and-discarded so a resumed window is exact even for the
            // resample-backed streams (Emperor, Voucher) where state is not a single double.
            var pulls = CollectPulls(ref ctx, ante, voucherState, n, offset);

            // raw shop-source queues, read independently of the resolved shop above
            var shopStreams = CollectShopStreams(ref ctx, ante, n, offset);

            // Activate voucher AFTER collecting pulls streams so voucher sequence
            // resampling uses the correct state (pre-activation for this ante).
            // Ante 0 reports its voucher without entering the chain — the search starts the
            // chain at ante 1, so the analyzer reports the same ante-1 voucher it matches.
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

        // The shop advances by the depth actually walked this window, which is ShopSlots when the
        // caller set one and otherwise the per-ante default -- ante 1 walks 15, ante 2+ walks 50.
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
        // Nothing to walk and nothing to replay: building the streams would hash a dozen
        // PRNG keys per ante to then read none of them. `eventRolls: 0` is sold as the
        // cheap shape, so make it cheap.
        if (n == 0 && offset == 0)
            return new([], [], [], [], [], [], [], [], [], [], []);

        // Joker streams
        var judgementStream = ctx.CreateJudgementJokerStream(ante);
        var wraithStream = ctx.CreateWraithJokerStream(ante);
        var riffRaffStream = ctx.CreateRiffRaffJokerStream(ante);
        var rareTagStream = ctx.CreateRareTagJokerStream(ante);
        var uncommonTagStream = ctx.CreateUncommonTagJokerStream(ante);
        var legendaryStream = ctx.CreateLegendaryJokerStream(ante);

        // Tarot streams
        var emperorStream = ctx.CreateEmperorTarotStream(ante);
        var purpleSealStream = ctx.CreatePurpleSealTarotStream(ante);

        // Spectral streams
        var sixthSenseStream = ctx.CreateSixthSenseSpectralStream(ante);
        var seanceStream = ctx.CreateSeanceSpectralStream(ante);

        // Voucher sequence
        var voucherStream = ctx.CreateVoucherStream(ante);

        MotelyItem[] judgement = new MotelyItem[n];
        MotelyItem[] wraith = new MotelyItem[n];
        MotelyItem[] riffRaff = new MotelyItem[n];
        MotelyItem[] rareTag = new MotelyItem[n];
        MotelyItem[] uncommonTag = new MotelyItem[n];
        MotelyItem[] legendary = new MotelyItem[n];
        MotelyItem[] emperor = new MotelyItem[n * 2]; // 2 tarots per use
        MotelyItem[] purpleSeal = new MotelyItem[n];
        MotelyItem[] sixthSense = new MotelyItem[n];
        MotelyItem[] seance = new MotelyItem[n];
        MotelyVoucher[] vouchers = new MotelyVoucher[n];

        // Replay [0, offset) and discard, then keep [offset, offset+n). Re-running the exact
        // same calls advances every resample substream identically — exact resume by construction.
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
        // Same short-circuit as CollectPulls: no rolls and nothing to replay means the
        // seven streams below would be built only to be thrown away.
        if (n == 0 && offset == 0)
            return new([], [], [], [], [], [], []);

        // Shop-source streams share the keys the shop item queue consumes, but each raw
        // queue is read on its own copy of stream state — collecting them here does not
        // perturb the resolved shop above. None depend on voucher run-state.
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

        // Same offset-replay as pulls: discard [0, offset), keep [offset, offset+n).
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
        // No resume bag -> each stream starts at the seed's natural start.
        // With a resume bag -> each stream resumes from its saved State double, so the
        // window continues exactly where the previous one stopped (no prefix re-roll).
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

        // End-of-window state bag — hand straight back as resumeFrom. RollOffset advances by this
        // window's N so composite (pulls/shop) replay lands on the next window; the doubles let
        // the event streams resume exactly without re-rolling.
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
