using System.Buffers;
using VYaml.Parser;

namespace Motely.Filters.Jaml;

/// <summary>
/// The source line of every scalar in a JAML document, in document order.
///
/// VYaml 1.4 has no per-event start mark. <c>CurrentMark</c> is where the tokenizer's reader is,
/// and the tokenizer can be well ahead of the parser: past the next key after a plain scalar,
/// and past a whole flow collection (a <c>{ … }</c> line of JSON, or an entire one-line .json
/// file) before the parser emits the collection's first event. No mark read during parsing is
/// reliably on the scalar's own line, so this reads the lines off the source instead: one
/// parse of the document collects the scalar events in order, and a cursor walks the bytes
/// alongside it, skipping what lies between two scalars (whitespace, comments, indicators,
/// anchors, tags) and landing on each scalar's first byte.
///
/// Every step checks itself: a plain scalar must match the event's text, a quoted one must
/// open where the cursor lands. If anything disagrees, <see cref="TryBuild"/> returns null and
/// the formatter falls back to marks.
/// </summary>
internal sealed class JamlScalarLines
{
    private readonly int[] _lines;
    private readonly string?[] _values;
    private readonly int[] _listStarts;
    private int _nextList;
    private int _next = -1;
    private bool _broken;

    /// <summary>Every scalar's line and text, in document order (tests check the walk with these).</summary>
    internal IReadOnlyList<int> Lines => _lines;
    internal IReadOnlyList<string?> Values => _values;

    /// <summary>Root keys in document order with the line each is on.</summary>
    public IReadOnlyList<(string Key, int Line)> RootKeys { get; }

    private JamlScalarLines(int[] lines, string?[] values, int[] listStarts, List<(string, int)> rootKeys)
    {
        _lines = lines;
        _values = values;
        _listStarts = listStarts;
        RootKeys = rootKeys;
    }

    /// <summary>
    /// A must:/should:/mustNot: value is starting. VYaml's generated JamlConfig reader calls the
    /// clause-list formatter once per such root key, in document order, so the n-th call is the
    /// n-th of those keys and its first scalar is the one recorded for it.
    /// </summary>
    public void BeginList()
    {
        if (_nextList < _listStarts.Length)
            _next = _listStarts[_nextList++];
        else
            _broken = true;
    }

    /// <summary>The line of the scalar the formatter is about to read, without consuming it.</summary>
    public bool TryPeek(out int line)
    {
        line = 0;
        if (_broken || _next < 0 || _next >= _lines.Length)
            return false;
        line = _lines[_next];
        return true;
    }

    /// <summary>
    /// The formatter just read a scalar with this text: its line, and move on. A text that is
    /// not the one expected here means the walk and the formatter disagree about where they
    /// are, so every later answer is off too; from then on report nothing.
    /// </summary>
    public bool TryTake(string? value, out int line)
    {
        line = 0;
        if (_broken || _next < 0 || _next >= _lines.Length || !string.Equals(_values[_next], value, StringComparison.Ordinal))
        {
            _broken = true;
            return false;
        }
        line = _lines[_next++];
        return true;
    }

    public static JamlScalarLines? TryBuild(byte[] bytes)
    {
        try
        {
            return Build(bytes);
        }
        catch (Exception)
        {
            return null; // malformed YAML: Deserialize reports it with VYaml's own position
        }
    }

