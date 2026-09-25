namespace Motely;

public static partial class MotelyGlobals
{
    public const int MaxCachedPseudoHashKeyLength = 32;

    public static readonly char[] SeedDigits = [.. "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"];
    public const int MaxSeedLength = 8;
    public const int MaxVectorWidth = 8;

    public const int DefaultProviderBatchSeedCount = 35 * 35 * 35;

    public static string NormalizeSeed(string seed) =>
        seed.Trim().ToUpperInvariant().Replace('0', 'O');

    public const int ItemTypeMask = 0xFFFF;

    public const int StandardcardRankMask = 0b1111;
    public const int StandardcardSuitOffset = 4;
    public const int StandardcardSuitMask = 0b11 << StandardcardSuitOffset;

    public const int ItemTypeCategoryOffset = 12;
    public const int ItemTypeCategoryMask = 0b1111 << ItemTypeCategoryOffset;

    public const int JokerRarityOffset = 10;
    public const int JokerRarityMask = 0b11 << JokerRarityOffset;

    public const int ItemSealOffset = 16;
    public const int ItemSealMask = 0b111 << ItemSealOffset;

    public const int ItemEnhancementOffset = 19;
    public const int ItemEnhancementMask = 0b1111 << ItemEnhancementOffset;

    public const int ItemEditionOffset = 23;
    public const int ItemEditionMask = 0b111 << ItemEditionOffset;

    public const int BoosterPackTypeOffset = 2;
    public const int BoosterPackTypeMask = 0b11 << BoosterPackTypeOffset;
    public const int BoosterPackSizeMask = 0b11;

    public const int PerishableStickerOffset = 31;
    public const int EternalStickerOffset = 30;
    public const int RentalStickerOffset = 29;

    public const int BossTypeOffset = 31;
    public const int BossTypeMask = 0b1 << BossTypeOffset;
    public const int BossRequiredAnteOffset = 28;
    public const int BossRequiredAnteMask = 0b111 << BossRequiredAnteOffset;

    public const int JokerMisprintMin = 0;
    public const int JokerMisprintMax = 23;
    public const double JokerCavendishChance = 1000;
    public const double JokerGrosMichelChance = 6;
    public const double JokerSpaceChance = 4;
    public const double JokerBusinessChance = 2;
    public const double JokerBloodstoneChance = 2;
    public const double JokerParkingChance = 2;
    public const double JokerEightBallChance = 4;
    public const double CardGlassChance = 4;
    public const double VoucherOmenGlobeChance = 5;
    public const double BossTheWheelChance = 7;

    public const int EarlyAnteMaxPackSlot = 3;

    public const int LateAntesMaxPackSlot = 5;

    public const int MaxMapTagRollIndex = 5;

    public const int MaxMapVoucherRollIndex = 2;

    public const int MaxMapBossRollIndex = 2;

    public const double EnhancementLuckyMoneyChance = 15;
    public const double EnhancementLuckyMultChance = 5;

    public const double TarrotWheelChance = 4;

    public static readonly MotelyItem[] StandardCardPool =
    [
        new(MotelyStandardCard.TwoOfSpades),
        new(MotelyStandardCard.ThreeOfSpades),
        new(MotelyStandardCard.FourOfSpades),
        new(MotelyStandardCard.FiveOfSpades),
        new(MotelyStandardCard.SixOfSpades),
        new(MotelyStandardCard.SevenOfSpades),
        new(MotelyStandardCard.EightOfSpades),
        new(MotelyStandardCard.NineOfSpades),
        new(MotelyStandardCard.TenOfSpades),
        new(MotelyStandardCard.JackOfSpades),
        new(MotelyStandardCard.QueenOfSpades),
        new(MotelyStandardCard.KingOfSpades),
        new(MotelyStandardCard.AceOfSpades),
        new(MotelyStandardCard.TwoOfHearts),
        new(MotelyStandardCard.ThreeOfHearts),
        new(MotelyStandardCard.FourOfHearts),
        new(MotelyStandardCard.FiveOfHearts),
        new(MotelyStandardCard.SixOfHearts),
        new(MotelyStandardCard.SevenOfHearts),
        new(MotelyStandardCard.EightOfHearts),
        new(MotelyStandardCard.NineOfHearts),
        new(MotelyStandardCard.TenOfHearts),
        new(MotelyStandardCard.JackOfHearts),
        new(MotelyStandardCard.QueenOfHearts),
        new(MotelyStandardCard.KingOfHearts),
        new(MotelyStandardCard.AceOfHearts),
        new(MotelyStandardCard.TwoOfClubs),
        new(MotelyStandardCard.ThreeOfClubs),
        new(MotelyStandardCard.FourOfClubs),
        new(MotelyStandardCard.FiveOfClubs),
        new(MotelyStandardCard.SixOfClubs),
        new(MotelyStandardCard.SevenOfClubs),
        new(MotelyStandardCard.EightOfClubs),
        new(MotelyStandardCard.NineOfClubs),
        new(MotelyStandardCard.TenOfClubs),
        new(MotelyStandardCard.JackOfClubs),
        new(MotelyStandardCard.QueenOfClubs),
        new(MotelyStandardCard.KingOfClubs),
        new(MotelyStandardCard.AceOfClubs),
        new(MotelyStandardCard.TwoOfDiamonds),
        new(MotelyStandardCard.ThreeOfDiamonds),
        new(MotelyStandardCard.FourOfDiamonds),
        new(MotelyStandardCard.FiveOfDiamonds),
        new(MotelyStandardCard.SixOfDiamonds),
        new(MotelyStandardCard.SevenOfDiamonds),
        new(MotelyStandardCard.EightOfDiamonds),
        new(MotelyStandardCard.NineOfDiamonds),
        new(MotelyStandardCard.TenOfDiamonds),
        new(MotelyStandardCard.JackOfDiamonds),
        new(MotelyStandardCard.QueenOfDiamonds),
        new(MotelyStandardCard.KingOfDiamonds),
        new(MotelyStandardCard.AceOfDiamonds),
    ];

