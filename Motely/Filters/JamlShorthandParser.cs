using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using Motely.Enums;

namespace Motely.Filters;

/// <summary>
/// Parses JAML shorthand strings like "Perkeo in Ante 1 or 2" into <see cref="IMotelyClause"/> objects.
/// </summary>
/// <remarks>
/// Shorthand grammar (informal):
///   line        := [sticker] name ["in" ante-spec] | ["The"] name ["boss"]
///   sticker     := "Eternal" | "Perishable" | "Rental"
///   ante-spec   := "Ante" int | "Antes" int-list
///   int-list    := int (("," | "or" | "and") int)*
///   range       := "[" int ".." int "]" | "[" int ".." "]"
///
/// Name resolution is case-insensitive; output uses canonical PascalCase.
/// </remarks>
public static partial class JamlShorthandParser
{
    // Regex to tokenize: words, numbers, brackets, punctuation
    [GeneratedRegex(@"\[|\]|\.{2,}|\w+|\d+", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    private static readonly HashSet<string> KnownStickers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Eternal", "Perishable", "Rental"
    };

    private static readonly HashSet<string> AnteConnectors = new(StringComparer.OrdinalIgnoreCase)
    {
        ",", "or", "and"
    };

    // Name → (clauseType, enumValue) lookup tables, built once
    private static readonly Dictionary<string, (Type ClauseType, string EnumValue, string FamilyKey)> NameRegistry;

    static JamlShorthandParser()
    {
        NameRegistry = new Dictionary<string, (Type, string, string)>(StringComparer.OrdinalIgnoreCase);

        // Legendary jokers
        RegisterNames<LegendaryJokerClause, MotelyJoker>(
            "legendaryJoker",
            [MotelyJoker.Canio, MotelyJoker.Triboulet, MotelyJoker.Yorick, MotelyJoker.Chicot, MotelyJoker.Perkeo]);

        // All jokers (includes legendary for disambiguation warnings)
        RegisterNames<JokerClause, MotelyJoker>("joker",
            Enum.GetValues<MotelyJoker>());

        // Bosses
        RegisterNames<BossClause, MotelyBossBlind>("boss",
            Enum.GetValues<MotelyBossBlind>());

        // Vouchers
        RegisterNames<VoucherClause, MotelyVoucher>("voucher",
            Enum.GetValues<MotelyVoucher>());

        // Tarot cards
        RegisterNames<TarotCardClause, MotelyTarotCard>("tarotCard",
            Enum.GetValues<MotelyTarotCard>());

        // Spectral cards
        RegisterNames<SpectralCardClause, MotelySpectralCard>("spectralCard",
            Enum.GetValues<MotelySpectralCard>());

        // Planet cards
        RegisterNames<PlanetCardClause, MotelyPlanetCard>("planetCard",
            Enum.GetValues<MotelyPlanetCard>());

        // Tags
        RegisterNames<TagClause, MotelyTag>("tag",
            Enum.GetValues<MotelyTag>());
    }

    private static void RegisterNames<TClause, TEnum>(string familyKey, IEnumerable<TEnum> values)
        where TClause : IMotelyClause
        where TEnum : struct, Enum
    {
        foreach (var val in values)
        {
            var name = val.ToString();
            if (!NameRegistry.TryAdd(name, (typeof(TClause), name, familyKey)))
            {
                // Ambiguity: same name exists in two families.
                // We keep the first registration and let resolution handle it.
            }
        }
    }

    /// <summary>
    /// Attempts to parse a shorthand string into an <see cref="IMotelyClause"/>.
    /// </summary>
    /// <param name="input">The raw shorthand line (e.g. "Perkeo in Ante 1 or 2").</param>
    /// <param name="clause">The resulting clause, or null on failure.</param>
    /// <param name="error">A human-readable error message on failure.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string input, out IMotelyClause? clause, out string? error)
    {
        clause = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Shorthand clause is empty.";
            return false;
        }

        var tokens = Tokenize(input);
        var pos = 0;

        // 1. Optional sticker
        MotelyJokerSticker? sticker = null;
        if (pos < tokens.Count && KnownStickers.Contains(tokens[pos]))
        {
            sticker = Enum.Parse<MotelyJokerSticker>(tokens[pos], ignoreCase: true);
            pos++;
        }

        // 2. Optional "The" prefix (for bosses, tarots, etc.)
        bool hasThePrefix = false;
        if (pos < tokens.Count && tokens[pos].Equals("The", StringComparison.OrdinalIgnoreCase))
        {
            hasThePrefix = true;
            pos++;
        }

        // 3. Item name: collect consecutive word/number tokens until we hit "in", "boss", or end
        var nameParts = new List<string>();
        while (pos < tokens.Count)
        {
            var tok = tokens[pos];
            if (tok.Equals("in", StringComparison.OrdinalIgnoreCase)
                || tok.Equals("boss", StringComparison.OrdinalIgnoreCase)
                || tok.Equals("ante", StringComparison.OrdinalIgnoreCase)
                || tok.Equals("antes", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            nameParts.Add(tok);
            pos++;
        }

        if (nameParts.Count == 0)
        {
            error = $"No item name found in shorthand: '{input}'";
            return false;
        }

        var name = string.Concat(nameParts);

        // 4. Optional "boss" suffix
        bool hasBossSuffix = false;
        if (pos < tokens.Count && tokens[pos].Equals("boss", StringComparison.OrdinalIgnoreCase))
        {
            hasBossSuffix = true;
            pos++;
        }

        // 5. Optional "in Ante[s] ..."
        int[]? antes = null;
        if (pos < tokens.Count && tokens[pos].Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            pos++; // consume "in"
            if (pos < tokens.Count
                && (tokens[pos].Equals("ante", StringComparison.OrdinalIgnoreCase)
                    || tokens[pos].Equals("antes", StringComparison.OrdinalIgnoreCase)))
            {
                pos++; // consume "ante" / "antes"
                if (!TryParseAnteList(tokens, ref pos, out antes, out error))
                {
                    error = $"Invalid ante list in shorthand '{input}': {error}";
                    return false;
                }
            }
        }
        else if (pos < tokens.Count && tokens[pos].Equals("ante", StringComparison.OrdinalIgnoreCase))
        {
            // Handle "Perkeo Ante 1" (without "in")
            pos++;
            if (!TryParseAnteList(tokens, ref pos, out antes, out error))
            {
                error = $"Invalid ante list in shorthand '{input}': {error}";
                return false;
            }
        }

        // 6. Resolve name
        if (!TryResolveName(name, hasBossSuffix, out var resolved, out var ambiguities))
        {
            if (ambiguities?.Count > 0)
            {
                error = $"Ambiguous name '{name}' matches multiple items: {string.Join(", ", ambiguities)}. " +
                        $"Use the full clause form (e.g. 'legendaryJoker: {name}') to disambiguate.";
            }
            else
            {
                error = $"Unknown item name '{name}' in shorthand. Known names include: " +
                        $"{string.Join(", ", GetSuggestedNames(name))}";
            }
            return false;
        }

        // 7. Build clause
        clause = CreateClause(resolved.ClauseType, resolved.EnumValue, sticker, antes, resolved.FamilyKey);
        return true;
    }

    private static bool TryParseAnteList(List<string> tokens, ref int pos, out int[]? antes, out string? error)
    {
        antes = null;
        error = null;
        var values = new List<int>();

        // Check for bracketed range/list first: [1..4] or [1,2,3] or [2..]
        if (pos < tokens.Count && tokens[pos] == "[")
        {
            pos++; // consume '['
            var innerTokens = new List<string>();
            while (pos < tokens.Count && tokens[pos] != "]")
            {
                innerTokens.Add(tokens[pos]);
                pos++;
            }
            if (pos >= tokens.Count || tokens[pos] != "]")
            {
                error = "Unclosed '[' in ante list.";
                return false;
            }
            pos++; // consume ']'

            // Check for range: [1..4] or [2..]
            var dotIndex = innerTokens.IndexOf("..");
            if (dotIndex >= 0)
            {
                if (!int.TryParse(innerTokens[0], out var start))
                {
                    error = $"Invalid range start '{innerTokens[0]}'.";
                    return false;
                }
                if (dotIndex + 1 < innerTokens.Count)
                {
                    // Closed range [1..4]
                    if (!int.TryParse(innerTokens[dotIndex + 1], out var end))
                    {
                        error = $"Invalid range end '{innerTokens[dotIndex + 1]}'.";
                        return false;
                    }
                    if (start > end)
                    {
                        error = $"Range start {start} > end {end}.";
                        return false;
                    }
                    for (int i = start; i <= end; i++)
                        values.Add(i);
                }
                else
                {
                    // Open-ended range [2..] → defer to engine max (we use 99 as placeholder)
                    for (int i = start; i <= 99; i++)
                        values.Add(i);
                }
            }
            else
            {
                // Comma-separated list inside brackets
                foreach (var tok in innerTokens)
                {
                    if (tok == ",") continue;
                    if (!int.TryParse(tok, out var val))
                    {
                        error = $"Invalid ante number '{tok}' in bracketed list.";
                        return false;
                    }
                    values.Add(val);
                }
            }
        }
        else
        {
            // Bare list: 1, 2, 3 or 1 or 2 or 3 or 1 and 2
            while (pos < tokens.Count)
            {
                if (int.TryParse(tokens[pos], out var val))
                {
                    values.Add(val);
                    pos++;
                }
                else if (AnteConnectors.Contains(tokens[pos]))
                {
                    pos++; // skip connector
                }
                else
                {
                    break;
                }
            }
        }

        if (values.Count == 0)
        {
            error = "Expected ante numbers after 'Ante(s)'.";
            return false;
        }

        antes = values.Distinct().OrderBy(x => x).ToArray();
        return true;
    }

    private static bool TryResolveName(string name, bool forceBoss, out (Type ClauseType, string EnumValue, string FamilyKey) result, out List<string>? ambiguities)
    {
        result = default;
        ambiguities = null;

        // Direct lookup
        if (NameRegistry.TryGetValue(name, out var direct))
        {
            // Check for ambiguity: same name in multiple families
            ambiguities = FindAmbiguities(name);
            if (ambiguities.Count > 1 && !forceBoss)
            {
                // Only truly ambiguous if the families are different
                return false;
            }
            result = direct;
            return true;
        }

        // Fuzzy match: try with/without "The" prefix
        var candidates = NameRegistry.Keys
            .Where(k => k.Contains(name, StringComparison.OrdinalIgnoreCase)
                     || name.Contains(k, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 1)
        {
            result = NameRegistry[candidates[0]];
            return true;
        }

        return false;
    }

    private static List<string> FindAmbiguities(string name)
    {
        var matches = new List<string>();
        foreach (var kvp in NameRegistry)
        {
            if (kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add($"{kvp.Value.FamilyKey}: {kvp.Value.EnumValue}");
            }
        }
        return matches;
    }

    private static IEnumerable<string> GetSuggestedNames(string partial)
    {
        return NameRegistry.Keys
            .Where(k => k.Contains(partial, StringComparison.OrdinalIgnoreCase))
            .Take(5);
    }

    private static IMotelyClause CreateClause(Type clauseType, string enumValue, MotelyJokerSticker? sticker, int[]? antes, string familyKey)
    {
        IMotelyClause clause = familyKey switch
        {
            "legendaryJoker" => new LegendaryJokerClause { Joker = Enum.Parse<MotelyJoker>(enumValue) },
            "joker" => new JokerClause { Joker = Enum.Parse<MotelyJoker>(enumValue) },
            "boss" => new BossClause { Boss = Enum.Parse<MotelyBossBlind>(enumValue) },
            "voucher" => new VoucherClause { Voucher = Enum.Parse<MotelyVoucher>(enumValue) },
            "tarotCard" => new TarotCardClause { TarotCard = Enum.Parse<MotelyTarotCard>(enumValue) },
            "spectralCard" => new SpectralCardClause { SpectralCard = Enum.Parse<MotelySpectralCard>(enumValue) },
            "planetCard" => new PlanetCardClause { PlanetCard = Enum.Parse<MotelyPlanetCard>(enumValue) },
            "tag" => new TagClause { Tag = Enum.Parse<MotelyTag>(enumValue) },
            _ => throw new InvalidOperationException($"Unknown clause family: {familyKey}")
        };

        // Apply sticker if applicable
        if (sticker.HasValue && clause is IJokerClause jokerClause)
        {
            jokerClause.Stickers = [sticker.Value];
        }

        // Apply antes if applicable
        if (antes != null && clause is IAnteScopedClause anteClause)
        {
            anteClause.Antes = antes;
        }

        return clause;
    }

    private static List<string> Tokenize(string input)
    {
        var matches = TokenRegex().Matches(input);
        var tokens = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            tokens.Add(m.Value);
        }
        return tokens;
    }
}