    private static JamlScalarLines? Build(byte[] bytes)
    {
        var walk = new Walk(bytes);
        var lines = new List<int>();
        var values = new List<string?>();
        var listStarts = new List<int>();
        var rootKeys = new List<(string, int)>();

        var parser = new YamlParser(new ReadOnlySequence<byte>(bytes));
        int depth = 0;           // open collections
        bool rootIsMapping = false;
        bool expectKey = false;  // inside the root mapping, the next depth-1 node is a key
        bool listValue = false;  // the key just read was must/should/mustNot

        while (parser.Read())
        {
            var type = parser.CurrentEventType;
            bool atRoot = rootIsMapping && depth == 1;

            // A root value starts: note where a clause list's scalars begin.
            if (atRoot && !expectKey && type is ParseEventType.Scalar or ParseEventType.SequenceStart
                    or ParseEventType.MappingStart or ParseEventType.Alias)
            {
                if (listValue)
                    listStarts.Add(lines.Count);
                listValue = false;
            }

            switch (type)
            {
                case ParseEventType.Scalar:
                {
                    // No span at all is the parser's made-up empty scalar (`joker:` with no value).
                    if (!parser.TryGetScalarAsSpan(out var raw))
                        raw = default;
                    int line = walk.Scalar(raw);
                    if (line < 0)
                        return null;
                    var value = parser.IsNullScalar() ? null : parser.GetScalarAsString();
                    lines.Add(line);
                    values.Add(value);
                    if (atRoot)
                    {
                        if (expectKey)
                        {
                            var key = value ?? "";
                            rootKeys.Add((key, line));
                            listValue = key is "must" or "should" or "mustNot";
                        }
                        expectKey = !expectKey;
                    }
                    break;
                }
                case ParseEventType.Alias:
                    if (atRoot) expectKey = true;
                    break;
                case ParseEventType.MappingStart or ParseEventType.SequenceStart:
                    if (depth == 0 && type == ParseEventType.MappingStart)
                    {
                        rootIsMapping = true;
                        expectKey = true;
                    }
                    depth++;
                    break;
                case ParseEventType.MappingEnd or ParseEventType.SequenceEnd:
                    depth--;
                    if (rootIsMapping && depth == 1)
                        expectKey = true; // a root value collection closed
                    break;
            }
        }
        return new JamlScalarLines([.. lines], [.. values], [.. listStarts], rootKeys);
    }

    /// <summary>A cursor over the document's bytes that steps from one scalar to the next.</summary>
    private sealed class Walk(byte[] b)
    {
        private readonly int[] _lineStarts = LineStarts(b);
        private int _pos = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;

