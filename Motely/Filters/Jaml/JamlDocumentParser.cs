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

/// <summary>Parse failure with a <see cref="JamlSpan"/> so editors can underline the
/// offending line. Position lives on the exception and in the message text.</summary>
internal sealed class JamlSyntaxException(string message, JamlSpan span)
    : Exception($"JAML parse error at line {span.StartLine + 1}: {message}")
{
    /// <summary>The message without the "JAML parse error at line N:" preamble.</summary>
    public string RawMessage { get; } = message;

    /// <summary>Where the failure sits. Required — every throw site is holding the line it failed
    /// on, so there is no such thing as a parse error that doesn't know where it happened.</summary>
    public JamlSpan Span { get; } = span;
}

internal static class JamlDocumentParser
{
    // ── JAML-native block format (the human-authored .jaml files) ──────────────────────────

    public static JMap ParseJaml(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int i = 0;
        var root = ParseBlock(lines, ref i, 0);
        if (root is not JMap map)
            throw new JamlSyntaxException("JAML root must be a mapping.", JamlSpan.OfLine(0, lines.Length > 0 ? lines[0] : ""));
        return map;
    }

    // Parses the block starting at `i` whose lines are indented at exactly `indent` — either a
    // sequence of "- " items or a sequence of "key: value" mappings. Which one is decided by the
    // first non-blank line's shape; a real JAML document never mixes the two at one indent level.
    private static JNode ParseBlock(string[] lines, ref int i, int indent)
    {
        SkipBlankAndComments(lines, ref i);
        if (i >= lines.Length || IndentOf(lines[i]) < indent)
            return new JMap(); // an empty block (e.g. `with:` with nothing under it) is an empty map

        string head = lines[i].AsSpan(IndentOf(lines[i])).TrimStart().ToString();
        if (head.StartsWith('['))
            return ParseMultilineFlowArray(lines, ref i, "");
        return head.StartsWith('-') ? ParseSequence(lines, ref i, indent) : ParseMapping(lines, ref i, indent);
    }

    private static JMap ParseMapping(string[] lines, ref int i, int indent)
    {
        var map = new JMap();
        while (true)
        {
            SkipBlankAndComments(lines, ref i);
            if (i >= lines.Length)
                break;
            int lineIndent = IndentOf(lines[i]);
            if (lineIndent < indent)
                break;
            if (lineIndent > indent)
                throw new JamlSyntaxException($"Unexpected indent (expected {indent} spaces).", JamlSpan.OfLine(i, lines[i]));

            var (key, inlineValue, keySpan, valueSpan) = SplitKeyValue(lines[i], i);
            int keyLineIndent = lineIndent;
            i++;

            if (inlineValue is "|" or ">")
            {
                map.Set(
                    key,
                    new JScalar(ParseBlockScalar(lines, ref i, keyLineIndent, inlineValue[0]))
                    {
                        Span = valueSpan.IsEmpty ? keySpan : valueSpan,
                    },
                    keySpan
                );
            }
            else if (inlineValue is "|-" or ">-" or "|+" or ">+")
            {
                // YAML's chomping indicators, refused on purpose. They tune how many trailing
                // newlines survive a block — a distinction JAML has nowhere to express, since
                // ParseBlockScalar always drops trailing blank lines. Accepting them would mean
                // reading '|+' and silently doing what '|' does, so a filter says one thing and
                // means another with nothing to warn the author. Two block styles, both honored.
                throw new JamlSyntaxException(
                    $"'{inlineValue}' is not a JAML block style. Use '|' to keep line breaks or '>' to fold them into spaces.",
                    JamlSpan.OfLine(i - 1, lines[i - 1]));
            }
            else if (inlineValue is { Length: > 0 } && inlineValue.StartsWith('[') && !inlineValue.EndsWith(']'))
            {
                // "key: [" opens a flow array inline but doesn't close it on this line — the
                // items and the closing "]" continue on following lines (real corpus files write
                // long shopItems:/seeds: arrays this way for readability). `i` already points at
                // the line right after the key line; the collector picks up from there.
                var multi = ParseMultilineFlowArray(lines, ref i, inlineValue);
                multi.Span = valueSpan.IsEmpty ? keySpan : valueSpan;
                map.Set(key, multi, keySpan);
            }
            else if (inlineValue is { Length: > 0 })
            {
                map.Set(key, ParseScalarOrFlow(inlineValue, i - 1, valueSpan), keySpan);
            }
            else
            {
                // "key:" with nothing after it — the value is a nested block. Normally deeper-
                // indented than the key; a block SEQUENCE or a flow array's opening "[" is also
                // legal at the SAME indent as its own key (real corpus files use both — "- or:\n
                // - erraticSuit: Hearts" with the nested list at the same column as "or:" itself).
                // A same-indent MAPPING would be genuinely ambiguous with the parent's own sibling
                // keys, so only a same-indent "-" or "[" start gets this exception.
                SkipBlankAndComments(lines, ref i);
                int childIndent = i < lines.Length ? IndentOf(lines[i]) : -1;
                bool sameIndentBlockStart =
                    childIndent == indent
                    && i < lines.Length
                    && lines[i].AsSpan(indent).TrimStart() is { Length: > 0 } head
                    && (head[0] == '-' || head[0] == '[');
                map.Set(
                    key,
                    childIndent > indent || sameIndentBlockStart
                        ? ParseBlock(lines, ref i, childIndent)
                        : new JMap(),
                    keySpan
                );
            }
        }
        return map;
    }

