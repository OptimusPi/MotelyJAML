using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

/// <summary>
/// JAML-layer category-any. Motely enums stay Motely — they do not grow an Any member.
/// On the wire: blank (<c>joker:</c>) or the keyword <c>Any</c>. Engine side: empty array.
/// </summary>
internal static class JamlDisc
{
    public static bool IsAnyToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || string.Equals(value.Trim(), "any", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the disc list is absent or empty — match the whole category.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCategoryAny<T>(T[]? items) => items is not { Length: > 0 };

    /// <summary>Never null — loader/host may leave the property unset.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T[] OrEmpty<T>(T[]? items) => items ?? [];
}
