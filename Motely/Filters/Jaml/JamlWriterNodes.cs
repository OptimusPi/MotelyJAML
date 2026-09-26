namespace Motely.Filters.Jaml;

// JAML document tree. One grammar: root key/value pairs; must/should/mustNot clause lists
// (block or one-line); flow arrays; nested sources:/with:/clauses:. Parses itself.

internal abstract class JNode
{
    // Where this node sits in the source. The parser knows every position as it walks the lines;
    // keeping it here is what lets a diagnostic point at the character instead of the document.
    // Nodes the writer builds from scratch leave this default (IsEmpty) — they have no source.
    // settable so the mapping loop can stamp a value span after a multi-line collect;
    // writer-built trees leave default (IsEmpty).
    public JamlSpan Span { get; set; }
}

internal sealed class JMap : JNode
{
    // Insertion order preserved (JamlConfigWriter round-trips in a stable order); lookups are
    // case-insensitive, matching every wire key in the grammar (camelCase, but authors mistype
    // case sometimes and the loader has always tolerated it).
    private readonly Dictionary<string, JNode> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JamlSpan> _keySpans = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    public IReadOnlyList<string> Keys => _order;

    // The key's own span is required, and tracked separately from the value's: "unknown key
    // 'jokerz'" has to underline jokerz, while the value node's span points at whatever came
    // after the colon. No convenience overload that defaults it — a document where some keys
    // know where they are and some don't is worse than one where none do, because nothing tells
    // you which is which.
    public void Set(string key, JNode value, JamlSpan keySpan)
    {
        if (!_items.ContainsKey(key))
            _order.Add(key);
        _items[key] = value;
        _keySpans[key] = keySpan;
    }

    public JNode? Get(string key) => _items.TryGetValue(key, out var v) ? v : null;

    /// <summary>The span of the key itself as written, or an empty span for a synthesized node.</summary>
    public JamlSpan KeySpan(string key) => _keySpans.TryGetValue(key, out var s) ? s : default;

    /// <summary>The value node's source span when the parser stamped one; empty otherwise.</summary>
    public JamlSpan ValueSpan(string key) =>
        Get(key) is { Span: { IsEmpty: false } span } ? span : default;
}

internal sealed class JSeq : JNode
{
    public List<JNode> Items { get; } = [];
}

// Holds the parsed text alongside what KIND of literal it looked like on the page — an integer,
// a bare word, or something that needed quotes to disambiguate from either. The writer uses this
// to decide whether a value needs quoting by asking what it IS, not by re-deriving it from a pile
// of Contains(':')/StartsWith('-') heuristics after the fact. The reader still hands everything to
// callers as text (GetInt/GetBool re-parse it) — JAML's wire format is text; this only stops the
// writer from throwing typing away and immediately having to guess it back.
internal enum JScalarKind { Bare, Quoted, Integer }

internal sealed class JScalar(string value, JScalarKind kind = JScalarKind.Bare) : JNode
{
    public string Value { get; } = value;
    public JScalarKind Kind { get; } = kind;

    public static JScalar Of(int value) => new(value.ToString(), JScalarKind.Integer);
    public static JScalar Of(bool value) => new(value ? "true" : "false");
}

