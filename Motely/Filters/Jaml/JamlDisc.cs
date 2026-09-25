namespace Motely.Filters.Jaml;

public static class JamlDisc
{
    public static T[] OrEmpty<T>(T[]? values) => values ?? [];

    public static bool IsCategoryAny<T>(T[]? values) => OrEmpty(values).Length == 0;

    public static bool IsAnyToken(string? text) =>
        string.Equals(text?.Trim(), "any", StringComparison.OrdinalIgnoreCase);
}

public static class MapFeatureRolls
{
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