    private static JSeq ParseSequence(string[] lines, ref int i, int indent)
    {
        var seq = new JSeq();
        while (true)
        {
            SkipBlankAndComments(lines, ref i);
            if (i >= lines.Length)
                break;
            int lineIndent = IndentOf(lines[i]);
            if (lineIndent < indent)
                break;
            if (lineIndent > indent)
                throw new JamlSyntaxException($"Unexpected indent (expected {indent} spaces).", JamlSpan.OfLine(i, lines[i]));

            string trimmed = lines[i].AsSpan(lineIndent).ToString();
            if (!trimmed.StartsWith('-'))
            {
                // Not a list item at this indent — if this sequence IS a same-indent value of a
                // mapping key (see ParseBlock/ParseMapping's sameIndentBlockStart), this line is
                // actually the next SIBLING KEY of that same mapping, not part of the sequence
                // ("- or: [...]\n  min: 13" — "min" belongs next to "or", not inside its list).
                // Return what's collected so far and let the caller's own mapping loop re-examine
                // this line; it validates as a real key there, or fails there with a clear error.
                break;
            }

            // The list item's own content starts right after "- ", at a virtual indent equal to
            // where that content begins — this is what lets "- joker: Blueprint" open a mapping
            // whose later sibling keys ("  min: 1") are indented to align under the item content,
            // not under the dash.
            string afterDash = trimmed.Length > 1 ? trimmed[1..] : "";
            int contentCol = lineIndent + (trimmed.Length - afterDash.TrimStart().Length);
            string content = afterDash.TrimStart();

            if (content.Length == 0)
            {
                // "- " alone, value entirely on following indented lines.
                i++;
                seq.Items.Add(ParseBlock(lines, ref i, indent + 1));
                continue;
            }

            if (LooksLikeKeyValue(content))
            {
                // Rewrite this line as if it were a plain mapping line at contentCol, then let
                // ParseMapping consume it and any deeper-indented sibling keys that follow.
                lines[i] = new string(' ', contentCol) + content;
                seq.Items.Add(ParseMapping(lines, ref i, contentCol));
            }
            else
            {
                // A bare scalar list item — a one-line JAML clause ("Blueprint in ante 1"), a
                // plain string (a seeds: entry), or a flow array token — JamlLine/ScalarValue
                // consumers decide which. The tokenizer's only job is not to lose the text.
                i++;

                // A terse line carries continuation keys when deeper-indented "key: value" lines
                // follow it ("- Negative Perkeo" then "    ante: 0"), which is how the line form
                // stays usable for anything the one-liner has no spelling for. The line travels
                // under TerseLineKey and the loader hands it to JamlLine, so both spellings stay
                // one grammar.
                int nextIndent = PeekIndent(lines, i);
                if (nextIndent > lineIndent && HasKeyValueAt(lines, i))
                {
                    var mapping = ParseMapping(lines, ref i, nextIndent);
                    mapping.Set(TerseLineKey, new JScalar(content), JamlSpan.OfLine(i - 1, content));
                    seq.Items.Add(mapping);
                }
                else
                {
                    seq.Items.Add(new JScalar(content));
                }
            }
        }
        return seq;
    }

