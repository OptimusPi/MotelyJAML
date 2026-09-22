using System;
using System.Collections.Generic;
using System.Linq;

namespace Motely.Filters.Jaml;

/// <summary>
/// One line, one JAML clause. Formatting/parsing delegates item and enum
/// spellings to the engine's own canonical formatters whenever they exist.
/// </summary>
public static class JamlLine
{
    private const string VoucherPrefix = "Voucher ";
    private const string TagPrefix = "Tag ";
    private const string SmallBlindTagPrefix = "Small Blind Tag ";
    private const string BigBlindTagPrefix = "Big Blind Tag ";
    private const string BossPrefix = "Boss ";
    private const string StartingDrawPrefix = "Starting Draw ";

    // ── Clause → line ─────────────────────────────────────────────────────────

    public static string? FromClause(IJamlClause clause) =>
        clause switch
        {
            JokerClause j => FromJoker(j),
            TarotCardClause t => FromConsumable(t.Tarots, t.Antes),
            SpectralCardClause s => FromConsumable(s.Spectrals, s.Antes),
            PlanetCardClause p => FromConsumable(p.Planets, p.Antes),
            StandardCardClause s => FromStandardCard(s),
            VoucherClause v => FromVoucher(v),
            TagClause t => FromTag(t),
            BossClause b => FromBoss(b),
            StartingDrawClause s => FromStartingDraw(s),
            LuckyMoneyClause e => FromRollEvent("Lucky Money", e.Rolls, e.With),
            LuckyMultClause e => FromRollEvent("Lucky Mult", e.Rolls, e.With),
            MisprintMultClause e => FromMisprint(e),
            WheelOfFortuneClause e => FromRollEvent("Wheel of Fortune", e.Rolls, e.With),
            GrosMichelExtinctClause e => FromRollEvent("Gros Michel Extinct", e.Rolls, e.With),
            CavendishExtinctClause e => FromRollEvent("Cavendish Extinct", e.Rolls, e.With),
            SpaceLevelupClause e => FromRollEvent("Space Levelup", e.Rolls, e.With),
            GlassDestroyClause e => FromRollEvent("Glass Destroy", e.Rolls, e.With),
            WheelStaysFlippedClause e => FromRollEvent("Wheel Stays Flipped", e.Rolls, e.With),
            BusinessPayoutClause e => FromRollEvent("Business Payout", e.Rolls, null),
            BloodstoneTriggerClause e => FromRollEvent("Bloodstone Trigger", e.Rolls, null),
            ParkingPayoutClause e => FromRollEvent("Parking Payout", e.Rolls, null),
            _ => null,
        };

    private static string? FromConsumable<T>(T[] values, int[] antes)
        where T : struct, Enum
    {
        if (values.Length != 1)
            return null;
        if (!Enum.TryParse<MotelyItemType>(values[0].ToString(), out var type))
            return null;
        return FormatUtils.FormatItem(new MotelyItem(type)) + AnteTail(antes);
    }

    private static string? FromJoker(JokerClause clause)
    {
        // Category wildcard (empty jokers) has no line spelling — block form only.
        if (clause.Jokers.Length != 1)
            return null;
        var item = new MotelyItem(clause.Jokers[0], clause.Edition ?? MotelyItemEdition.None);
        item = ApplyStickers(item, clause.Stickers);
        string head = FormatUtils.FormatItem(item);

        return head + AnteTail(clause.Antes);
    }

    private static string? FromStandardCard(StandardCardClause clause)
    {
        if (clause.Rank is not { } rank || clause.Suit is not { } suit)
            return null;

        var card = (MotelyStandardCard)((int)rank | (int)suit);
        var item = new MotelyItem(card);
        if (clause.Seal is { } seal)
            item = item.WithSeal(seal);
        if (clause.Edition is { } edition)
            item = item.WithEdition(edition);
        if (clause.Enhancement is { } enhancement)
            item = item.WithEnhancement(enhancement);
        return FormatUtils.FormatItem(item) + AnteTail(clause.Antes);
    }

    private static string? FromStartingDraw(StartingDrawClause clause)
    {
        if (clause.Rank is not { } rank || clause.Suit is not { } suit)
            return null;
        var card = (MotelyStandardCard)((int)rank | (int)suit);
        return StartingDrawPrefix
            + FormatUtils.FormatItem(new MotelyItem(card))
            + AnteTail(clause.Antes);
    }

