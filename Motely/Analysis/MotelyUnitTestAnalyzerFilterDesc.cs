using System.ComponentModel;

namespace Motely.Analysis;

public sealed class MotelyUnitTestAnalyzerFilterDesc()
    : IMotelySeedFilterDesc<MotelyUnitTestAnalyzerFilterDesc.LegacyTextAnalyzerFilter>
{
    public MotelyUnitTestAnalysis? LastAnalysis { get; private set; } = null;

    public LegacyTextAnalyzerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        return new LegacyTextAnalyzerFilter(this);
    }

    public readonly struct LegacyTextAnalyzerFilter(MotelyUnitTestAnalyzerFilterDesc filterDesc)
        : IMotelySeedFilter
    {
        public MotelyUnitTestAnalyzerFilterDesc FilterDesc { get; } = filterDesc;

        public readonly VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            return ctx.SearchIndividualSeeds(CheckSeed);
        }

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

        public readonly int CheckSeed(MotelySingleSearchContext ctx)
        {
            MotelyRunState voucherState = new();
            MotelySingleBossStream bossStream = ctx.CreateBossStream();

            var deckStream = ctx.CreateErraticDeckPrngStream(isCached: false);
            var deckCards = new List<string>();

            for (int i = 0; i < 52; i++)
            {
                var card = ctx.GetNextErraticDeckCard(ref deckStream);
                deckCards.Add(FormatCardString(card.StandardcardRank, card.StandardcardSuit));
            }

            string startingDeck = string.Join(",", deckCards);

            List<MotelyAnteAnalysis> antes = [];

            for (int ante = 1; ante <= 8; ante++)
            {
                AnteAnalysisState state = new()
                {
                    ArcanaStream = default,
                    CelestialStream = default,
                    SpectralStream = default,
                    StandardStream = MotelySingleStandardCardStream.Invalid,
                    BuffoonStream = default,
                };

                MotelyBossBlind boss = ctx.GetBossForAnte(ref bossStream, ante, voucherState);

                MotelyVoucher voucher = ctx.GetAnteFirstVoucher(ante, voucherState);
                voucherState.ActivateVoucher(voucher);

                MotelySingleTagStream tagStream = ctx.CreateTagStream(ante);

                MotelyTag smallTag = ctx.GetNextTag(ref tagStream);
                MotelyTag bigTag = ctx.GetNextTag(ref tagStream);

                MotelySingleShopItemStream shopStream = ctx.CreateShopItemStream(ante);

                int maxSlots = ante == 1 ? 15 : 50;
                MotelyAnalyzedItem[] shopItems = new MotelyAnalyzedItem[maxSlots];

                for (int i = 0; i < maxSlots; i++)
                {
                    shopItems[i] = new(ctx.GetNextShopItem(ref shopStream));
                }

                var packStream = ctx.CreateBoosterPackStream(ante);
                int maxPacks = ante == 1 ? 4 : 6;
                MotelyBoosterPackAnalysis[] packs = new MotelyBoosterPackAnalysis[maxPacks];

                for (int i = 0; i < maxPacks; i++)
                {
                    MotelyBoosterPack pack = ctx.GetNextBoosterPack(ref packStream);
                    MotelySingleItemSet packContent = GetPackContents(
                        ref ctx,
                        ante,
                        pack,
                        ref state
                    );

                    packs[i] = new(
                        pack,
                        packContent
                            .AsArray()
                            .Select(static item => new MotelyAnalyzedItem(item))
                            .ToArray()
                    );
                }

                antes.Add(new(ante, boss, voucher, smallTag, bigTag, shopItems, packs, null));
            }

            string? deckComposition = ctx.Deck == MotelyDeck.Erratic ? startingDeck : null;
            string? deckBreakdown =
                ctx.Deck == MotelyDeck.Erratic ? GetErraticDeckBreakdown(deckCards) : null;

            FilterDesc.LastAnalysis = new(null, antes, ctx.Deck, deckComposition, deckBreakdown);

            return 0;
        }

        private static string FormatCardString(
            MotelyStandardcardRank rank,
            MotelyStandardcardSuit suit
        )
        {
            var rankStr = rank switch
            {
                MotelyStandardcardRank.Two => "2",
                MotelyStandardcardRank.Three => "3",
                MotelyStandardcardRank.Four => "4",
                MotelyStandardcardRank.Five => "5",
                MotelyStandardcardRank.Six => "6",
                MotelyStandardcardRank.Seven => "7",
                MotelyStandardcardRank.Eight => "8",
                MotelyStandardcardRank.Nine => "9",
                MotelyStandardcardRank.Ten => "10",
                MotelyStandardcardRank.Jack => "J",
                MotelyStandardcardRank.Queen => "Q",
                MotelyStandardcardRank.King => "K",
                MotelyStandardcardRank.Ace => "A",
                _ => rank.ToString(),
            };
            var suitStr = suit switch
            {
                MotelyStandardcardSuit.Clubs => "C",
                MotelyStandardcardSuit.Diamonds => "D",
                MotelyStandardcardSuit.Hearts => "H",
                MotelyStandardcardSuit.Spades => "S",
                _ => suit.ToString(),
            };
            return $"{rankStr}_{suitStr}";
        }

        private static string GetErraticDeckBreakdown(List<string> deckCards)
        {
            var rankCounts = new Dictionary<string, int>();
            var suitCounts = new Dictionary<char, int>
            {
                ['C'] = 0,
                ['D'] = 0,
                ['H'] = 0,
                ['S'] = 0,
            };

            foreach (var card in deckCards)
            {
                var parts = card.Split('_');
                if (parts.Length == 2)
                {
                    var rank = parts[0];
                    var suit = parts[1][0];

                    rankCounts[rank] = rankCounts.GetValueOrDefault(rank, 0) + 1;
                    if (suitCounts.ContainsKey(suit))
                        suitCounts[suit]++;
                }
            }

            int maxRankCount = rankCounts.Values.Max();
            int maxSuitCount = suitCounts.Values.Max();

            var sb = new System.Text.StringBuilder();

            string[] rankOrder = ["2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A"];
            sb.AppendLine("Ranks:");
            foreach (var rank in rankOrder)
            {
                int count = rankCounts.GetValueOrDefault(rank, 0);
                string marker = count == maxRankCount && count > 0 ? "*" : "";
                sb.AppendLine($"  {rank, 2}: {count}{marker}");
            }

            sb.AppendLine("Suits:");
            var suitSymbols = new Dictionary<char, string>
            {
                ['C'] = "♣",
                ['D'] = "♦",
                ['H'] = "♥",
                ['S'] = "♠",
            };
            foreach (var (suit, symbol) in suitSymbols)
            {
                int count = suitCounts[suit];
                string marker = count == maxSuitCount && count > 0 ? "*" : "";
                sb.AppendLine($"  {symbol}: {count}{marker}");
            }

            return sb.ToString().TrimEnd();
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
}