    /// <summary>
    /// Carries a terse one-line clause that also has continuation keys. Starts with a space so no
    /// authored JAML key can collide with it, and so a stray copy of it fails key validation.
    /// </summary>
    public const string TerseLineKey = " line";

    private static int PeekIndent(string[] lines, int i)
    {
        SkipBlankAndComments(lines, ref i);
        return i < lines.Length ? IndentOf(lines[i]) : -1;
    }

    private static bool HasKeyValueAt(string[] lines, int i)
    {
        SkipBlankAndComments(lines, ref i);
        return i < lines.Length && LooksLikeKeyValue(lines[i].AsSpan(IndentOf(lines[i])).ToString());
    }

    // A very small, deliberately permissive rule: "key: value" only when the FIRST colon is
    // followed by whitespace-or-end-of-line, so a one-line clause like "Perkeo score 100" (no
    // colon at all) or a seed string never gets misread as a mapping key.
    private static bool LooksLikeKeyValue(string content)
    {
        int idx = content.IndexOf(':');
        if (idx < 0)
            return false;
        return idx + 1 == content.Length || char.IsWhiteSpace(content[idx + 1]);
    }

    // Also reports where the key itself sits on the line. "Unknown key 'jokerz'" has to underline
    // jokerz — the column is free here (it's just how far in the text was trimmed) and impossible
    // to recover later, once the key is a bare string detached from its line. Same for the value:
    // "Cannot parse 'NotARealJoker'" underlines the token after the colon.
    private static (string Key, string? Value, JamlSpan KeySpan, JamlSpan ValueSpan) SplitKeyValue(
        string line,
        int lineIndex
    )
    {
        string uncommented = StripComment(line);
        string trimmed = uncommented.TrimStart();
        int keyColumn = uncommented.Length - trimmed.Length;
        int idx = trimmed.IndexOf(':');
        if (idx < 0 || (idx + 1 < trimmed.Length && !char.IsWhiteSpace(trimmed[idx + 1])))
            throw new JamlSyntaxException(
                $"Expected 'key: value' or 'key:', got '{trimmed}'.",
                JamlSpan.OnLine(lineIndex, keyColumn, trimmed.TrimEnd().Length));
        string key = trimmed[..idx].Trim();
        string value = trimmed[(idx + 1)..].Trim();
        // The key as written, before Trim() collapsed any padding between it and the colon.
        int keyLength = trimmed[..idx].TrimEnd().Length;
        var keySpan = JamlSpan.OnLine(lineIndex, keyColumn, keyLength);
        JamlSpan valueSpan = default;
        if (value.Length > 0)
        {
            int afterColon = keyColumn + idx + 1;
            while (afterColon < uncommented.Length && char.IsWhiteSpace(uncommented[afterColon]))
                afterColon++;
            valueSpan = JamlSpan.OnLine(lineIndex, afterColon, value.Length);
        }
        return (key, value.Length == 0 ? null : value, keySpan, valueSpan);
    }

    // Collects a flow array "[a, b, c]" that spans multiple lines — either "key: [" left open at
    // end of line (leadingText carries what followed the "["), or "key:" with the "[" starting
    // fresh on its own following line (leadingText is ""). `i` points at the first unconsumed
    // line either way. Consumes lines until one contains the matching "]", joins everything
    // between the brackets, and splits on commas — same token handling as a single-line flow
    // array, just gathered across lines first.
    private static JSeq ParseMultilineFlowArray(string[] lines, ref int i, string leadingText)
    {
        var sb = new System.Text.StringBuilder(leadingText);
        while (!sb.ToString().Contains(']'))
        {
            if (i >= lines.Length)
                throw new JamlSyntaxException("Unterminated flow array (missing ']').", JamlSpan.OfLine(i - 1, lines[i - 1]));
            sb.Append(' ').Append(StripComment(lines[i]).Trim());
            i++;
        }
        string joined = sb.ToString();
        int open = joined.IndexOf('[');
        int close = joined.IndexOf(']');
        string inner = joined[(open + 1)..close];

        var seq = new JSeq();
        if (inner.Trim().Length > 0)
            foreach (var token in inner.Split(','))
            {
                string t = token.Trim();
                if (t.Length == 0)
                    continue;
                seq.Items.Add(
                    int.TryParse(t, out _) ? new JScalar(t, JScalarKind.Integer) : new JScalar(t)
                );
            }
        return seq;
    }

