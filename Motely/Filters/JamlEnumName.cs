using System.Collections.Concurrent;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary>
/// A JAML enum value is a name, matched case-insensitively. <c>Enum.Parse</c> on its own also reads
/// a number as the enum's value and <c>A, B</c> as <c>A | B</c>, so <c>erraticRank: 2</c> bound
/// Four (Two is 0) and <c>rank: "10"</c> bound Queen, with no error. Those forms throw here.
/// </summary>
internal static class JamlEnumName
{
    /// <summary>
    /// Enums whose value is what the number says: <see cref="MotelyLuck"/>'s value is the
    /// multiplier (<c>X4 = 4</c>), so <c>luck: 4</c> is X4. The number must be one of its values.
    /// </summary>
    public static bool NumberIsValue(Type enumType) => enumType == typeof(MotelyLuck);

    public static T Parse<T>(string text) where T : struct, Enum => (T)Parse(typeof(T), text);

    public static object Parse(Type enumType, string text)
    {
        var trimmed = text.AsSpan().Trim();
        if (trimmed.Length > 0 && (char.IsAsciiDigit(trimmed[0]) || trimmed[0] is '-' or '+'))
        {
            if (NumberIsValue(enumType)
                && int.TryParse(trimmed, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int number)
                && Enum.IsDefined(enumType, number))
                return Enum.ToObject(enumType, number);
            throw new FormatException(NumberIsValue(enumType)
                ? $"'{text}' is not a value: write one of {string.Join(", ", Enum.GetNames(enumType))}, or its number."
                : $"'{text}' is not a name: write {Names(enumType)}.");
        }
        if (trimmed.Contains(','))
            throw new FormatException($"'{text}' holds more than one name: a list goes in brackets, [{text}].");
        if (Enum.TryParse(enumType, text, ignoreCase: true, out var exact))
            return exact;
        if (Tolerant(enumType, text) is { } name)
            return Enum.Parse(enumType, name);
        throw new FormatException(NotANameMessage(enumType, text.Trim()));
    }

    /// <summary>Per enum: normalized name (letters and digits, lower case) → the names spelled that way.</summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, string[]>> NormalizedNames = new();