        private static int[] LineStarts(byte[] b)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < b.Length; i++)
                if (b[i] == (byte)'\n')
                    starts.Add(i + 1);
            return [.. starts];
        }

        /// <summary>1-based line of a byte offset.</summary>
        private int LineAt(int pos)
        {
            int i = Array.BinarySearch(_lineStarts, pos);
            return (i >= 0 ? i : ~i - 1) + 1;
        }

        /// <summary>Line of the next scalar, whose parsed text is <paramref name="raw"/>; -1 if the
        /// source there is not that scalar.</summary>
        public int Scalar(ReadOnlySpan<byte> raw)
        {
            int start = SkipGap(_pos, out int firstIndicator);
            if (raw.Length == 0)
            {
                // `""` / `''`, or an empty value the parser made up (`key:` with nothing after
                // it, a bare `-`). The made-up one has no text: file it at the indicator that
                // implied it and step past only that indicator.
                if (start + 1 < b.Length && b[start] == b[start + 1] && b[start] is (byte)'"' or (byte)'\''
                    && !(b[start] == '\'' && start + 2 < b.Length && b[start + 2] == '\''))
                {
                    _pos = start + 2;
                    return LineAt(start);
                }
                if (firstIndicator >= 0)
                {
                    _pos = firstIndicator + 1;
                    return LineAt(firstIndicator);
                }
                return LineAt(Math.Min(_pos, b.Length));
            }

            if (start >= b.Length)
                return -1;
            int end = b[start] switch
            {
                (byte)'"' => DoubleQuoted(start),
                (byte)'\'' => SingleQuoted(start),
                (byte)'|' or (byte)'>' => Block(start),
                _ => Plain(start, raw),
            };
            if (end < 0)
                return -1;
            _pos = end;
            return LineAt(start);
        }

        private static bool IsSpace(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
        private static bool IsFlow(byte c) => c is (byte)',' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}';

        /// <summary>Past whitespace, comments, document markers, directives, indicators, anchors,
        /// tags and aliases: everything that can sit between two scalars.</summary>
        private int SkipGap(int p, out int firstIndicator)
        {
            firstIndicator = -1;
            while (p < b.Length)
            {
                byte c = b[p];
                bool lineStart = p == 0 || b[p - 1] == '\n';
                bool nextIsBreak = p + 1 >= b.Length || IsSpace(b[p + 1]);

                if (IsSpace(c)) { p++; continue; }
                if (c == '#' || (lineStart && c == '%'))
                {
                    while (p < b.Length && b[p] != '\n') p++;
                    continue;
                }
                if (lineStart && p + 2 < b.Length && (c == '-' || c == '.') && b[p + 1] == c && b[p + 2] == c
                    && (p + 3 >= b.Length || IsSpace(b[p + 3])))
                {
                    p += 3;
                    continue;
                }
                if (IsFlow(c)
                    || ((c == '-' || c == '?') && nextIsBreak)
                    // `:` ends a key. JSON writes it straight after a quoted key (`"a":1`), so
                    // between scalars it is always the indicator; a plain scalar that really
                    // starts with `:` then fails to match below, and the walk gives up.
                    || c == ':')
                {
                    if (firstIndicator < 0 && c is (byte)'-' or (byte)'?' or (byte)':' or (byte)',')
                        firstIndicator = p;
                    p++;
                    continue;
                }
                if (c is (byte)'&' or (byte)'!' or (byte)'*')
                {
                    while (p < b.Length && !IsSpace(b[p]) && !IsFlow(b[p])) p++;
                    continue;
                }
                return p;
            }
            return p;
        }

        private int DoubleQuoted(int p)
        {
            for (int i = p + 1; i < b.Length; i++)
            {
                if (b[i] == '\\') { i++; continue; }
                if (b[i] == '"') return i + 1;
            }
            return -1;
        }

        private int SingleQuoted(int p)
        {
            for (int i = p + 1; i < b.Length; i++)
            {
                if (b[i] != '\'') continue;
                if (i + 1 < b.Length && b[i + 1] == '\'') { i++; continue; } // '' is an escaped quote
                return i + 1;
            }
            return -1;
        }

        /// <summary>A `|` or `>` block scalar: the header line, then every line that is blank or
        /// indented deeper than the header's line.</summary>
        private int Block(int p)
        {
            int lineStart = _lineStarts[LineAt(p) - 1];
            int headerIndent = 0;
            while (lineStart + headerIndent < b.Length && b[lineStart + headerIndent] == ' ') headerIndent++;

            int i = p;
            while (i < b.Length && b[i] != '\n') i++;
            while (i < b.Length) // i is on a '\n'
            {
                int next = i + 1, indent = 0;
                while (next + indent < b.Length && b[next + indent] == ' ') indent++;
                int after = next + indent;
                bool blank = after >= b.Length || b[after] is (byte)'\n' or (byte)'\r';
                if (!blank && indent <= headerIndent)
                    return next;
                i = after;
                while (i < b.Length && b[i] != '\n') i++;
            }
            return b.Length;
        }

        /// <summary>A plain scalar: its text, with any run of whitespace (a folded line break)
        /// standing for any run in the source.</summary>
        private int Plain(int p, ReadOnlySpan<byte> raw)
        {
            int i = p, j = 0;
            while (j < raw.Length)
            {
                if (IsSpace(raw[j]))
                {
                    if (i >= b.Length || !IsSpace(b[i])) return -1;
                    while (j < raw.Length && IsSpace(raw[j])) j++;
                    while (i < b.Length && IsSpace(b[i])) i++;
                    continue;
                }
                if (i >= b.Length || b[i] != raw[j]) return -1;
                i++;
                j++;
            }
            return i;
        }
    }
}
