namespace Motely.Filters.Jaml;

/// <summary>
/// Shared enum-parse failure text. Editors show this in a Problems panel — list a sample of
/// known names, not the entire enum (jokers alone are 150+).
/// </summary>
internal static class JamlEnumMessages
{
    public const int MaxKnownNames = 12;

    public static string CannotParse(string raw, Type enumType)
    {
        var names = Enum.GetNames(enumType);
        if (names.Length == 0)
            return $"Cannot parse '{raw}' as {enumType.Name}.";

        var shown = names.Length <= MaxKnownNames
            ? string.Join(", ", names)
            : string.Join(", ", names.AsSpan(0, MaxKnownNames).ToArray())
              + $", … +{names.Length - MaxKnownNames} more";

        return $"Cannot parse '{raw}' as {enumType.Name}. Known values: {shown}.";
    }
}
