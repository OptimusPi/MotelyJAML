using System.Diagnostics;

namespace Motely;

/// <summary>
/// Poker-hand evaluation over a multi-card span (starting draw = 8 cards after deck shuffle).
/// Lifted from the native <c>ShuffleFinder</c> filter so JAML and native share one law.
/// </summary>
public static class MotelyPokerHandEval
{
    private static readonly MotelyStandardcardRank[] StraightRankOrder =
    [
        MotelyStandardcardRank.Ace,
        MotelyStandardcardRank.Two,
        MotelyStandardcardRank.Three,
        MotelyStandardcardRank.Four,
        MotelyStandardcardRank.Five,
        MotelyStandardcardRank.Six,
        MotelyStandardcardRank.Seven,
        MotelyStandardcardRank.Eight,
        MotelyStandardcardRank.Nine,
        MotelyStandardcardRank.Ten,
        MotelyStandardcardRank.Jack,
        MotelyStandardcardRank.Queen,
        MotelyStandardcardRank.King,
        MotelyStandardcardRank.Ace,
    ];

    public readonly struct HandInfo(MotelyPokerHand type, int chips, int mult)
    {
        public MotelyPokerHand Type { get; } = type;
        public int Chips { get; } = chips;
        public int Mult { get; } = mult;

        public double Score => Chips * Mult;

        public double PlasmaScore
        {
            get
            {
                double floor = Math.Floor((double)(Chips + Mult));
                return floor * floor;
            }
        }
    }

    /// <summary>
    /// Best poker category present in <paramref name="hand"/> (full 8-card starting draw, or any span).
    /// Rank counts / flush / straight scan the whole span — same law as ShuffleFinder.
    /// </summary>
    public static HandInfo BestScore(Span<MotelyItem> hand)
    {
        hand.Sort((a, b) => ((int)a.StandardcardRank) - ((int)b.StandardcardRank));

        int clubSuitCount = 0;
        int diamondSuitCount = 0;
        int heartSuitCount = 0;
        int spadeSuitCount = 0;

        int bestScore = 0;
        int bestScoreChips = 0,
            bestScoreMult = 0;
        MotelyPokerHand bestHand = MotelyPokerHand.HighCard;

        int[] cardCounts = new int[MotelyEnum<MotelyStandardcardRank>.ValueCount];

        for (int i = 0; i < hand.Length; i++)
        {
            switch (hand[i].StandardcardSuit)
            {
                case MotelyStandardcardSuit.Clubs:
                    ++clubSuitCount;
                    break;
                case MotelyStandardcardSuit.Diamonds:
                    ++diamondSuitCount;
                    break;
                case MotelyStandardcardSuit.Hearts:
                    ++heartSuitCount;
                    break;
                case MotelyStandardcardSuit.Spades:
                    ++spadeSuitCount;
                    break;
            }

            MotelyStandardcardRank rank = hand[i].StandardcardRank;

            int rankCount = ++cardCounts[(int)rank];

            int chips = 0,
                mult = 0;
            MotelyPokerHand handType = MotelyPokerHand.HighCard;

            switch (rankCount)
            {
                case 1:
                    chips = 5 + GetCardChips(rank);
                    mult = 1;
                    handType = MotelyPokerHand.HighCard;
                    break;
                case 2:
                    chips = 10 + 2 * GetCardChips(rank);
                    mult = 2;
                    handType = MotelyPokerHand.Pair;
                    break;
                case 3:
                    chips = 30 + 3 * GetCardChips(rank);
                    mult = 3;
                    handType = MotelyPokerHand.ThreeOfAKind;
                    break;
                case 4:
                    chips = 60 + 4 * GetCardChips(rank);
                    mult = 7;
                    handType = MotelyPokerHand.FourOfAKind;
                    break;
            }

            if (mult * chips > bestScore)
            {
                bestScoreChips = chips;
                bestScoreMult = mult;
                bestScore = bestScoreChips * bestScoreMult;
                bestHand = handType;
            }
        }

        const int straightStartingCardCount = 10;

        bool[] straightStart = new bool[straightStartingCardCount];
        bool hasStraight = false;

        for (int i = 0; i < straightStartingCardCount; i++)
        {
            bool matches = true;

            for (int j = 0; j < 5; j++)
            {
                if (cardCounts[(int)StraightRankOrder[i + j]] == 0)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                straightStart[i] = true;
                hasStraight = true;

                int chips = 30;
                for (int j = 0; j < 5; j++)
                    chips += GetCardChips(StraightRankOrder[i + j]);

                if (chips * 4 > bestScore)
                {
                    bestScoreChips = chips;
                    bestScoreMult = 4;
                    bestScore = bestScoreChips * bestScoreMult;
                    bestHand = MotelyPokerHand.Straight;
                }
            }
        }

        void ScoreFlush(Span<MotelyItem> cards, MotelyStandardcardSuit suit)
        {
            int chips = 35;
            int cardCount = 0;

            for (int j = 0; j < cards.Length; j++)
            {
                if (cards[j].StandardcardSuit == suit)
                {
                    ++cardCount;
                    chips += GetCardChips(cards[j].StandardcardRank);
                    if (cardCount == 5)
                        break;
                }
            }

            Debug.Assert(cardCount == 5);

            if (chips * 4 > bestScore)
            {
                bestScoreChips = chips;
                bestScoreMult = 4;
                bestScore = bestScoreChips * bestScoreMult;
                bestHand = MotelyPokerHand.Flush;
            }
        }

        if (clubSuitCount >= 5)
            ScoreFlush(hand, MotelyStandardcardSuit.Clubs);
        if (diamondSuitCount >= 5)
            ScoreFlush(hand, MotelyStandardcardSuit.Diamonds);
        if (heartSuitCount >= 5)
            ScoreFlush(hand, MotelyStandardcardSuit.Hearts);
        if (spadeSuitCount >= 5)
            ScoreFlush(hand, MotelyStandardcardSuit.Spades);

        void SearchForStraightFlush(Span<MotelyItem> cards, MotelyStandardcardSuit suit)
        {
            for (int i = 0; i < straightStartingCardCount; i++)
            {
                bool matches = true;

                for (int j = 0; j < 5; j++)
                {
                    if (
                        !CardMatches(
                            cards,
                            card =>
                                card.StandardcardRank == StraightRankOrder[i + j]
                                && card.StandardcardSuit == suit
                        )
                    )
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    int chips = 100;
                    for (int j = 0; j < 5; j++)
                        chips += GetCardChips(StraightRankOrder[i + j]);

                    if (chips * 8 > bestScore)
                    {
                        bestScoreChips = chips;
                        bestScoreMult = 8;
                        bestScore = bestScoreChips * bestScoreMult;
                        bestHand = MotelyPokerHand.StraightFlush;
                    }
                }
            }
        }

        if (hasStraight)
        {
            if (clubSuitCount >= 5)
                SearchForStraightFlush(hand, MotelyStandardcardSuit.Clubs);
            if (diamondSuitCount >= 5)
                SearchForStraightFlush(hand, MotelyStandardcardSuit.Diamonds);
            if (heartSuitCount >= 5)
                SearchForStraightFlush(hand, MotelyStandardcardSuit.Hearts);
            if (spadeSuitCount >= 5)
                SearchForStraightFlush(hand, MotelyStandardcardSuit.Spades);
        }

        {
            int threeRank = -1;
            int twoRankA = -1;
            int twoRankB = -1;

            for (int i = cardCounts.Length - 1; i >= 0; i--)
            {
                int cardCount = cardCounts[i];

                if (cardCount >= 2)
                {
                    if (twoRankA == -1)
                        twoRankA = i;
                    else if (twoRankB == -1)
                        twoRankB = i;

                    if (cardCount == 3 && threeRank == -1)
                        threeRank = i;
                }
            }

            if (threeRank != -1 && twoRankA != -1)
            {
                int chips =
                    40
                    + 3 * GetCardChips((MotelyStandardcardRank)threeRank)
                    + 2 * GetCardChips((MotelyStandardcardRank)twoRankA);
                if (chips * 4 > bestScore)
                {
                    bestScoreChips = chips;
                    bestScoreMult = 4;
                    bestScore = bestScoreChips * bestScoreMult;
                    bestHand = MotelyPokerHand.FullHouse;
                }
            }

            if (twoRankA != -1 && twoRankB != -1)
            {
                int chips =
                    20
                    + 2 * GetCardChips((MotelyStandardcardRank)twoRankA)
                    + 2 * GetCardChips((MotelyStandardcardRank)twoRankB);
                if (chips * 2 > bestScore)
                {
                    bestScoreChips = chips;
                    bestScoreMult = 2;
                    bestScore = bestScoreChips * bestScoreMult;
                    bestHand = MotelyPokerHand.TwoPair;
                }
            }
        }

        return new HandInfo(bestHand, bestScoreChips, bestScoreMult);
    }

