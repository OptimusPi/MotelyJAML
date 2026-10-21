namespace Motely;

public static partial class MotelyGlobals
{
    public const int MaxCachedPseudoHashKeyLength = 32;

    public static readonly char[] SeedDigits = [.. "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"];
    public const int MaxSeedLength = 8;
    public const int MaxVectorWidth = 8;

    /// <summary>
    /// Default seeds per provider-mode report batch: <c>35³</c>. SIMD still processes
    /// <see cref="MaxVectorWidth"/> lanes at a time; this is how many seeds a provider plan
    /// chews before progress / auto-cutoff chat. Sequential batch size is different — it
    /// partitions the seed space by character count for partial-hash reuse.
    /// </summary>
    public const int DefaultProviderBatchSeedCount = 35 * 35 * 35; // 42_875

    /// <summary>
    /// Seeds in one sequential batch: a batch varies the last <paramref name="batchCharacterCount"/>
    /// characters, so it is 35^batchCharacterCount seeds (1,500,625 at 4). The one place this math
    /// lives; workers, the CLI and coordinators read it here instead of re-deriving it.
    /// </summary>
    public static long SeedsPerSequentialBatch(int batchCharacterCount) =>
        Pow35(ValidBatchCharacterCount(batchCharacterCount));

    /// <summary>Batches in the 8-character space at this batch size: 35^(8 - batchCharacterCount).</summary>
    public static long SequentialBatchCount(int batchCharacterCount) =>
        Pow35(MaxSeedLength - ValidBatchCharacterCount(batchCharacterCount));

    private static int ValidBatchCharacterCount(int batchCharacterCount) =>
        batchCharacterCount is >= 1 and < MaxSeedLength
            ? batchCharacterCount
            : throw new ArgumentOutOfRangeException(
                nameof(batchCharacterCount),
                batchCharacterCount,
                $"Batch character count must be 1-{MaxSeedLength - 1}."
            );

    private static long Pow35(int exponent)
    {
        long result = 1;
        for (int i = 0; i < exponent; i++)
            result *= SeedDigits.Length;
        return result;
    }

    /// <summary>
    /// Canonical seed normalization: uppercase and replace '0' with 'O'.
    /// Call this everywhere user-provided seeds enter the engine.
    /// </summary>
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

    // Booster-pack slot counts per ante (ante 1 = 4 packs / indices 0..3; antes 2+ = 6 / 0..5)
    // are inlined at each hot-path site via `ante == 1 ? ... : ...` — no helper function here
    // to avoid any temptation of call overhead in per-seed SIMD loops.

    /// <summary>Maximum pack-slot INDEX reachable in ante 1 through normal gameplay (4 packs, 0..3).</summary>
    public const int EarlyAnteMaxPackSlot = 3;

    /// <summary>Maximum pack-slot INDEX at antes 2+ (6 packs, 0..5). Not user-configurable.</summary>
    public const int LateAntesMaxPackSlot = 5;

    /// <summary>Tag-stream draw indices per ante: 0 small blind, 1 big blind, 2+ replay / double-tag extras.</summary>
    public const int MaxMapTagRollIndex = 5;

    /// <summary>Voucher-stream draw indices per ante: 0 ante award, 1+ Hieroglyph bonus / voucher-tag extras.</summary>
    public const int MaxMapVoucherRollIndex = 2;

    /// <summary>Boss roll 1+ needs full-run rewind simulation; filters match roll 0 today.</summary>
    public const int MaxMapBossRollIndex = 2;

    public const double EnhancementLuckyMoneyChance = 15;
    public const double EnhancementLuckyMultChance = 5;

    public const double TarrotWheelChance = 4;

    public static readonly MotelyItem[] StandardCardPool =
    [
        // Spades
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
        // Hearts
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
        // Clubs
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
        // Diamonds
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

    /// <summary>
    /// Parse a padding string like "67Z" into the specific chars to use for padding.
    /// Filters to valid seed digits, deduplicates, uppercases. Returns null if empty/invalid.
    /// </summary>
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

    /// <summary>
    /// Keywords of length 2 or less expand across the full seed alphabet when <paramref name="validChars"/> is null,
    /// producing an enormous search space. Callers must pass explicit padding characters (e.g. from <see cref="ParsePaddingChars"/>).
    /// </summary>
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

    /// <summary>
    /// Generate seeds for multiple keywords, combining their padded variations lazily.
    /// Each keyword is padded independently up to <see cref="MaxSeedLength"/>.
    /// </summary>
    public static IEnumerable<string> GeneratePaddedSeedsForKeywords(
        IEnumerable<string> keywords,
        char[]? validChars = null
    )
    {
        // Single-pass enumerators must be materialized so we validate all keywords before yielding.
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
                continue; // keyword too long — skip silently
            foreach (var seed in GeneratePaddedSeeds(keyword, padLen, validChars))
                yield return seed;
        }
    }

    /// <summary>
    /// Total seed count for multiple keywords with the given padding chars.
    /// </summary>
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

    /// <summary>
    /// Same as <see cref="GetPaddedSeedCountForKeywords"/> but returns a saturated <see cref="long"/> for provider APIs.
    /// </summary>
    public static long GetPaddedSeedCountForKeywordsLong(
        IEnumerable<string> keywords,
        char[]? validChars = null
    )
    {
        ulong u = GetPaddedSeedCountForKeywords(keywords, validChars);
        return u > (ulong)long.MaxValue ? long.MaxValue : (long)u;
    }

    /// <summary>
    /// Generate all seed variations by padding a keyword with the given valid characters.
    /// Pads 0-3 characters at all positions (prefix, suffix, infix).
    /// </summary>
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

            // Number of distinct keyword positions in a seed of length (keyword.Length + padLen)
            // is (padLen + 1) — one slot between each consecutive pair of pad chars plus the ends.
            // Earlier cases happen to equal padLen + 1; the general case was buggy with keyword.Length.
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
            // For padLen > 3, generate combinations recursively
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
            // Slide the keyword across the padding block: keyword at position 0..padLen,
            // splitting the PADDING (not the keyword). This matches the padLen 1/2/3 hand-
            // rolled cases and keeps the keyword contiguous for every emitted seed.
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
