using Motely.SeedProviders;

namespace Motely.CLI;

public static class JamlAestheticParser
{
    private static readonly (string Jaml, JamlAesthetic Value)[] Known =
    [
        ("balatro", JamlAesthetic.Balatro),
        ("gross", JamlAesthetic.Gross),
        ("funny", JamlAesthetic.Funny),
        ("nsfw", JamlAesthetic.Nsfw),
        ("mirror", JamlAesthetic.Mirror),
        ("repeater", JamlAesthetic.Repeater),
        ("runs", JamlAesthetic.Runs),
        ("palindrome", JamlAesthetic.Palindrome),
        ("step", JamlAesthetic.Step),
        ("leet", JamlAesthetic.Leet),
        ("psychosis", JamlAesthetic.Psychosis),
    ];

    public const string AllToken = "all";

    public static string[] KnownJamlStringsForSchema() => [.. Known.Select(static e => e.Jaml)];

    public static string KnownJamlStringsDescription() =>
        string.Join(", ", Known.Select(static e => e.Jaml)) + ", " + AllToken;

    public static JamlAesthetic[] AllAesthetics() => [.. Known.Select(static e => e.Value)];

    public static bool IsAllToken(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && raw.Trim().Equals(AllToken, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string raw, out JamlAesthetic value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var key = raw.Trim().ToLowerInvariant();
        if (key == AllToken)
            return false;

        foreach (var (jaml, v) in Known)
        {
            if (key == jaml)
            {
                value = v;
                return true;
            }
        }

        return false;
    }
}
