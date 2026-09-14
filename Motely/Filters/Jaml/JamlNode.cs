namespace Motely.Filters.Jaml;

// The document tree VYaml parse events are read into. The loader walks this; nothing else.

internal abstract class JNode
{
    public JamlSpan Span { get; set; }
}

internal sealed class JMap : JNode
{
    // Insertion order preserved; lookups are case-insensitive, matching every wire key in the
    // grammar (camelCase, but authors mistype case sometimes and the loader has always tolerated it).
    private readonly Dictionary<string, JNode> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JamlSpan> _keySpans = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    public IReadOnlyList<string> Keys => _order;

    public void Set(string key, JNode value, JamlSpan keySpan)
    {
        if (!_items.ContainsKey(key))
            _order.Add(key);
        _items[key] = value;
        _keySpans[key] = keySpan;
    }

    public JNode? Get(string key) => _items.TryGetValue(key, out var v) ? v : null;

    /// <summary>The span of the key itself as written, or an empty span when none was stamped.</summary>
    public JamlSpan KeySpan(string key) => _keySpans.TryGetValue(key, out var s) ? s : default;

    /// <summary>The value node's source span when one was stamped; empty otherwise.</summary>
    public JamlSpan ValueSpan(string key) =>
        Get(key) is { Span: { IsEmpty: false } span } ? span : default;
}

internal sealed class JSeq : JNode
{
    public List<JNode> Items { get; } = [];
}

internal enum JScalarKind { Bare, Quoted, Integer }

internal sealed class JScalar(string value, JScalarKind kind = JScalarKind.Bare) : JNode
{
    public string Value { get; } = value;
    public JScalarKind Kind { get; } = kind;

    /// <summary>YAML null (<c>null</c>, <c>~</c>, a blank value). Reads as not written.</summary>
    public bool IsNull { get; init; }
}

/// <summary>JAML's one integer-range grammar: <c>A-B</c>, <c>A..B</c>, <c>A–B</c>, inclusive, either direction.</summary>
internal static class JamlIntRange
{
    public static bool TrySplit(string token, out int lo, out int hi)
    {
        lo = 0;
        hi = 0;
        string[]? parts =
            token.Contains("..", StringComparison.Ordinal) ? token.Split("..", StringSplitOptions.RemoveEmptyEntries)
            : token.Contains('–') ? token.Split('–')
            : token.Contains('-') ? token.Split('-')
            : null;
        return parts is { Length: 2 }
            && int.TryParse(parts[0].Trim(), out lo)
            && int.TryParse(parts[1].Trim(), out hi);
    }

    public static void Append(List<int> values, int lo, int hi)
    {
        if (lo <= hi)
            for (int n = lo; n <= hi; n++) values.Add(n);
        else
            for (int n = lo; n >= hi; n--) values.Add(n);
    }
}
