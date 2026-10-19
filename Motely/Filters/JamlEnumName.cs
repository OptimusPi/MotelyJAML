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
        return Enum.Parse(enumType, text, ignoreCase: true);
    }

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