    public static char[]? ParsePaddingChars(string? padding)
    {
        if (string.IsNullOrEmpty(padding))
            return null;

        var chars = padding
            .ToUpperInvariant()
            .Where(c => Array.IndexOf(SeedDigits, c) >= 0)
            .Distinct()
            .ToArray();

        return chars.Length > 0 ? chars : null;
    }

    private static void ThrowIfShortKeywordWithoutExplicitPadding(
        string keyword,
        char[]? validChars,
        string paramName
    )
    {
        if (validChars != null || string.IsNullOrEmpty(keyword) || keyword.Length > 2)
            return;

        throw new ArgumentException(
            $"Keyword \"{keyword}\" has length {keyword.Length}; keywords of length 2 or less require explicit padding characters. "
                + "Pass a non-null validChars array (e.g. from ParsePaddingChars).",
            paramName
        );
    }

    public static IEnumerable<string> GeneratePaddedSeedsForKeywords(
        IEnumerable<string> keywords,
        char[]? validChars = null
    )
    {
        IEnumerable<string> sequence = keywords is IList<string> or ICollection<string>
            ? keywords
            : keywords.ToList();

        foreach (var keyword in sequence)
        {
            if (string.IsNullOrEmpty(keyword))
                continue;
            ThrowIfShortKeywordWithoutExplicitPadding(keyword, validChars, nameof(keywords));
        }

        foreach (var keyword in sequence)
        {
            if (string.IsNullOrEmpty(keyword))
                continue;
            int padLen = MaxSeedLength - keyword.Length;
            if (padLen < 0)
                continue;
            foreach (var seed in GeneratePaddedSeeds(keyword, padLen, validChars))
                yield return seed;
        }
    }

