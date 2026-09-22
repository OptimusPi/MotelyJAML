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

    public static JamlLoaderValueReader FromScalar(string? text, JamlSpan span = default) =>
        new(text ?? "", null, false, span);

    public static JamlLoaderValueReader FromStrings(string[]? values, JamlSpan span = default)
    {
        if (values is null || values.Length == 0)
            return new("", null, false, span);
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

    public bool TryBool(out bool value)
    {
        if (bool.TryParse(_text, out value))
            return true;
        if (string.Equals(_text, "yes", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (string.Equals(_text, "no", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    public bool TryIntArray(out int[] value)
    {
        if (string.IsNullOrWhiteSpace(_text) && !(_hasArray && _array is { Length: > 0 }))
        {
            value = [];
            return false;
        }

        if (int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var one)
            && !(_hasArray && _array is { Length: > 1 }))
        {
            value = [one];
            return true;
        }

        if (_hasArray && _array is not null)
        {
            var list = new int[_array.Length];
            for (int i = 0; i < _array.Length; i++)
            {
                if (!int.TryParse(_array[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out list[i]))
                    throw new JamlSemanticException($"Cannot parse '{_array[i]}' as int.", Span);
            }
            value = list;
            return true;
        }

        value = [];
        return false;
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
