using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely;

public struct FilledSoulFilterDesc() : IMotelySeedFilterDesc<FilledSoulFilterDesc.FilterStruct>
{
    public const int MinAnte = 0;
    public const int MaxAnte = 0;
    public const int SoulsInARow = 2;
    public const int hAnte = 1;

    public FilterStruct CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        ctx.CachePseudoHash(MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + 0);
        ctx.CachePseudoHash(MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + 1);
        for (int ante = 0; ante <= 1; ante++)
        {
            ctx.CacheAnteFirstVoucher(ante);
            ctx.CacheBoosterPackStream(ante);
        }
        return new FilterStruct();
    }

    public struct FilterStruct() : IMotelySeedFilter
    {
        [MethodImpl(
            MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization
        )]
        public static int CheckAnteForSoulJoker(
            int ante,
            ref MotelySingleSearchContext searchContext
        )
        {
            MotelySingleTarotStream tarotStream = searchContext.CreateArcanaPackTarotStream(
                ante,
                true
            );
            ;
            MotelySingleSpectralStream spectralStream = default;
            MotelySingleJokerFixedRarityStream soulStream = searchContext.CreateSoulJokerStream(
                ante
            );
            MotelySingleBoosterPackStream boosterPackStream = default;
            bool boosterPackStreamInit = false,
                tarotStreamInit = false,
                spectralStreamInit = false;
            MotelySingleTagStream tagStream = searchContext.CreateTagStream(ante);
            var buffoonStream = searchContext.CreateBuffoonPackJokerStream(ante);

            int perkeo = 0;
            int negSouls = 0;
            int souls = 0;
            int showman = 0;

            for (int i = 0; i < 2; i++)
            {
                if (!boosterPackStreamInit)
                {
                    boosterPackStream = searchContext.CreateBoosterPackStream(ante, false, false);
                    boosterPackStreamInit = true;
                }

                var pack = searchContext.GetNextBoosterPack(ref boosterPackStream);

                if (pack.GetPackType() == MotelyBoosterPackType.Buffoon)
                {
                    var contents = searchContext.GetNextBuffoonPackContents(
                        ref buffoonStream,
                        pack.GetPackSize()
                    );

                    for (int j = 0; j < contents.Length; j++)
                    {
                        var item = contents[j];

                        if (item.Type == MotelyItemType.Showman)
                        {
                            showman++;
                        }
                    }

                    if (showman == 0 && ante == 1)
                        return 0;
                }
                else if (pack.GetPackType() == MotelyBoosterPackType.Spectral)
                {
                    if (!spectralStreamInit)
                    {
                        spectralStreamInit = true;
                        spectralStream = searchContext.CreateSpectralPackSpectralStream(ante, true);
                    }

                    if (
                        searchContext.GetNextSpectralPackHasTheSoul(
                            ref spectralStream,
                            pack.GetPackSize()
                        )
                    )
                    {
                        souls++;
                        var jok = searchContext.GetNextJoker(ref soulStream);
                        if (jok.Type == MotelyItemType.Perkeo)
                            perkeo++;
                        if (jok.Edition == MotelyItemEdition.Negative)
                            negSouls++;
                    }
                }
                else if (pack.GetPackType() == MotelyBoosterPackType.Arcana)
                {
                    if (!tarotStreamInit)
                    {
                        tarotStreamInit = true;
                        tarotStream = searchContext.CreateArcanaPackTarotStream(ante, true);
                    }

                    if (
                        searchContext.GetNextArcanaPackHasTheSoul(
                            ref tarotStream,
                            pack.GetPackSize()
                        )
                    )
                    {
                        souls++;
                        var jok = searchContext.GetNextJoker(ref soulStream);
                        if (jok.Type == MotelyItemType.Perkeo)
                            perkeo++;
                        if (jok.Edition == MotelyItemEdition.Negative)
                            negSouls++;
                    }
                }
                else if (
                    i == 1
                    && ante == 0
                    && searchContext.GetNextTag(ref tagStream) == MotelyTag.CharmTag
                )
                {
                    if (!tarotStreamInit)
                    {
                        tarotStreamInit = true;
                        tarotStream = searchContext.CreateArcanaPackTarotStream(ante, true);
                    }

                    if (
                        searchContext.GetNextArcanaPackHasTheSoul(
                            ref tarotStream,
                            pack.GetPackSize()
                        )
                    )
                    {
                        souls++;
                        var jok = searchContext.GetNextJoker(ref soulStream);
                        if (jok.Type == MotelyItemType.Perkeo)
                            perkeo++;
                        if (jok.Edition == MotelyItemEdition.Negative)
                            negSouls++;
                    }
                }
            }

            return (souls * 100) + (negSouls * 10) + perkeo;
        }

        public VectorMask Filter(ref MotelyVectorSearchContext searchContext)
        {
            MotelyVectorRunState voucherState = new();

            VectorEnum256<MotelyVoucher> vouchers = searchContext.GetAnteFirstVoucher(
                1,
                voucherState
            );
            VectorMask hiero = VectorEnum256.Equals(vouchers, MotelyVoucher.Hieroglyph);

            if (hiero.IsAllFalse())
                return VectorMask.NoBitsSet;

            MotelyVectorPrngStream vectorSoulStream0 = searchContext.CreatePrngStream(
                MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + (hAnte - 1)
            );
            MotelyVectorPrngStream vectorSoulStream1 = searchContext.CreatePrngStream(
                MotelyPrngKeys.TarotSoul + MotelyPrngKeys.Tarot + hAnte
            );

            Vector512<double> soulPoll = searchContext.GetNextRandom(ref vectorSoulStream0);
            VectorMask preMask = Vector512.GreaterThan(soulPoll, Vector512.Create(0.997));

            if (preMask.IsAllFalse())
                return VectorMask.NoBitsSet;

            preMask = Vector512.GreaterThan(soulPoll, Vector512.Create(0.997));
            if (preMask.IsAllFalse())
                return VectorMask.NoBitsSet;

            return searchContext.SearchIndividualSeeds(
                hiero,
                (ref MotelySingleSearchContext searchContext) =>
                {
                    // Real verify step
                    int score1 = CheckAnteForSoulJoker(hAnte - 1, ref searchContext);

                    if (score1 < 110)
                        return false;
                    int score2 = CheckAnteForSoulJoker(hAnte, ref searchContext);

                    if (score1 + score2 >= 111)
                    {
                        return true;
                    }

                    return false;
                }
            );
        }
    }
}
