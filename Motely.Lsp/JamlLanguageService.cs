using Motely.Filters.Jaml;

namespace Motely.Lsp.Core;

/// <summary>
/// The JAML language brain: diagnostics, hover, and completion computed directly off the
/// engine's own grammar — <c>JamlSchema</c> (generated from <c>[JamlDiscriminator]</c>),
/// <c>JamlConfigLoader</c> (the one true parser), and the engine's enums (the one true
/// vocabulary). Protocol-free on purpose: the stdio server and tests call these same methods,
/// so the grammar stays authored exactly once, in C#.
/// </summary>
public static class JamlLanguageService
{
    // ── Diagnostics ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse <paramref name="text"/> with the real engine loader and report what it reports.
    /// Every positioned failure carries its own <see cref="JamlSpan"/> straight from the engine —
    /// the tokenizer's for a syntax error, the rejected key's for an unknown-key error — so the
    /// squiggle lands on the offending token. A semantic error the loader can't yet place
    /// (a value outside its enum) falls back to the first line.
    /// </summary>
    public static IReadOnlyList<JamlDiagnostic> Diagnose(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        try
        {
            _ = JamlConfigLoader.FromJaml(text);
            return [];
        }
        catch (Exception ex)
        {
            return [ToDiagnostic(ex, text)];
        }
    }

    private static JamlDiagnostic ToDiagnostic(Exception ex, string text)
    {
        // Both positioned failures carry the span the parser/loader already held — walk the
        // inner chain (FromJaml can wrap) and paint the squiggle exactly where it belongs.
        for (Exception? walk = ex; walk is not null; walk = walk.InnerException)
        {
            // Syntax: the tokenizer's own span, message without the "at line N" preamble.
            if (walk is JamlSyntaxException syntax)
                return new JamlDiagnostic(
                    ClampSpan(syntax.Span, text),
                    syntax.RawMessage,
                    JamlDiagnosticSeverity.Error,
                    "JAML0001"
                );

            // Semantic (unknown key, bad value): the span of the token the loader rejected —
            // no regex over the message, no re-searching the document for a quoted word.
            if (walk is JamlSemanticException semantic && !semantic.Span.IsEmpty)
                return new JamlDiagnostic(
                    semantic.Span,
                    semantic.Message,
                    JamlDiagnosticSeverity.Error,
                    "JAML0100"
                );
        }

        // A semantic error the loader couldn't place (a value outside its enum, until those
        // throw sites carry a span too): underline the first line rather than invent a column.
        var firstLineLength = text.IndexOf('\n') is var nl && nl >= 0 ? nl : text.Length;
        return new JamlDiagnostic(
            JamlSpan.WholeLine(0, Math.Max(firstLineLength, 1)),
            ex.Message,
            JamlDiagnosticSeverity.Error,
            "JAML0100"
        );
    }

    private static JamlSpan ClampSpan(JamlSpan span, string text)
    {
        if (!span.IsEmpty)
            return span;
        var firstLineLength = text.IndexOf('\n') is var nl && nl >= 0 ? nl : text.Length;
        return JamlSpan.WholeLine(0, Math.Max(firstLineLength, 1));
    }

    // ── Hover ───────────────────────────────────────────────────────────────────────────