    public static ulong GetPaddedSeedCountForKeywords(
        IEnumerable<string> keywords,
        char[]? validChars = null
    )
    {
        ulong total = 0;
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrEmpty(keyword))
                continue;
            ThrowIfShortKeywordWithoutExplicitPadding(keyword, validChars, nameof(keywords));
            int padLen = MaxSeedLength - keyword.Length;
            if (padLen < 0)
                continue;
            total += GetPaddedSeedCount(keyword, padLen, validChars);
        }

        return total;
    }

    public static long GetPaddedSeedCountForKeywordsLong(
        IEnumerable<string> keywords,
        char[]? validChars = null
    )
    {
        ulong u = GetPaddedSeedCountForKeywords(keywords, validChars);
        return u > (ulong)long.MaxValue ? long.MaxValue : (long)u;
    }

    public static ulong GetPaddedSeedCount(string keyword, int padLen, char[]? validChars = null)
    {
        validChars ??= SeedDigits;

        if (validChars.Length == 0)
            throw new ArgumentException("validChars cannot be empty", nameof(validChars));
        if (string.IsNullOrEmpty(keyword))
            throw new ArgumentException("keyword cannot be null or empty", nameof(keyword));

        if (padLen <= 0)
            return 1;
        checked
        {
            ulong combinations = 1;
            for (int i = 0; i < padLen; i++)
                combinations *= (ulong)validChars.Length;

            return padLen switch
            {
                1 => combinations * 2,
                2 => combinations * 3,
                3 => combinations * 4,
                _ => combinations * (ulong)(padLen + 1),
            };
        }
    }

    public static IEnumerable<string> GeneratePaddedSeeds(
        string keyword,
        int padLen,
        char[]? validChars = null
    )
    {
        validChars ??= SeedDigits;

        if (validChars.Length == 0)
            throw new ArgumentException("validChars cannot be empty", nameof(validChars));
        if (string.IsNullOrEmpty(keyword))
            throw new ArgumentException("keyword cannot be null or empty", nameof(keyword));

        if (padLen <= 0)
        {
            yield return keyword;
            yield break;
        }

        if (padLen == 1)
        {
            int len = keyword.Length + 1;
            foreach (var c in validChars)
            {
                yield return string.Create(
                    len,
                    (c, keyword),
                    static (span, state) =>
                    {
                        span[0] = state.c;
                        state.keyword.AsSpan().CopyTo(span.Slice(1));
                    }
                );
                yield return string.Create(
                    len,
                    (c, keyword),
                    static (span, state) =>
                    {
                        state.keyword.AsSpan().CopyTo(span);
                        span[^1] = state.c;
                    }
                );
            }
        }
        else if (padLen == 2)
        {
            int len = keyword.Length + 2;
            foreach (var c1 in validChars)
            {
                foreach (var c2 in validChars)
                {
                    yield return string.Create(
                        len,
                        (c1, c2, keyword),
                        static (span, state) =>
                        {
                            span[0] = state.c1;
                            span[1] = state.c2;
                            state.keyword.AsSpan().CopyTo(span.Slice(2));
                        }
                    );
                    yield return string.Create(
                        len,
                        (c1, c2, keyword),
                        static (span, state) =>
                        {
                            state.keyword.AsSpan().CopyTo(span);
                            span[^2] = state.c1;
                            span[^1] = state.c2;
                        }
                    );
                    yield return string.Create(
                        len,
                        (c1, c2, keyword),
                        static (span, state) =>
                        {
                            span[0] = state.c1;
                            state.keyword.AsSpan().CopyTo(span.Slice(1));
                            span[^1] = state.c2;
                        }
                    );
                }
            }
        }
        else if (padLen == 3)
        {
            int len = keyword.Length + 3;
            foreach (var c1 in validChars)
            {
                foreach (var c2 in validChars)
                {
                    foreach (var c3 in validChars)
                    {
                        yield return string.Create(
                            len,
                            (c1, c2, c3, keyword),
                            static (span, state) =>
                            {
                                span[0] = state.c1;
                                span[1] = state.c2;
                                span[2] = state.c3;
                                state.keyword.AsSpan().CopyTo(span.Slice(3));
                            }
                        );
                        yield return string.Create(
                            len,
                            (c1, c2, c3, keyword),
                            static (span, state) =>
                            {
                                state.keyword.AsSpan().CopyTo(span);
                                span[^3] = state.c1;
                                span[^2] = state.c2;
                                span[^1] = state.c3;
                            }
                        );
                        yield return string.Create(
                            len,
                            (c1, c2, c3, keyword),
                            static (span, state) =>
                            {
                                span[0] = state.c1;
                                state.keyword.AsSpan().CopyTo(span.Slice(1));
                                span[^2] = state.c2;
                                span[^1] = state.c3;
                            }
                        );
                        yield return string.Create(
                            len,
                            (c1, c2, c3, keyword),
                            static (span, state) =>
                            {
                                span[0] = state.c1;
                                span[1] = state.c2;
                                state.keyword.AsSpan().CopyTo(span.Slice(2));
                                span[^1] = state.c3;
                            }
                        );
                    }
                }
            }
        }
        else
        {
            foreach (var seed in GenerateNPadVariations(keyword, padLen, validChars, ""))
                yield return seed;
        }
    }

    private static IEnumerable<string> GenerateNPadVariations(
        string keyword,
        int padLen,
        char[] validChars,
        string current
    )
    {
        if (current.Length == padLen)
        {
            for (int keywordStart = 0; keywordStart <= padLen; keywordStart++)
            {
                yield return current.Substring(0, keywordStart)
                    + keyword
                    + current.Substring(keywordStart);
            }
            yield break;
        }

        foreach (var c in validChars)
        {
            foreach (var seed in GenerateNPadVariations(keyword, padLen, validChars, current + c))
                yield return seed;
        }
    }
}
