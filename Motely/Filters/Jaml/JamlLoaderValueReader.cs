using System.Globalization;

namespace Motely.Filters.Jaml;

/// <summary>
/// <see cref="IJamlValueReader"/> for the JAML loader path.
/// Parse failures throw (same contract as <see cref="JamlConfigLoader.FromJaml"/>);
/// <c>false</c> means the value is absent, not invalid.
/// </summary>
internal sealed class JamlLoaderValueReader : IJamlValueReader
{
    private readonly string _text;
    private readonly string[]? _array;
    private readonly bool _hasArray;

    private JamlLoaderValueReader(string text, string[]? array, bool hasArray, JamlSpan span)
    {
        _text = text;
        _array = array;
        _hasArray = hasArray;
        Span = span;
    }

    public string Text => _text;
    public JamlSpan Span { get; }

    public bool IsAny => JamlDisc.IsAnyToken(_text);

    private bool IsEmptyList => _hasArray && _array is { Length: 0 };

    private void RejectEmptyList()
    {
        if (IsEmptyList)
            throw new JamlSemanticException(
                "'[]' is not a value. Write Any, or leave it blank, for the whole category.",
                Span
            );
    }

    public static JamlLoaderValueReader FromScalar(string? text, JamlSpan span = default) =>
        new(text ?? "", null, false, span);

    public static JamlLoaderValueReader FromStrings(string[]? values, JamlSpan span = default)
    {
        if (values is null)
            return new("", null, false, span);
        if (values.Length == 0)
            return new("", values, true, span);
        if (values.Length == 1)
            return new(values[0], values, true, span);
        return new(string.Join(", ", values), values, true, span);
    }

    public bool TryInt(out int value)
    {
        if (int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;
        value = 0;
        return false;
    }

    public bool TryBool(out bool value) => TryParseBool(_text, out value);

    /// <summary>
    /// The one bool spelling JAML accepts, shared with the block loader's <c>GetBool</c> so a
    /// flag reads the same whether it sits on a clause key or inside a <c>sources:</c> block.
    /// </summary>
    internal static bool TryParseBool(string text, out bool value)
    {
        if (bool.TryParse(text, out value))
            return true;
        if (string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    public bool TryIntArray(out int[] value)
    {
        RejectEmptyList();
        if (string.IsNullOrWhiteSpace(_text) && !(_hasArray && _array is { Length: > 0 }))
        {
            value = [];
            return false;
        }

        string[] tokens = _hasArray && _array is { Length: > 0 } ? _array : [_text];
        var list = new List<int>(tokens.Length);
        foreach (var token in tokens)
        {
            if (JamlLine.TrySplitRange(token, out int lo, out int hi))
                JamlLine.AppendRange(list, lo, hi);
            else if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var one))
                list.Add(one);
            else
                throw new JamlSemanticException($"Cannot parse '{token}' as an int or range.", Span);
        }
        value = [.. list];
        return true;
    }

    public bool TryEnum<TEnum>(out TEnum value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(_text))
        {
            value = default;
            return false;
        }

        if (typeof(TEnum) == typeof(MotelyStandardcardRank))
        {
            value = (TEnum)(object)JamlConfigLoader.ParseRank(_text, Span);
            return true;
        }

        value = JamlConfigLoader.ParseEnum<TEnum>(_text, Span);
        return true;
    }

    public bool TryRank(out MotelyStandardcardRank value)
    {
        if (string.IsNullOrWhiteSpace(_text))
        {
            value = default;
            return false;
        }

        value = JamlConfigLoader.ParseRank(_text, Span);
        return true;
    }

    public bool TryEnumArray<TEnum>(out TEnum[] value) where TEnum : struct, Enum
    {
        RejectEmptyList();

        var parts = _hasArray && _array is { Length: > 0 }
            ? _array
            : (JamlDisc.IsAnyToken(_text) ? null : new[] { _text });
        if (parts is null || parts.Length == 0)
        {
            value = [];
            return true;
        }

        if (parts.Length == 1 && JamlDisc.IsAnyToken(parts[0]))
        {
            value = [];
            return true;
        }

        value = new TEnum[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (JamlDisc.IsAnyToken(parts[i]))
                throw new JamlSemanticException(
                    "'Any' is the whole category, not a list member.",
                    Span
                );
            if (typeof(TEnum) == typeof(MotelyStandardcardRank))
                value[i] = (TEnum)(object)JamlConfigLoader.ParseRank(parts[i], Span);
            else
                value[i] = JamlConfigLoader.ParseEnum<TEnum>(parts[i], Span);
        }
        return true;
    }
}