    /// <summary>
    /// Balatro deck shuffle stream for the starting hand of <paramref name="ante"/>.
    /// The game keys this stream on the ante, not the round: <c>G.deck:shuffle('nr'..
    /// G.GAME.round_resets.ante)</c> (state_events.lua:344). Every blind played in that ante draws
    /// a successive value from this one stream — see <c>Shuffle</c>'s <c>advance</c> parameter.
    /// Ante 1 matches existing <c>startingDraw</c> / ShuffleFinder (<c>nr1</c>).
    /// </summary>
    public static string ShuffleKeyForAnte(int ante) =>
        ante <= 1 ? "nr1" : $"nr{ante}";

    public static int GetCardChips(MotelyStandardcardRank rank) =>
        rank switch
        {
            MotelyStandardcardRank.Ace => 11,
            MotelyStandardcardRank.King => 10,
            MotelyStandardcardRank.Queen => 10,
            MotelyStandardcardRank.Jack => 10,
            MotelyStandardcardRank.Ten => 10,
            MotelyStandardcardRank.Nine => 9,
            MotelyStandardcardRank.Eight => 8,
            MotelyStandardcardRank.Seven => 7,
            MotelyStandardcardRank.Six => 6,
            MotelyStandardcardRank.Five => 5,
            MotelyStandardcardRank.Four => 4,
            MotelyStandardcardRank.Three => 3,
            MotelyStandardcardRank.Two => 2,
            _ => throw new InvalidOperationException(),
        };

    private static bool CardMatches(Span<MotelyItem> hand, Predicate<MotelyItem> predicate)
    {
        foreach (MotelyItem item in hand)
        {
            if (predicate(item))
                return true;
        }
        return false;
    }
}