    // Strips a trailing "# comment" from a line of real content — real corpus files annotate
    // values inline ("luckymoney: 4 # 1x luck normal ..."). Only strips a '#' that starts a new
    // token (preceded by start-of-line or whitespace), so a value that legitimately contains '#'
    // isn't mistaken for one — none in this grammar do, but the check costs nothing.
    private static string StripComment(string line)
    {
        for (int j = 0; j < line.Length; j++)
        {
            if (line[j] == '#' && (j == 0 || char.IsWhiteSpace(line[j - 1])))
                return line[..j];
        }
        return line;
    }

    // "key: >" (folded — lines join with spaces) or "key: |" (literal — lines keep their own
    // newlines): every following line indented deeper than the key's own indent belongs to the
    // block; the first line's indent sets the block's left margin, stripped from every line.
    // Real corpus files use this for description: text that wraps across multiple lines — a
    // plain "key: value" line can't hold that, so JAML honors the same block-scalar indicator
    // a human reaches for out of YAML habit, same as JamlLine already tolerates quoted scalars.
    private static string ParseBlockScalar(string[] lines, ref int i, int keyIndent, char style)
    {
        var content = new List<string>();
        int? blockIndent = null;
        while (i < lines.Length)
        {
            string raw = lines[i];
            if (raw.Trim().Length == 0)
            {
                content.Add("");
                i++;
                continue;
            }
            int lineIndent = IndentOf(raw);
            if (lineIndent <= keyIndent)
                break;
            blockIndent ??= lineIndent;
            content.Add(raw[Math.Min(blockIndent.Value, raw.Length)..]);
            i++;
        }
        // Trim the trailing blank lines a block naturally accumulates at its end (the next key's
        // dedent stops the loop one line after the real content).
        while (content.Count > 0 && content[^1].Length == 0)
            content.RemoveAt(content.Count - 1);
        return style == '>' ? string.Join(" ", content).Trim() : string.Join("\n", content);
    }

    // A scalar value, or a flow array "[a, b, c]" — JAML's one and only bracket syntax. No flow
    // mappings: JamlLine's one-line clause tail already covers the case (a whole clause on one
    // line) that would otherwise tempt someone to reach for a fancier YAML form. Block scalars
    // are a separate path — see ParseBlockScalar, reached from ParseMapping when a key's value
    // is '|' or '>'.
    private static JNode ParseScalarOrFlow(string text, int lineIndex, JamlSpan valueSpan = default)
    {
        text = text.Trim();
        // "{}" — an explicit empty mapping. Real meaning: "sources: {}" overrides with
        // match-nowhere, distinct from an absent sources: key (use engine defaults) — the loader
        // tells the two apart by whether GetObject("sources") returns a map at all, so "{}" must
        // become a real (empty) JMap here, not fall through to a bare scalar string "{}".
        if (text == "{}")
            return new JMap { Span = valueSpan };
        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            var seq = new JSeq { Span = valueSpan };
            string inner = text[1..^1];
            if (inner.Trim().Length > 0)
                foreach (var token in inner.Split(','))
                    seq.Items.Add(new JScalar(token.Trim()) { Span = valueSpan });
            return seq;
        }
        // Strip a matching pair of quotes if present — JAML authors sometimes quote scalars out
        // of YAML habit; there's no escaping grammar to honor beyond that.
        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
            text = text[1..^1];
        return new JScalar(text) { Span = valueSpan };
    }

    private static int IndentOf(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ')
            i++;
        return i;
    }

    private static void SkipBlankAndComments(string[] lines, ref int i)
    {
        while (i < lines.Length)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                i++;
                continue;
            }
            break;
        }
    }
}