    /// <summary>Markdown for the word under the cursor, or null when there is nothing to say.</summary>
    public static JamlHoverInfo? Hover(string text, int line, int character)
    {
        var lines = SplitLines(text);
        if (line < 0 || line >= lines.Length)
            return null;
        var (word, span) = WordAt(lines[line], line, character);
        if (word.Length == 0)
            return null;

        if (IsDiscriminator(word))
        {
            var keys = JamlSchema.ClauseKeysFor(word);
            var md = $"**{word}** — JAML clause";
            var valueEnum = JamlSchema.ValueEnumTypeFor(word);
            if (valueEnum is not null)
                md += $"\n\nValue: `{valueEnum.Name}` ({Enum.GetNames(valueEnum).Length} names)";
            if (keys.Length > 0)
                md += $"\n\nKeys: {string.Join(", ", keys.Select(k => $"`{k}`"))}";
            if (JamlSchema.RollsAreInlineFor(word))
                md += "\n\nRolls event — the value is the roll list.";
            return new JamlHoverInfo(span, md);
        }

        foreach (var (enumType, kind) in JamlSchema.ValueEnumKinds)
            if (Enum.GetNames(enumType).FirstOrDefault(n =>
                    n.Equals(word, StringComparison.OrdinalIgnoreCase)) is { } exact)
                return new JamlHoverInfo(span, $"**{exact}** — {kind} (`{enumType.Name}`)");

        var context = ContextAt(lines, line);
        if (context.Discriminator is { } disc)
        {
            var keys = JamlSchema.ClauseKeysFor(disc);
            if (keys.Any(k => k.Equals(word, StringComparison.OrdinalIgnoreCase)))
                return new JamlHoverInfo(span, $"`{word}` — key of the **{disc}** clause");
        }

        return null;
    }

    // ── Explain (schema dump for tools / @jimbo /explain) ─────────────────────────────

