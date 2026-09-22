namespace Motely.Filters.Jaml;

internal static class MapFeatureRolls
{
    internal static int MaxRollIndex(ReadOnlySpan<int> rolls)
    {
        int max = 0;
        for (int i = 0; i < rolls.Length; i++)
        {
            if (rolls[i] > max)
                max = rolls[i];
        }

        return max;
    }
}
