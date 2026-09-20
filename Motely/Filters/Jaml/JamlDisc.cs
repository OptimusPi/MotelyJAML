namespace Motely.Filters.Jaml;

/// <summary>
/// The one convention every clause value list shares: an empty list means the whole category
/// ("Any"), not "nothing". Filters ask these two questions instead of null-checking by hand.
/// </summary>
public static class JamlDisc
{
    /// <summary>The list as written, with null read as empty.</summary>
    public static T[] OrEmpty<T>(T[]? values) => values ?? [];

    /// <summary>True when the clause names no specific values — the whole category matches.</summary>
    public static bool IsCategoryAny<T>(T[]? values) => OrEmpty(values).Length == 0;

    /// <summary>The whole-category token as spelled in a document.</summary>
    public static bool IsAnyToken(string? text) =>
        string.Equals(text?.Trim(), "any", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Roll-index helpers for ante features whose clause carries <c>rolls</c>.</summary>
public static class MapFeatureRolls
{
    /// <summary>The highest roll index the clause asks for, or -1 when it asks for none.</summary>
    public static int MaxRollIndex(int[]? rolls)
    {
        var values = JamlDisc.OrEmpty(rolls);
        int max = -1;
        foreach (int roll in values)
            if (roll > max)
                max = roll;
        return max;
    }
}