    /// <summary>
    /// Markdown explanation of a JAML word from the engine schema — discriminators, clause keys,
    /// value enums, root keys, or vocabulary hits via <see cref="JamlSchema.ListItems"/>.
    /// Returns null when the topic is empty or unknown.
    /// </summary>
    public static string? Explain(string topic)
    {
        var q = topic.Trim();
        if (q.Length == 0)
            return null;

        // Bare kind: "joker" as discriminator vs listItems query "joker Blueprint"
        var parts = q.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
        var head = parts[0];
        var rest = parts.Length > 1 ? parts[1].Trim() : null;

        if (IsDiscriminator(head))
        {
            var disc = JamlSchema.Discriminators.First(d =>
                d.Equals(head, StringComparison.OrdinalIgnoreCase)
            );
            var keys = JamlSchema.ClauseKeysFor(disc);
            var valueEnum = JamlSchema.ValueEnumTypeFor(disc);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"**`{disc}`** — JAML clause discriminator (Motely engine schema)");
            sb.AppendLine();
            if (valueEnum is not null)
            {
                var names = Enum.GetNames(valueEnum);
                sb.AppendLine(
                    $"**Value enum:** `{valueEnum.Name}` ({names.Length} names; empty list = category any)"
                );
                if (rest is { Length: > 0 })
                {
                    var hits = JamlSchema.ListItems(disc, rest);
                    if (hits.Length > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"**Matches for `{rest}`:**");
                        foreach (var h in hits.Take(25))
                            sb.AppendLine($"- `{h}`");
                        if (hits.Length > 25)
                            sb.AppendLine($"- … +{hits.Length - 25} more");
                    }
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine("Sample values: " + string.Join(", ", names.Take(12).Select(n => $"`{n}`")));
                    if (names.Length > 12)
                        sb.AppendLine($"… +{names.Length - 12} more (ask e.g. `{disc} lucky`)");
                }
            }
            if (keys.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Clause keys:** " + string.Join(", ", keys.Select(k => $"`{k}`")));
            }
            if (JamlSchema.RollsAreInlineFor(disc))
            {
                sb.AppendLine();
                sb.AppendLine("Rolls event — the clause value is the roll list.");
            }
            var sources = JamlSchema.SourceKeysFor(disc);
            if (sources is { Length: > 0 })
            {
                sb.AppendLine();
                sb.AppendLine("**Source keys:** " + string.Join(", ", sources.Select(k => $"`{k}`")));
            }
            sb.AppendLine();
            sb.AppendLine(
                "**must** = hard filter · **should** = scoring · **mustNot** = forbid. One grammar: FilterDesc → JamlSchema."
            );
            return sb.ToString().TrimEnd();
        }

        if (JamlConfig.RootKeys.Any(k => k.Equals(head, StringComparison.OrdinalIgnoreCase)))
        {
            var key = JamlConfig.RootKeys.First(k => k.Equals(head, StringComparison.OrdinalIgnoreCase));
            return key switch
            {
                "must" =>
                    "**`must`** — hard requirements. Seed fails if any must clause misses. List of clauses under `- disc: …`.",
                "should" =>
                    "**`should`** — soft scoring clauses. Matches add score (see `score:`). Do not alone fail the seed unless cutoff logic says so.",
                "mustNot" =>
                    "**`mustNot`** — forbidden patterns. Seed fails if a mustNot clause matches.",
                "deck" =>
                    "**`deck`** — MotelyDeck (Red, Blue, … Erratic).",
                "stake" =>
                    "**`stake`** — MotelyStake (White … Gold).",
                "seeds" =>
                    "**`seeds`** — optional saved seed list for list search / lake.",
                "name" => "**`name`** — human filter title.",
                _ => $"**`{key}`** — JAML root key (engine `JamlConfig.RootKeys`).",
            };
        }

        // Enum vocabulary hit (joker name, voucher, …)
        foreach (var (enumType, kind) in JamlSchema.ValueEnumKinds)
        {
            var exact = Enum.GetNames(enumType)
                .FirstOrDefault(n => n.Equals(head, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return $"**`{exact}`** — {kind} (`{enumType.Name}`). Use under the matching clause discriminator.";
        }

        // ListItems kind + optional query — unknown kinds throw; treat as not found.
        // Runs before clause-key explain so "edition" / "edition foil" hit vocabulary, not "key of commonJoker".
        try
        {
            if (rest is not null)
            {
                var items = JamlSchema.ListItems(head, rest);
                if (items.Length > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"**`{head}`** vocabulary matches for `{rest}` ({items.Length}):");
                    foreach (var h in items.Take(40))
                        sb.AppendLine($"- `{h}`");
                    if (items.Length > 40)
                        sb.AppendLine($"- … +{items.Length - 40} more");
                    return sb.ToString().TrimEnd();
                }
            }
            else
            {
                var items = JamlSchema.ListItems(head, null);
                if (items.Length > 0 && items.Length <= 80)
                {
                    return $"**`{head}`** — {items.Length} engine names:\n"
                        + string.Join("\n", items.Take(40).Select(i => $"- `{i}`"))
                        + (items.Length > 40 ? $"\n- … +{items.Length - 40} more" : "");
                }
                if (items.Length > 80)
                {
                    return $"**`{head}`** — {items.Length} engine names. Narrow with a query, e.g. `{head} lucky`.";
                }
            }
        }
        catch (ArgumentException)
        {
            // Unknown vocabulary kind — fall through to clause-key / null.
        }

        // Clause keys (antes, sources, min, …) live on FilterDescs via JamlSchema — not root.
        var clauseOwners = JamlSchema.Discriminators
            .Where(d =>
                JamlSchema.ClauseKeysFor(d)
                    .Any(k => k.Equals(head, StringComparison.OrdinalIgnoreCase))
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d switch
            {
                "joker" => 0,
                "voucher" => 1,
                "tarot" => 2,
                "planet" => 3,
                "spectral" => 4,
                "boosterPack" => 5,
                _ => 10
            })
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (clauseOwners.Length > 0)
        {
            var sample = clauseOwners.Take(12).Select(d => $"`{d}`");
            var more =
                clauseOwners.Length > 12 ? $", … +{clauseOwners.Length - 12} more" : "";
            return $"**`{head}`** — clause key on: {string.Join(", ", sample)}{more}.\n\n"
                + "Per-clause wire key from FilterDesc → JamlSchema (not a document root key).";
        }

        return null;
    }

    // ── Semantic tokens ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The legend for <see cref="SemanticTokens"/>, in index order. Standard LSP token types on
    /// purpose: every shipped VS Code theme maps them through the Semantic Token Scope Map, so
    /// JAML colours without a TextMate grammar and without per-theme rules.
    /// </summary>
    public static readonly string[] SemanticTokenTypes =
    [
        "keyword", // 0 — document root keys (must, should, deck, …)
        "type", // 1 — clause discriminators (joker, legendaryJoker, …)
        "property", // 2 — clause / source / with keys (antes, score, edition, …)
        "enumMember", // 3 — engine enum values (Perkeo, Negative, Gold, …)
        "number", // 4 — numeric literals
        "comment", // 5 — '#' to end of line
    ];

    /// <summary>No modifiers are emitted; the legend still has to be declared.</summary>
    public static readonly string[] SemanticTokenModifiers = [];

    /// <summary>
    /// Highlighting straight off the engine grammar — the same <c>JamlSchema</c> that answers
    /// completion and hover, so a colour and a diagnostic can never disagree about what a word is.
    /// Returns the LSP relative encoding: five ints per token
    /// (deltaLine, deltaStartChar, length, typeIndex, modifierMask).
    /// </summary>
    public static IReadOnlyList<int> SemanticTokens(string text)
    {
        var data = new List<int>();
        if (string.IsNullOrEmpty(text))
            return data;

        var lines = SplitLines(text);

        // Collected first, encoded after sorting: the relative encoding requires tokens in
        // position order, and a line's comment is found before the key/value on that same line.
        var found = new List<(int Line, int Start, int Length, int Type)>();
        void Emit(int line, int start, int length, int type)
        {
            if (length > 0)
                found.Add((line, start, length, type));
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];

            // Same rule as JamlDocumentParser.StripComment: a '#' opens a comment only at the
            // start of a token. Matching it exactly keeps the colour and the parse in agreement.
            var content = raw;
            for (var j = 0; j < raw.Length; j++)
                if (raw[j] == '#' && (j == 0 || char.IsWhiteSpace(raw[j - 1])))
                {
                    content = raw[..j];
                    Emit(i, j, raw.Length - j, 5);
                    break;
                }

            var body = content.TrimStart();
            if (body.Length == 0)
                continue;

            var indent = content.Length - body.Length;
            if (body.StartsWith("- ", StringComparison.Ordinal))
            {
                indent += 2;
                body = body[2..];
            }

            var colon = body.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = body[..colon].Trim();
            var keyStart = indent + body.IndexOf(key, StringComparison.Ordinal);

            if (IsDiscriminator(key))
                Emit(i, keyStart, key.Length, 1);
            else if (JamlConfig.RootKeys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
                Emit(i, keyStart, key.Length, 0);
            else
                Emit(i, keyStart, key.Length, 2);

            // Values: comma/bracket separated words. Enum names colour as enumMember, bare digits
            // as number; anything the engine does not recognise stays uncoloured rather than
            // guessing — an unknown word looking plain is the same signal the diagnostic gives.
            var valueStart = colon + 1;
            var scan = indent + valueStart;
            var value = body[valueStart..];
            for (var v = 0; v < value.Length; )
            {
                if (!char.IsLetterOrDigit(value[v]) && value[v] != '_')
                {
                    v++;
                    continue;
                }
                var start = v;
                while (v < value.Length && (char.IsLetterOrDigit(value[v]) || value[v] == '_'))
                    v++;
                var word = value[start..v];
                if (word.All(char.IsDigit))
                    Emit(i, scan + start, word.Length, 4);
                else if (IsEnumValue(word))
                    Emit(i, scan + start, word.Length, 3);
            }
        }

        int lastLine = 0;
        int lastStart = 0;
        foreach (var (line, start, length, type) in found.OrderBy(t => t.Line).ThenBy(t => t.Start))
        {
            var deltaLine = line - lastLine;
            data.Add(deltaLine);
            data.Add(deltaLine == 0 ? start - lastStart : start);
            data.Add(length);
            data.Add(type);
            data.Add(0);
            lastLine = line;
            lastStart = start;
        }

        return data;
    }

    // ── Document symbols ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The outline: Ctrl+Shift+O, breadcrumbs, and the Outline view. Nesting comes from
    /// indentation, read with the same comment rule, <c>"- "</c> handling, and key extraction
    /// <see cref="SemanticTokens"/> uses, so the tree can never disagree with the colours about
    /// where a key starts or what kind it is.
    ///
    /// List items are the case worth stating: <c>- joker:</c> entries at one column are siblings
    /// of each other, and the plain keys that follow at that same column (<c>antes:</c>,
    /// <c>score:</c>) are the item's children, not its siblings — YAML block-sequence shape.
    /// </summary>
    /// <summary>A node under construction: children accumulate until something at its level or
    /// shallower arrives, which is also when its end line becomes known.</summary>
    private sealed record SymbolFrame(
        int Indent,
        bool IsDash,
        string Name,
        int Kind,
        JamlSpan Selection,
        List<JamlDocumentSymbol> Children
    );

    public static IReadOnlyList<JamlDocumentSymbol> DocumentSymbols(string text)
    {
        var roots = new List<JamlDocumentSymbol>();
        if (string.IsNullOrEmpty(text))
            return roots;

        var lines = SplitLines(text);
        var stack = new List<SymbolFrame>();

        // Close every frame the incoming line is not inside, ending each block on `endLine`.
        void Close(int downToCount, int endLine)
        {
            while (stack.Count > downToCount)
            {
                var f = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                var endColumn = endLine >= 0 && endLine < lines.Length ? lines[endLine].Length : 0;
                var node = new JamlDocumentSymbol(
                    f.Name,
                    f.Kind,
                    new JamlSpan(f.Selection.StartLine, f.Indent, Math.Max(endLine, f.Selection.StartLine), endColumn),
                    f.Selection,
                    f.Children
                );
                if (stack.Count > 0)
                    stack[^1].Children.Add(node);
                else
                    roots.Add(node);
            }
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];

            // Same comment rule as SemanticTokens / JamlDocumentParser.StripComment.
            var content = raw;
            for (var j = 0; j < raw.Length; j++)
                if (raw[j] == '#' && (j == 0 || char.IsWhiteSpace(raw[j - 1])))
                {
                    content = raw[..j];
                    break;
                }

            var body = content.TrimStart();
            if (body.Length == 0)
                continue;

            var indent = content.Length - body.Length;
            var isDash = body.StartsWith("- ", StringComparison.Ordinal);
            if (isDash)
            {
                indent += 2;
                body = body[2..];
            }

            var colon = body.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = body[..colon].Trim();
            if (key.Length == 0)
                continue;
            var keyStart = indent + body.IndexOf(key, StringComparison.Ordinal);

            // Pop frames this line is not inside. At equal indent a dash always starts a new
            // sibling; a plain key only stays nested when the frame it meets is a dash item.
            var keep = stack.Count;
            while (
                keep > 0
                && (stack[keep - 1].Indent > indent
                    || (stack[keep - 1].Indent == indent && (isDash || !stack[keep - 1].IsDash)))
            )
                keep--;
            Close(keep, i - 1);

            var kind = IsDiscriminator(key) ? 5 // Class
                : JamlConfig.RootKeys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase))
                    ? 3 // Namespace
                    : 7; // Property

            stack.Add(new SymbolFrame(indent, isDash, key, kind, JamlSpan.OnLine(i, keyStart, key.Length), []));
        }

        Close(0, lines.Length - 1);
        return roots;
    }

    private static bool IsEnumValue(string word)
    {
        foreach (var (enumType, _) in JamlSchema.ValueEnumKinds)
            if (
                Enum.GetNames(enumType)
                    .Any(n => n.Equals(word, StringComparison.OrdinalIgnoreCase))
            )
                return true;
        return false;
    }

    // ── Completion ──────────────────────────────────────────────────────────────────────

    /// <summary>Completion candidates at the cursor, already filtered by the typed prefix.</summary>
    public static IReadOnlyList<JamlCompletionItem> Complete(string text, int line, int character)
    {
        var lines = SplitLines(text);
        var current = line >= 0 && line < lines.Length ? lines[line] : "";
        var endCol = Math.Min(Math.Max(character, 0), current.Length);
        var prefix = current[..endCol];

        var indent = CountIndent(prefix);
        var body = prefix.TrimStart();
        var isListItem = body.StartsWith("- ", StringComparison.Ordinal) || body == "-";
        if (isListItem)
            body = body.TrimStart('-').TrimStart();

        var colon = body.IndexOf(':');
        if (colon >= 0)
        {
            var valuePrefix = body[(colon + 1)..].TrimStart();
            var replace = TokenReplaceSpan(line, endCol, valuePrefix);
            return CompleteValue(lines, line, body[..colon].Trim(), valuePrefix, replace);
        }

        var replaceKey = TokenReplaceSpan(line, endCol, body);
        return CompleteKey(lines, line, indent, isListItem, body, replaceKey);
    }

    /// <summary>Span of the incomplete token ending at <paramref name="endColumn"/>.</summary>
    private static JamlSpan TokenReplaceSpan(int line, int endColumn, string typed)
    {
        var start = Math.Max(0, endColumn - typed.Length);
        return JamlSpan.OnLine(line, start, endColumn - start);
    }

    private static IReadOnlyList<JamlCompletionItem> CompleteValue(
        string[] lines, int line, string key, string valuePrefix, JamlSpan replace)
    {
        // Discriminator value: enum names only. Category any is an empty disc (`joker:`), not `[]` and not a token.
        if (IsDiscriminator(key) && JamlSchema.ValueEnumTypeFor(key) is { } valueEnum)
        {
            var names = Enum.GetNames(valueEnum).ToList();
            return FilterNames(names, valuePrefix, "value", valueEnum.Name, replace);
        }

        if (JamlSchema.EnumTypeForKind(key) is { } keyEnum)
            return FilterNames(Enum.GetNames(keyEnum), valuePrefix, "value", keyEnum.Name, replace);

        return [];
    }

    private static IReadOnlyList<JamlCompletionItem> CompleteKey(
        string[] lines, int line, int indent, bool isListItem, string typed, JamlSpan replace)
    {
        if (indent == 0 && !isListItem)
            return FilterNames(JamlConfig.RootKeys, typed, "key", "JAML root key", replace);

        var context = ContextAt(lines, line);

        if (isListItem && context.InClauseList)
            return FilterNames(
                JamlSchema.Discriminators.Distinct(StringComparer.OrdinalIgnoreCase),
                typed, "discriminator", "JAML clause", replace);

        if (context.BlockKey is "sources" && context.Discriminator is { } srcDisc)
            return FilterNames(
                JamlSchema.SourceKeysFor(srcDisc) ?? [], typed, "key", $"{srcDisc} source key", replace);

        if (context.BlockKey is "with")
            return FilterNames(JamlClause.WithBlockKeys, typed, "key", "with-block key", replace);

        if (context.Discriminator is { } disc)
            return FilterNames(JamlSchema.ClauseKeysFor(disc), typed, "key", $"{disc} clause key", replace);

        return [];
    }

    private static IReadOnlyList<JamlCompletionItem> FilterNames(
        IEnumerable<string> names, string typed, string kind, string detail, JamlSpan replace)
    {
        var starts = new List<JamlCompletionItem>();
        var contains = new List<JamlCompletionItem>();
        foreach (var name in names)
        {
            if (typed.Length == 0 || name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                starts.Add(new JamlCompletionItem(name, kind, detail, replace));
            else if (name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                contains.Add(new JamlCompletionItem(name, kind, detail, replace));
        }
        starts.AddRange(contains);
        return starts;
    }

    // ── Context detection ───────────────────────────────────────────────────────────────

    private readonly record struct LineContext(
        string? Discriminator,
        string? BlockKey,
        bool InClauseList
    );

    /// <summary>
    /// Walk upward from <paramref name="line"/> collecting the enclosing shape: the nearest
    /// block key at a lower indent (sources:, with:, clauses:), the clause's discriminator,
    /// and whether we're inside a must/should/mustNot list at all.
    /// </summary>
    private static LineContext ContextAt(string[] lines, int line)
    {
        string? discriminator = null;
        string? blockKey = null;
        var inClauseList = false;

        var reference = int.MaxValue;
        if (line >= 0 && line < lines.Length && lines[line].Trim().Length > 0)
            reference = EffectiveIndent(lines[line]);

        for (var i = Math.Min(line, lines.Length - 1); i >= 0; i--)
        {
            var raw = lines[i];
            if (raw.Trim().Length == 0)
                continue;
            var lineIndent = EffectiveIndent(raw);
            if (i != line && lineIndent > reference)
                continue;
            if (i != line)
                reference = lineIndent;

            var body = raw.TrimStart();
            var fromListItem = body.StartsWith("- ", StringComparison.Ordinal);
            if (fromListItem)
                body = body[2..].TrimStart();

            var colon = body.IndexOf(':');
            if (colon <= 0)
            {
                // A one-line clause carries its discriminator in prose instead of a `key:` —
                // "- Perkeo in ante 1" is a joker clause though the word "joker" never appears.
                // Without this, keys indented under a line clause saw no enclosing clause and
                // completed to nothing.
                if (fromListItem && discriminator is null && blockKey is null)
                    discriminator = DiscriminatorForLine(body);
                continue;
            }
            var key = body[..colon].Trim();

            if (discriminator is null && IsDiscriminator(key))
                discriminator = key;
            else if (blockKey is null && discriminator is null
                && (key is "sources" or "with" or "clauses"))
                blockKey = key;

            if (key is "must" or "should" or "mustNot")
            {
                inClauseList = true;
                break;
            }
        }

        return new LineContext(discriminator, blockKey, inClauseList);
    }

    private static int EffectiveIndent(string line)
    {
        var indent = CountIndent(line);
        return line.TrimStart().StartsWith('-') ? indent + 2 : indent;
    }

    /// <summary>
    /// The discriminator a one-line clause stands for. Asks the engine's own line reader
    /// (<see cref="JamlLine.TryToClause"/>) rather than pattern-matching the prose here, so the
    /// completion list can never disagree with what the loader will actually build. Answers only
    /// with a discriminator <see cref="JamlSchema"/> knows, so a rename on either side degrades
    /// to "no completion" instead of to a confidently wrong key list.
    /// </summary>
    private static string? DiscriminatorForLine(string lineBody)
    {
        if (lineBody.Length == 0)
            return null;
        if (!JamlLine.TryToClause(lineBody, out var clause, out _) || clause is null)
            return null;
        return JamlLine.DiscriminatorOf(clause);
    }

    private static bool IsDiscriminator(string word) =>
        JamlSchema.Discriminators.Any(d => d.Equals(word, StringComparison.OrdinalIgnoreCase));

    private static int CountIndent(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
            count++;
        return count;
    }

    private static (string Word, JamlSpan Span) WordAt(string lineText, int line, int character)
    {
        if (character < 0)
            return ("", default);
        var at = Math.Min(character, Math.Max(lineText.Length - 1, 0));
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        if (lineText.Length == 0 || (!IsWordChar(lineText[at]) && (at == 0 || !IsWordChar(lineText[at - 1]))))
            return ("", default);
        if (!IsWordChar(lineText[at]))
            at--;
        var start = at;
        while (start > 0 && IsWordChar(lineText[start - 1]))
            start--;
        var end = at;
        while (end + 1 < lineText.Length && IsWordChar(lineText[end + 1]))
            end++;
        return (lineText[start..(end + 1)], JamlSpan.OnLine(line, start, end + 1 - start));
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n');
}
