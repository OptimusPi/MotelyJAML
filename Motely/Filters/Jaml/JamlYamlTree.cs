using System.Globalization;
using System.Text;
using VYaml.Parser;

namespace Motely.Filters.Jaml;

/// <summary>A document that is not valid YAML, with the line the scanner stopped on.</summary>
internal sealed class JamlSyntaxException(string message, JamlSpan span, Exception inner)
    : InvalidOperationException(message, inner)
{
    public JamlSpan Span { get; } = span;
}

// The document tree VYaml parse events are read into. The loader walks this; nothing else.
internal abstract class JNode
{
    public JamlSpan Span { get; set; }
}

internal sealed class JMap : JNode
{
    // Insertion order preserved; lookups are case-insensitive, like every wire key in the grammar.
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

    public JamlSpan KeySpan(string key) => _keySpans.TryGetValue(key, out var s) ? s : default;

    /// <summary>The value's span when stamped, else the key's (same line for inline values).</summary>
    public JamlSpan ValueSpan(string key) =>
        Get(key) is { Span: { IsEmpty: false } span } ? span : KeySpan(key);
}

internal sealed class JSeq : JNode
{
    public List<JNode> Items { get; } = [];
}

internal sealed class JScalar(string value) : JNode
{
    public string Value { get; } = value;

    /// <summary>YAML null (<c>null</c>, <c>~</c>, a blank value). Reads as not written.</summary>
    public bool IsNull { get; init; }
}

/// <summary>
/// One value off the tree, as a desc reads it (<see cref="IJamlValueReader"/>): the node itself
/// plus where it sits, so a value a desc rejects underlines itself. Malformed values throw a
/// positioned <see cref="JamlSemanticException"/>; <c>false</c> means absent, not invalid.
/// </summary>
internal sealed class JamlLoaderValueReader(JNode? node, JamlSpan span = default) : IJamlValueReader
{
    public JamlSpan Span { get; } = span.IsEmpty && node is not null ? node.Span : span;

    /// <summary>The value as written: a scalar's text, or a sequence joined for the message.</summary>
    public string Text =>
        node switch
        {
            JScalar { IsNull: false } s => s.Value,
            JSeq seq => string.Join(", ", seq.Items.Select(ScalarText)),
            _ => "",
        };

    public bool IsAny => JamlDisc.IsAnyToken(Text);

    /// <summary>The sequence's items, or null when the value is not a written sequence.</summary>
    private string[]? Items =>
        node is JSeq { Items.Count: > 0 } seq ? [.. seq.Items.Select(ScalarText)] : null;

    private static string ScalarText(JNode item) => item is JScalar { IsNull: false } s ? s.Value : "";

    public static JamlLoaderValueReader FromScalar(string? text, JamlSpan span = default) =>
        new(new JScalar(text ?? "") { Span = span }, span);

    public static JamlLoaderValueReader FromStrings(string[]? values, JamlSpan span = default)
    {
        if (values is null)
            return new(null, span);
        var seq = new JSeq { Span = span };
        foreach (var value in values)
            seq.Items.Add(new JScalar(value) { Span = span });
        return new(seq, span);
    }

    // `[]` is the one list shape that is not a value: the whole-category token is Any, or blank.
    private void RejectEmptyList()
    {
        if (node is JSeq { Items.Count: 0 })
            throw new JamlSemanticException(
                "'[]' is not a value. Write Any, or leave it blank, for the whole category.",
                Span
            );
    }

    public bool TryInt(out int value) =>
        int.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public bool TryBool(out bool value) => TryParseBool(Text, out value);