    private static string? FromVoucher(VoucherClause clause)
    {
        if (clause.Vouchers.Length != 1)
            return null;
        return VoucherPrefix
            + FormatUtils.FormatVoucher(clause.Vouchers[0])
            + RollsTail(clause.Rolls)
            + AnteTail(clause.Antes);
    }

    private static string? FromTag(TagClause clause)
    {
        if (clause.Tags.Length != 1)
            return null;
        var prefix = clause.Rolls switch
        {
            [0] => SmallBlindTagPrefix,
            [1] => BigBlindTagPrefix,
            _ => TagPrefix,
        };
        var rolls = prefix == TagPrefix ? RollsTail(clause.Rolls) : "";
        return prefix + FormatUtils.FormatTag(clause.Tags[0]) + rolls + AnteTail(clause.Antes);
    }

    private static string? FromBoss(BossClause clause)
    {
        if (clause.Bosses.Length != 1)
            return null;
        return BossPrefix + FormatUtils.FormatBoss(clause.Bosses[0]) + AnteTail(clause.Antes);
    }

    private static string FromMisprint(MisprintMultClause clause) =>
        "Misprint Mult" + RollsTail(clause.Rolls) + $" mult {clause.Mult}";

    private static string FromRollEvent(string name, int[] rolls, JamlWith? with)
    {
        var line = name + RollsTail(rolls);
        if (with?.Luck is { } luck && luck != MotelyLuck.X1)
            line += $" with luck {(int)luck}";
        return line;
    }

    private static MotelyItem ApplyStickers(MotelyItem item, MotelyJokerSticker[] stickers)
    {
        foreach (var sticker in stickers)
            item = sticker switch
            {
                MotelyJokerSticker.Eternal => item.WithEternal(true),
                MotelyJokerSticker.Perishable => item.WithPerishable(true),
                MotelyJokerSticker.Rental => item.WithRental(true),
                _ => item,
            };
        return item;
    }

    private static string RollsTail(int[] rolls)
    {
        if (rolls is not { Length: > 0 })
            return "";
        return " rolls " + JoinNumbers(rolls);
    }

    private static string AnteTail(int[] antes)
    {
        if (antes is not { Length: > 0 })
            return "";
        if (antes.Length == 1)
            return $" in ante {antes[0]}";
        return " in antes " + JoinNumbers(antes);
    }