    /// <summary>
    /// Short names that mean one value and nothing else: a card rank written as on the card.
    /// (Numbers stay refused: <c>Enum.Parse</c> read <c>2</c> as Four.)
    /// </summary>
    private static readonly Dictionary<string, string> RankLetters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["J"] = nameof(MotelyStandardcardRank.Jack),
        ["Q"] = nameof(MotelyStandardcardRank.Queen),
        ["K"] = nameof(MotelyStandardcardRank.King),
        ["A"] = nameof(MotelyStandardcardRank.Ace),
    };

    /// <summary>
    /// The one name <paramref name="text"/> means when spelled loosely — any case, spaces or
    /// punctuation (<c>Reroll Surplus</c>, <c>the_hook</c>), with or without a leading "The"
    /// (<c>The Luchador</c>, <c>Fool</c>) — or null when no name, or more than one, fits.
    /// </summary>
    internal static string? Tolerant(Type enumType, string text)
    {
        if (enumType == typeof(MotelyStandardcardRank) && RankLetters.TryGetValue(text.Trim(), out var rank))
            return rank;

        var names = NormalizedNames.GetOrAdd(enumType, static t =>
            Enum.GetNames(t)
                .GroupBy(JamlKeys.Normalize, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal));
        var key = JamlKeys.Normalize(text);
        if (key.Length == 0)
            return null;
        if (names.TryGetValue(key, out var found))
            return found.Length == 1 ? found[0] : null;
        var other = key.StartsWith("the", StringComparison.Ordinal) ? key[3..] : "the" + key;
        return names.TryGetValue(other, out found) && found.Length == 1 ? found[0] : null;
    }

    /// <summary>
    /// Why <paramref name="text"/> is no <paramref name="enumType"/> name, in one line that names the
    /// fix: the clause key it belongs under when it names something else (Certificate is a joker,
    /// Showman an uncommon joker), else the closest names.
    /// </summary>
    private static string NotANameMessage(Type enumType, string text)
    {
        var kind = Kind(enumType);
        if (JamlShorthand.Lookup(text) is { Length: 1 } entries && entries[0] is var entry && JamlShorthand.Kind(entry) is var (otherKind, keys) && otherKind != kind)
        {
            var article = Article(otherKind);
            var write = JamlKeys.JoinOr(keys.Select(k => $"`{k}: {entry.Name}`").ToList());
            return $"'{text}' is {article} {otherKind}, not {Article(kind)} {kind}: write {write}.";
        }

        var closest = JamlKeys.Closest(text, Enum.GetNames(enumType));
        return closest.Count > 0
            ? $"'{text}' is not {Article(kind)} {kind} name; did you mean {JamlKeys.JoinOr(closest)}?"
            : $"'{text}' is not {Article(kind)} {kind} name: write {Names(enumType)}.";
    }

    /// <summary>What an enum names, in the words the clause-key messages use.</summary>
    internal static string Kind(Type enumType) =>
        enumType.Name switch
        {
            nameof(MotelyJoker) => "joker",
            nameof(MotelyJokerCommon) => "common joker",
            nameof(MotelyJokerUncommon) => "uncommon joker",
            nameof(MotelyJokerRare) => "rare joker",
            nameof(MotelyJokerLegendary) => "legendary joker",
            nameof(MotelyBossBlind) => "boss",
            nameof(MotelyVoucher) => "voucher",
            nameof(MotelyTag) => "tag",
            nameof(MotelyTarotCard) => "tarot card",
            nameof(MotelyPlanetCard) => "planet card",
            nameof(MotelySpectralCard) => "spectral card",
            nameof(MotelyStandardCard) => "standard card",
            nameof(MotelyStandardcardRank) => "rank",
            nameof(MotelyStandardcardSuit) => "suit",
            nameof(MotelyItemEdition) => "edition",
            nameof(MotelyItemEnhancement) => "enhancement",
            nameof(MotelyItemSeal) => "seal",
            nameof(MotelyBoosterPack) => "booster pack",
            nameof(MotelyPokerHand) => "poker hand",
            nameof(MotelyDeck) => "deck",
            nameof(MotelyStake) => "stake",
            _ => enumType.Name.StartsWith("Motely", StringComparison.Ordinal) ? enumType.Name["Motely".Length..] : enumType.Name,
        };

    private static string Article(string word) => word.Length > 0 && "aeiouAEIOU".Contains(word[0]) ? "an" : "a";

    private static string Names(Type enumType)
    {
        var names = Enum.GetNames(enumType);
        return names.Length <= 16
            ? "one of " + string.Join(", ", names)
            : "a name such as " + string.Join(", ", names.Take(3));
    }
}

/// <summary>
/// Every enum-typed scalar — <c>deck</c>, <c>edition</c>, <c>erraticRank</c>, a <c>stickers</c>
/// entry — binds through <see cref="JamlEnumName"/>. YAML's null forms fall through to the
/// deserializer below this one, as before.
/// </summary>
internal sealed class JamlEnumNodeDeserializer : INodeDeserializer
{
    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer
    )
    {
        value = null;
        var enumType = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        if (!enumType.IsEnum || reader.Current is not Scalar scalar || IsNull(scalar))
            return false;

        reader.Consume<Scalar>();
        value = JamlEnumName.Parse(enumType, scalar.Value);
        return true;
    }

    private static bool IsNull(Scalar scalar) =>
        scalar.Tag == "tag:yaml.org,2002:null"
        || scalar.Style == ScalarStyle.Plain && scalar.Value is "" or "~" or "null" or "Null" or "NULL";
}
