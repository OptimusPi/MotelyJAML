using Motely.SeedProviders;

namespace Motely.CLI;

/// <summary>
/// Parses the CLI <c>--aesthetic</c> string into a <see cref="MotelyAesthetic"/>.
/// CLI-only input concern: the engine consumes the enum, never the spelling.
/// <c>all</c> is a CLI meta-token (every family concatenated) — not a schema enum value.
/// </summary>
public static class MotelyAestheticParser
{
    /// <summary>
    /// Canonical JAML spellings (lowercase). Single source for <see cref="TryParse"/> and JSON schema (<c>definitions/MotelyAesthetic</c>).
    /// </summary>
    private static readonly (string Jaml, MotelyAesthetic Value)[] Known =
    [
        ("balatro", MotelyAesthetic.Balatro),
        ("gross", MotelyAesthetic.Gross),
        ("funny", MotelyAesthetic.Funny),
        ("nsfw", MotelyAesthetic.Nsfw),
        ("mirror", MotelyAesthetic.Mirror),
        ("repeater", MotelyAesthetic.Repeater),
        ("runs", MotelyAesthetic.Runs),
        ("palindrome", MotelyAesthetic.Palindrome),
        ("step", MotelyAesthetic.Step),
        ("leet", MotelyAesthetic.Leet),
        ("psychosis", MotelyAesthetic.Psychosis),
    ];

    /// <summary>CLI meta-token: run every aesthetic family (not a <see cref="MotelyAesthetic"/> value).</summary>
    public const string AllToken = "all";

    /// <summary>Strings for <c>enum</c> in the generated schema; order matches <see cref="Known"/> (no <c>all</c>).</summary>
    public static string[] KnownJamlStringsForSchema() => [.. Known.Select(static e => e.Jaml)];

    /// <summary>Comma-separated list for load errors / help (includes <c>all</c>).</summary>
    public static string KnownJamlStringsDescription() =>
        string.Join(", ", Known.Select(static e => e.Jaml)) + ", " + AllToken;

    /// <summary>Every aesthetic family in canonical order (for <c>--aesthetic all</c> concat providers).</summary>
    public static MotelyAesthetic[] AllAesthetics() => [.. Known.Select(static e => e.Value)];

    public static bool IsAllToken(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && raw.Trim().Equals(AllToken, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string raw, out MotelyAesthetic value)
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