    /// <summary>The one bool spelling JAML accepts: true/false, yes/no.</summary>
    internal static bool TryParseBool(string text, out bool value)
    {
        if (bool.TryParse(text, out value))
            return true;
        if (string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
            return value = true;
        if (string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
            return !(value = false);
        return value = false;
    }

    public bool TryIntArray(out int[] value)
    {
        RejectEmptyList();
        if (Items is null && string.IsNullOrWhiteSpace(Text))
        {
            value = [];
            return false;
        }

        var list = new List<int>();
        foreach (var token in Items ?? [Text])
        {
            if (JamlIntRange.TrySplit(token, out int lo, out int hi))
                JamlIntRange.Append(list, lo, hi);
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
        if (string.IsNullOrWhiteSpace(Text))
        {
            value = default;
            return false;
        }
        value = ParseOne<TEnum>(Text);
        return true;
    }

    public bool TryRank(out MotelyStandardcardRank value)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            value = default;
            return false;
        }
        value = JamlConfigLoader.ParseRank(Text, Span);
        return true;
    }

    public bool TryEnumArray<TEnum>(out TEnum[] value) where TEnum : struct, Enum
    {
        RejectEmptyList();
        var parts = Items ?? (JamlDisc.IsAnyToken(Text) ? [] : [Text]);
        if (parts.Length == 0 || (parts.Length == 1 && JamlDisc.IsAnyToken(parts[0])))
        {
            value = [];
            return true;
        }

        value = new TEnum[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (JamlDisc.IsAnyToken(parts[i]))
                throw new JamlSemanticException("'Any' is the whole category, not a list member.", Span);
            value[i] = ParseOne<TEnum>(parts[i]);
        }
        return true;
    }

    private TEnum ParseOne<TEnum>(string text) where TEnum : struct, Enum =>
        typeof(TEnum) == typeof(MotelyStandardcardRank)
            ? (TEnum)(object)JamlConfigLoader.ParseRank(text, Span)
            : JamlConfigLoader.ParseEnum<TEnum>(text, Span);
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

/// <summary>YAML text → <see cref="JMap"/> through VYaml's parser. JSON is read as YAML.</summary>
internal static class JamlYamlTree
{
    private const int NodeCap = 65_536;

    public static JMap Parse(string text)
    {
        // VYaml hangs on a bare trailing "joker:" with no newline after it; a newline makes it null.
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!text.EndsWith('\n'))
            text += "\n";

        var locator = new Locator(text);
        var parser = YamlParser.FromBytes(Encoding.UTF8.GetBytes(text));
        try
        {
            parser.SkipAfter(ParseEventType.DocumentStart);
            if (parser.End
                || parser.CurrentEventType is ParseEventType.DocumentEnd or ParseEventType.StreamEnd)
                return new JMap();
            return Read(ref parser, locator) as JMap
                ?? throw new InvalidOperationException("YAML root must be a mapping.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The scanner's mark is where it gave up; step back over blank lines to the one to fix.
            var lines = text.Split('\n');
            var line = Math.Clamp(parser.CurrentMark.Line - 1, 0, lines.Length - 1);
            while (line > 0 && string.IsNullOrWhiteSpace(lines[line]))
                line--;
            throw new JamlSyntaxException(
                $"YAML parse error at '{lines[line].Trim()}': {ex.Message}",
                JamlSpan.WholeLine(line, Math.Max(lines[line].Length, 1)),
                ex
            );
        }
    }

    // Each Read consumes exactly one node.
    private static JNode Read(ref YamlParser parser, Locator locator)
    {
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
            {
                var isNull = parser.IsNullScalar();
                var text = isNull ? "" : parser.GetScalarAsString() ?? "";
                var span = locator.Scalar(text);
                parser.Read();
                return new JScalar(text) { Span = span, IsNull = isNull };
            }
            case ParseEventType.MappingStart:
            {
                var map = new JMap { Span = locator.Here() };
                parser.Read();
                for (int n = 0; !parser.End && parser.CurrentEventType != ParseEventType.MappingEnd; n++)
                {
                    if (n > NodeCap)
                        throw new InvalidOperationException("YAML mapping did not terminate.");
                    if (parser.CurrentEventType != ParseEventType.Scalar)
                        throw new InvalidOperationException("YAML mapping key must be a scalar.");
                    var key = parser.GetScalarAsString() ?? "";
                    var keySpan = locator.Scalar(key);
                    if (!parser.Read())
                        throw new InvalidOperationException($"YAML mapping '{key}' is missing a value.");
                    map.Set(key, Read(ref parser, locator), keySpan);
                }
                if (parser.CurrentEventType == ParseEventType.MappingEnd)
                    parser.Read();
                return map;
            }
            case ParseEventType.SequenceStart:
            {
                var seq = new JSeq { Span = locator.Here() };
                parser.Read();
                for (int n = 0; !parser.End && parser.CurrentEventType != ParseEventType.SequenceEnd; n++)
                {
                    if (n > NodeCap)
                        throw new InvalidOperationException("YAML sequence did not terminate.");
                    seq.Items.Add(Read(ref parser, locator));
                }
                if (parser.CurrentEventType == ParseEventType.SequenceEnd)
                    parser.Read();
                return seq;
            }
            case ParseEventType.Alias:
                throw new InvalidOperationException("YAML aliases are not supported.");
            default:
                throw new InvalidOperationException($"Unexpected YAML event {parser.CurrentEventType}.");
        }
    }

    /// <summary>
    /// Stamps source spans. VYaml's mark runs past the token, so each scalar is found by searching
    /// forward from the end of the previous one (tokens arrive in document order).
    /// </summary>
    private sealed class Locator(string text)
    {
        private int _cursor;

        public JamlSpan Scalar(string value)
        {
            if (value.Length > 0)
            {
                int at = text.IndexOf(value, _cursor, StringComparison.Ordinal);
                if (at >= 0)
                {
                    _cursor = at + value.Length;
                    return SpanOf(at, value.Length);
                }
            }
            return Here();
        }

        public JamlSpan Here()
        {
            int at = _cursor;
            while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '-' or ':' or ',' or '[' or '{')
                at++;
            return SpanOf(Math.Min(at, text.Length), 0);
        }

        private JamlSpan SpanOf(int offset, int length)
        {
            int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
            int line = 0;
            for (int i = 0; i < lineStart; i++)
                if (text[i] == '\n')
                    line++;
            return JamlSpan.OnLine(line, offset - lineStart, length);
        }
    }
}