    // Collapse ascending consecutive runs into ranges the parser reads back ("1-6"),
    // so the writer speaks the same range dialect the reader already accepts.
    private static string JoinNumbers(int[] values)
    {
        if (values.Length == 0)
            return "";
        var parts = new List<string>();
        int runStart = values[0], prev = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] == prev + 1) { prev = values[i]; continue; }
            parts.Add(FormatRun(runStart, prev));
            runStart = prev = values[i];
        }
        parts.Add(FormatRun(runStart, prev));
        return string.Join(" or ", parts);
    }

    // A single number stays itself; an adjacent pair reads "2 or 3"; three or more
    // become an inclusive "1-6" range.
    private static string FormatRun(int start, int end) =>
        start == end ? start.ToString()
        : end == start + 1 ? $"{start} or {end}"
        : $"{start}-{end}";

    /// <summary>Null when <paramref name="line"/> parses as one-line JAML; the parser's error otherwise.</summary>
    public static string? Validate(string line) =>
        TryToClause(line, out _, out string? error) ? null : error;

    /// <summary>The canonical spelling of <paramref name="line"/>: parse, then format back.</summary>
    /// <exception cref="FormatException">The line does not parse.</exception>
    public static string Canonicalize(string line)
    {
        if (!TryToClause(line, out IJamlClause? clause, out string? error))
            throw new FormatException(error);
        return FromClause(clause!) ?? throw new InvalidOperationException(line);
    }

    // ── Line → clause ─────────────────────────────────────────────────────────

    public static bool TryToClause(string line, out IJamlClause? clause, out string? error)
    {
        clause = null;
        error = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "Empty JAML line.";
            return false;
        }

        // A trailing "score N" is captured here and applied to whatever clause the rest parses to,
        // so a whole should clause fits on one forgiving line — "Perkeo in antes 1-8 score 100" —
        // instead of fragile multi-line YAML (a bare list item can't carry an indented `score:`).
        var (withoutScore, score, scoreError) = SplitScoreTail(line.Trim());
        if (scoreError != null)
        {
            error = scoreError;
            return false;
        }

        if (!TryToClauseCore(withoutScore, out clause, out error))
            return false;

        if (score is { } s)
            clause!.Score = s;
        return true;
    }

    private static bool TryToClauseCore(string line, out IJamlClause? clause, out string? error)
    {
        clause = null;
        error = null;

        var (withoutAnte, antes, anteError) = SplitAnteTail(line.Trim());
        if (anteError != null)
        {
            error = anteError;
            return false;
        }

        if (TryParsePrefixed(withoutAnte, StartingDrawPrefix, out var startingText))
            return TryParseStartingDraw(startingText, antes, out clause, out error);
        if (TryParsePrefixed(withoutAnte, SmallBlindTagPrefix, out var smallTagText))
            return TryParseTag(smallTagText, [0], antes, out clause, out error);
        if (TryParsePrefixed(withoutAnte, BigBlindTagPrefix, out var bigTagText))
            return TryParseTag(bigTagText, [1], antes, out clause, out error);
        if (TryParsePrefixed(withoutAnte, TagPrefix, out var tagText))
            return TryParseGenericTag(tagText, antes, out clause, out error);
        if (TryParsePrefixed(withoutAnte, VoucherPrefix, out var voucherText))
            return TryParseVoucher(voucherText, antes, out clause, out error);
        if (TryParsePrefixed(withoutAnte, BossPrefix, out var bossText))
            return TryParseBoss(bossText, antes, out clause, out error);
        if (TryParseEvent(withoutAnte, out clause, out error))
            return true;
        if (error != null)
            return false;

        // No bare "Any" token — category match is empty disc in block form only.

        if (!FormatUtils.TryParseMotelyItem(withoutAnte, out var item))
        {
            error = $"Unrecognized item: '{withoutAnte}'.";
            return false;
        }

        switch (item.TypeCategory)
        {
            case MotelyItemTypeCategory.Joker when TryExtractJoker(item, out var joker):
                clause = new JokerClause
                {
                    Jokers = [joker],
                    Edition = item.Edition == MotelyItemEdition.None ? null : item.Edition,
                    Stickers = StickersOf(item),
                    Antes = antes,
                };
                return true;

            case MotelyItemTypeCategory.TarotCard
                when TryParseSpecific<MotelyTarotCard>(item, out var tarot):
                clause = new TarotCardClause { Tarots = [tarot], Antes = antes };
                return true;

            case MotelyItemTypeCategory.SpectralCard
                when TryParseSpecific<MotelySpectralCard>(item, out var spectral):
                clause = new SpectralCardClause { Spectrals = [spectral], Antes = antes };
                return true;

            case MotelyItemTypeCategory.PlanetCard
                when TryParseSpecific<MotelyPlanetCard>(item, out var planet):
                clause = new PlanetCardClause { Planets = [planet], Antes = antes };
                return true;

            case MotelyItemTypeCategory.Standardcard:
                clause = new StandardCardClause
                {
                    Rank = item.StandardcardRank,
                    Suit = item.StandardcardSuit,
                    Seal = item.Seal == MotelyItemSeal.None ? null : item.Seal,
                    Edition = item.Edition == MotelyItemEdition.None ? null : item.Edition,
                    Enhancement =
                        item.Enhancement == MotelyItemEnhancement.None ? null : item.Enhancement,
                    Antes = antes,
                };
                return true;
        }

        error =
            $"Item '{withoutAnte}' isn't a supported one-line clause yet (category {item.TypeCategory}).";
        return false;
    }

    private static bool TryParseStartingDraw(
        string text,
        int[] antes,
        out IJamlClause? clause,
        out string? error
    )
    {
        clause = null;
        if (
            !FormatUtils.TryParseMotelyItem(text, out var item)
            || item.TypeCategory != MotelyItemTypeCategory.Standardcard
        )
        {
            error = $"Starting Draw requires a standard card, got '{text}'.";
            return false;
        }

        if (
            item.Seal != MotelyItemSeal.None
            || item.Edition != MotelyItemEdition.None
            || item.Enhancement != MotelyItemEnhancement.None
        )
        {
            error = "Starting Draw supports rank/suit only.";
            return false;
        }

        clause = new StartingDrawClause
        {
            Rank = item.StandardcardRank,
            Suit = item.StandardcardSuit,
            Antes = antes,
        };
        error = null;
        return true;
    }

    private static bool TryParseVoucher(
        string text,
        int[] antes,
        out IJamlClause? clause,
        out string? error
    )
    {
        var (head, rolls, rollError) = SplitRollsTail(text);
        if (rollError != null)
        {
            clause = null;
            error = rollError;
            return false;
        }

        if (!TryParseFormattedEnum(head, FormatUtils.FormatVoucher, out MotelyVoucher voucher))
        {
            clause = null;
            error = $"Unrecognized voucher: '{head}'.";
            return false;
        }

        clause = new VoucherClause
        {
            Vouchers = [voucher],
            Rolls = rolls.Length == 0 ? [0] : rolls,
            Antes = antes,
        };
        error = null;
        return true;
    }

    private static bool TryParseGenericTag(
        string text,
        int[] antes,
        out IJamlClause? clause,
        out string? error
    )
    {
        var (head, rolls, rollError) = SplitRollsTail(text);
        if (rollError != null)
        {
            clause = null;
            error = rollError;
            return false;
        }
        return TryParseTag(head, rolls.Length == 0 ? [0, 1] : rolls, antes, out clause, out error);
    }

    private static bool TryParseTag(
        string text,
        int[] rolls,
        int[] antes,
        out IJamlClause? clause,
        out string? error
    )
    {
        if (!TryParseFormattedEnum(text, FormatUtils.FormatTag, out MotelyTag tag))
        {
            clause = null;
            error = $"Unrecognized tag: '{text}'.";
            return false;
        }

        clause = new TagClause
        {
            Tags = [tag],
            Rolls = rolls,
            Antes = antes,
        };
        error = null;
        return true;
    }

    private static bool TryParseBoss(
        string text,
        int[] antes,
        out IJamlClause? clause,
        out string? error
    )
    {
        if (!TryParseFormattedEnum(text, FormatUtils.FormatBoss, out MotelyBossBlind boss))
        {
            clause = null;
            error = $"Unrecognized boss: '{text}'.";
            return false;
        }

        clause = new BossClause { Bosses = [boss], Antes = antes };
        error = null;
        return true;
    }

    private static bool TryParseEvent(string text, out IJamlClause? clause, out string? error)
    {
        clause = null;
        error = null;

        if (TryParseEventPayload(text, "Misprint Mult", out var misprintPayload))
        {
            var (withoutMult, mult, multError) = SplitRequiredIntTail(misprintPayload, " mult ");
            if (multError != null)
            {
                error = multError;
                return false;
            }
            var (rolls, rollError) = ParseRequiredRolls(withoutMult, "Misprint Mult");
            if (rollError != null)
            {
                error = rollError;
                return false;
            }
            clause = new MisprintMultClause { Rolls = rolls, Mult = mult };
            return true;
        }

        foreach (var spec in EventSpecs)
        {
            if (!TryParseEventPayload(text, spec.Name, out var payload))
                continue;

            var (withoutLuck, luck, luckError) = SplitLuckTail(payload, spec.AllowLuck);
            if (luckError != null)
            {
                error = luckError;
                return false;
            }

            var (rolls, rollError) = ParseRequiredRolls(withoutLuck, spec.Name);
            if (rollError != null)
            {
                error = rollError;
                return false;
            }

            var with = new JamlWith { Luck = luck ?? MotelyLuck.X1 };
            clause = spec.Create(rolls, with);
            return true;
        }

        return false;
    }

    private static readonly EventSpec[] EventSpecs =
    [
        new(
            "Lucky Money",
            true,
            static (rolls, with) => new LuckyMoneyClause { Rolls = rolls, With = with }
        ),
        new(
            "Lucky Mult",
            true,
            static (rolls, with) => new LuckyMultClause { Rolls = rolls, With = with }
        ),
        new(
            "Wheel of Fortune",
            true,
            static (rolls, with) => new WheelOfFortuneClause { Rolls = rolls, With = with }
        ),
        new(
            "Gros Michel Extinct",
            true,
            static (rolls, with) => new GrosMichelExtinctClause { Rolls = rolls, With = with }
        ),
        new(
            "Cavendish Extinct",
            true,
            static (rolls, with) => new CavendishExtinctClause { Rolls = rolls, With = with }
        ),
        new(
            "Space Levelup",
            true,
            static (rolls, with) => new SpaceLevelupClause { Rolls = rolls, With = with }
        ),
        new(
            "Glass Destroy",
            true,
            static (rolls, with) => new GlassDestroyClause { Rolls = rolls, With = with }
        ),
        new(
            "Wheel Stays Flipped",
            true,
            static (rolls, with) => new WheelStaysFlippedClause { Rolls = rolls, With = with }
        ),
        new(
            "Business Payout",
            false,
            static (rolls, _) => new BusinessPayoutClause { Rolls = rolls }
        ),
        new(
            "Bloodstone Trigger",
            false,
            static (rolls, _) => new BloodstoneTriggerClause { Rolls = rolls }
        ),
        new(
            "Parking Payout",
            false,
            static (rolls, _) => new ParkingPayoutClause { Rolls = rolls }
        ),
    ];

    private readonly record struct EventSpec(
        string Name,
        bool AllowLuck,
        Func<int[], JamlWith, IJamlClause> Create
    );

    private static bool TryParseEventPayload(string text, string name, out string payload)
    {
        payload = "";
        if (!text.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (text.Length > name.Length && text[name.Length] != ' ')
            return false;
        payload = text[name.Length..].TrimStart();
        return true;
    }

    private static (string Text, MotelyLuck? Luck, string? Error) SplitLuckTail(
        string text,
        bool allowLuck
    )
    {
        const string marker = " with luck ";
        int idx = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return (text, null, null);
        if (!allowLuck)
            return (text, null, "This event does not support luck.");
        var luckText = text[(idx + marker.Length)..].Trim();
        if (!int.TryParse(luckText, out var numeric) || !TryParseLuck(numeric, out var luck))
            return (text, null, $"Bad luck multiplier '{luckText}'.");
        return (text[..idx].TrimEnd(), luck, null);
    }

    private static (string Text, int Value, string? Error) SplitRequiredIntTail(
        string text,
        string marker
    )
    {
        int idx = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return (text, 0, $"Missing required '{marker.Trim()}' tail.");
        var valueText = text[(idx + marker.Length)..].Trim();
        if (!int.TryParse(valueText, out var value))
            return (text, 0, $"Bad integer '{valueText}' in '{text}'.");
        return (text[..idx].TrimEnd(), value, null);
    }

    private static (int[] Rolls, string? Error) ParseRequiredRolls(string text, string name)
    {
        var (head, rolls, error) = SplitRollsTail(text);
        if (error != null)
            return ([], error);
        if (!string.IsNullOrWhiteSpace(head))
            return ([], $"Unexpected text after '{name}': '{head}'.");
        if (rolls.Length == 0)
            return ([], $"{name} requires a rolls tail.");
        return (rolls, null);
    }

    private static bool TryParseLuck(int value, out MotelyLuck luck)
    {
        foreach (var candidate in Enum.GetValues<MotelyLuck>())
        {
            if ((int)candidate == value)
            {
                luck = candidate;
                return true;
            }
        }
        luck = default;
        return false;
    }

    private static bool TryParsePrefixed(string text, string prefix, out string rest)
    {
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rest = text[prefix.Length..].Trim();
            return true;
        }
        rest = "";
        return false;
    }

    private static bool TryParseFormattedEnum<T>(string text, Func<T, string> format, out T value)
        where T : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (
                string.Equals(format(candidate), text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase)
            )
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryParseSpecific<T>(MotelyItem item, out T value)
        where T : struct, Enum =>
        Enum.TryParse(item.Type.ToString(), ignoreCase: false, out value) && Enum.IsDefined(value);

    private static readonly Dictionary<MotelyItemType, MotelyJoker> JokerByType =
        Enum.GetValues<MotelyJoker>().ToDictionary(j => new MotelyItem(j).Type);

    private static bool TryExtractJoker(MotelyItem item, out MotelyJoker joker) =>
        JokerByType.TryGetValue(item.Type, out joker);

    private static MotelyJokerSticker[] StickersOf(MotelyItem item)
    {
        var stickers = new List<MotelyJokerSticker>(3);
        if (item.IsEternal)
            stickers.Add(MotelyJokerSticker.Eternal);
        if (item.IsPerishable)
            stickers.Add(MotelyJokerSticker.Perishable);
        if (item.IsRental)
            stickers.Add(MotelyJokerSticker.Rental);
        return [.. stickers];
    }

    private static (string Head, int[] Rolls, string? Error) SplitRollsTail(string text)
    {
        const string marker = "rolls ";
        int idx = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || (idx > 0 && !char.IsWhiteSpace(text[idx - 1])))
            return (text.Trim(), [], null);
        var head = text[..idx].TrimEnd();
        var tail = text[(idx + marker.Length)..];
        var (values, error) = ParseNumberList(tail, text);
        return (head, values, error);
    }

    // A trailing "score N" (N may be negative — a penalty). Absent → (text, null, null), so a line
    // with no score just parses normally and the clause takes its default. This is what lets a whole
    // should clause live on one forgiving line instead of fragile multi-line YAML.
    private static (string Text, int? Score, string? Error) SplitScoreTail(string text)
    {
        const string marker = " score ";
        int idx = text.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return (text, null, null);
        var scoreText = text[(idx + marker.Length)..].Trim();
        if (!int.TryParse(scoreText, out var value))
            return (text, null, $"Bad score '{scoreText}' in '{text}'.");
        return (text[..idx].TrimEnd(), value, null);
    }

    private static (string Head, int[] Antes, string? Error) SplitAnteTail(string line)
    {
        int idx = line.IndexOf(" in ante", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return (line, [], null);

        var head = line[..idx].TrimEnd();
        var tail = line[(idx + " in ante".Length)..];
        if (tail.StartsWith("s", StringComparison.OrdinalIgnoreCase))
            tail = tail[1..];

        var (antes, error) = ParseNumberList(tail, line);
        return (head, antes, error);
    }

    private static (int[] Values, string? Error) ParseNumberList(string text, string fullLine)
    {
        var tokens = text.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var values = new List<int>(tokens.Length);
        // A "to"/"through"/"thru" word means the NEXT number closes an inclusive range with the
        // previous one — "1 to 8" is 1..8, not the two numbers 1 and 8. "or"/"and" are plain list
        // separators (skipped). This is the difference between a range and a list, in human words.
        bool rangePending = false;
        foreach (var token in tokens)
        {
            if (token.Equals("or", StringComparison.OrdinalIgnoreCase)
                || token.Equals("and", StringComparison.OrdinalIgnoreCase))
            {
                rangePending = false;
                continue;
            }
            if (token.Equals("to", StringComparison.OrdinalIgnoreCase)
                || token.Equals("through", StringComparison.OrdinalIgnoreCase)
                || token.Equals("thru", StringComparison.OrdinalIgnoreCase))
            {
                rangePending = true;
                continue;
            }

            // A range token — "1-8", "1..8", "3–6" (en dash) — expands inclusively, ascending or
            // descending. This is the shorthand a real person reaches for; the parser meets it.
            if (TrySplitRange(token, out int lo, out int hi))
            {
                AppendRange(values, lo, hi);
                rangePending = false;
                continue;
            }

            if (!int.TryParse(token, out var value))
                return ([], $"Bad numeric token '{token}' in '{fullLine}'.");

            // "N to M": expand from the number we already added up to this one (exclusive of the
            // start, which is already in the list), so "1 to 8" reads as 1..8 in order.
            if (rangePending && values.Count > 0)
            {
                int start = values[^1];
                if (value >= start)
                    for (int n = start + 1; n <= value; n++) values.Add(n);
                else
                    for (int n = start - 1; n >= value; n--) values.Add(n);
                rangePending = false;
                continue;
            }

            values.Add(value);
        }
        return ([.. values], null);
    }

    private static void AppendRange(List<int> values, int lo, int hi)
    {
        if (lo <= hi)
            for (int n = lo; n <= hi; n++) values.Add(n);
        else
            for (int n = lo; n >= hi; n--) values.Add(n);
    }

    // Recognizes "A-B", "A..B", or "A<en-dash>B" as an inclusive integer range. Returns false for a
    // plain number (which the caller parses directly) or anything that isn't two integers around a
    // single separator, so genuinely malformed tokens still surface as errors.
    private static bool TrySplitRange(string token, out int lo, out int hi)
    {
        lo = 0;
        hi = 0;
        string[]? parts =
            token.Contains("..", StringComparison.Ordinal) ? token.Split("..", StringSplitOptions.RemoveEmptyEntries)
            : token.Contains('–') ? token.Split('–')          // en dash
            : token.Contains('-') ? token.Split('-')
            : null;
        return parts is { Length: 2 }
            && int.TryParse(parts[0].Trim(), out lo)
            && int.TryParse(parts[1].Trim(), out hi);
    }
}
