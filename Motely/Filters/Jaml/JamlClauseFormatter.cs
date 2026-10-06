using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using VYaml.Parser;

namespace Motely.Filters.Jaml;

/// <summary>
/// The one thing VYaml can't do on its own: turn <c>- joker: Blueprint</c> into a
/// <see cref="JokerClause"/>. First key of the mapping is the wire name (from
/// <see cref="JamlDiscriminatorAttribute"/>), its value goes to the clause's value property,
/// every sibling key is a property set by name.
/// </summary>
// AOT/WASM: every type this formatter reflects over lives in Motely, and Motely ships
// ILLink.Descriptors.xml (embedded) that preserves the whole assembly. The trimmer and
// NativeAOT keep the types, properties and constructors, so the reflection below is safe.
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2077", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
public static class JamlClauseFormatter
{
    // ── wire name → clause type + attribute ──
    private static readonly Dictionary<string, (Type Type, JamlDiscriminatorAttribute Attr)> Wires = BuildWires();

    private static Dictionary<string, (Type, JamlDiscriminatorAttribute)> BuildWires()
    {
        var map = new Dictionary<string, (Type, JamlDiscriminatorAttribute)>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in typeof(IJamlClause).Assembly.GetTypes())
            foreach (var attr in type.GetCustomAttributes<JamlDiscriminatorAttribute>(inherit: false))
                foreach (var wire in attr.Wires)
                    map[wire] = (type, attr);
        return map;
    }

    /// <summary>The rolls a wire fills in when the page writes none (JamlConfigWriter elides them again).</summary>
    internal static int[]? RollsDefaultFor(string wire) =>
        Wires.TryGetValue(wire, out var w) ? w.Attr.RollsDefault : null;

    /// <summary>
    /// Scalar lines for the document being loaded, read off the source (see
    /// <see cref="JamlScalarLines"/>). Set by <see cref="JamlConfigLoader.FromJaml"/> around the
    /// deserialize; null when the formatter runs without it, and then lines come from marks.
    /// </summary>
    [ThreadStatic] internal static JamlScalarLines? Source;

    /// <summary>A must:/should:/mustNot: list. Same result as VYaml's ListFormatter, but it tells
    /// <see cref="Source"/> a list is starting, and without Source each item is read with the mark
    /// from before it, so a scalar item (`- Blueprint`) still reports its own line. An anchored
    /// item is filed in <paramref name="anchors"/> and an alias returns that same instance, as
    /// VYaml's DeserializeWithAlias did.</summary>
    internal static List<IJamlClause>? ReadClauseList(ref YamlParser parser, Dictionary<Anchor, object?> anchors)
    {
        Source?.BeginList();
        if (parser.IsNullScalar())
        {
            Source?.TryTake(null, out _);
            parser.Read();
            return null;
        }
        if (parser.CurrentEventType != ParseEventType.SequenceStart)
            throw Error(PeekLine(parser.CurrentMark.Line), "must:, should: and mustNot: are lists of clauses like `- joker: Blueprint`");

        var prevLine = parser.CurrentMark.Line;
        parser.Read();
        var list = new List<IJamlClause>();
        while (parser.CurrentEventType != ParseEventType.SequenceEnd)
        {
            if (parser.CurrentEventType == ParseEventType.Alias)
            {
                list.Add(JamlConfigLoader.ResolveAlias<IJamlClause>(ref parser, anchors));
                prevLine = parser.CurrentMark.Line;
                continue;
            }
            if (parser.TryGetCurrentAnchor(out var anchor))
            {
                // Entered on the clause's own event: a mapping's start mark is right (see ReadNode).
                prevLine = parser.CurrentMark.Line;
                var anchored = ReadClause(ReadNode(ref parser, ref prevLine));
                anchors[anchor] = anchored;
                list.Add(anchored);
                prevLine = parser.CurrentMark.Line;
                continue;
            }
            list.Add(ReadClause(ReadNode(ref parser, ref prevLine)));
        }
        parser.Read();
        return list;
    }

    // ── YAML events → tiny tree ──

    private sealed class Node
    {
        public int Line;
        public string? Scalar;
        public List<Node>? Items;
        public List<(string Key, Node Value)>? Map;
        /// <summary>The key the page wrote, when the value is bound under a different name
        /// (`joker: X` binds to `Jokers`); errors quote this one.</summary>
        public string? WireKey;
        public bool IsNull => Scalar is null && Items is null && Map is null;
    }

    // Line numbers. VYaml 1.4 has no per-event start mark: CurrentMark is where the tokenizer's
    // reader is, and the tokenizer runs ahead of the parser (to the next key after a plain
    // scalar, past a whole `{ … }` in flow style). So the line of each scalar comes from
    // Source, which read it off the text. Without Source, the fallback is the mark taken one
    // event earlier (the key, the previous `- ` item), which is right for block style: that is
    // `prevLine`, and a mapping value is filed under its key's line.

    /// <summary>The line of the scalar just read with this text.</summary>
    private static int TakeLine(string? value, int fallback) =>
        Source is { } s && s.TryTake(value, out var line) ? line : fallback;

    /// <summary>The line of the next scalar, not yet read. A collection starts on or before its
    /// first scalar's line, and never after its own start mark.</summary>
    private static int PeekLine(int mark) =>
        Source is { } s && s.TryPeek(out var line) ? Math.Min(line, mark) : mark;

    private static Node ReadNode(ref YamlParser parser, ref int prevLine)
    {
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
            {
                var node = new Node();
                var fallback = prevLine;
                prevLine = parser.CurrentMark.Line;
                node.Scalar = parser.IsNullScalar() ? null : parser.ReadScalarAsString();
                if (node.Scalar is null) parser.Read();
                node.Line = TakeLine(node.Scalar, fallback);
                return node;
            }

            case ParseEventType.SequenceStart:
            {
                var node = new Node { Line = PeekLine(parser.CurrentMark.Line), Items = [] };
                prevLine = parser.CurrentMark.Line;
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.SequenceEnd)
                    node.Items.Add(ReadNode(ref parser, ref prevLine));
                prevLine = parser.CurrentMark.Line;
                parser.Read();
                return node;
            }

            case ParseEventType.MappingStart:
            {
                var node = new Node { Line = PeekLine(parser.CurrentMark.Line), Map = [] };
                prevLine = parser.CurrentMark.Line;
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.MappingEnd)
                {
                    // Without Source: a block key's mark sits just past its `:`, on the key's line.
                    var markLine = parser.CurrentMark.Line;
                    prevLine = markLine;
                    var keyText = parser.IsNullScalar() ? null : parser.ReadScalarAsString();
                    if (keyText is null) parser.Read();
                    var key = keyText ?? "";
                    var keyLine = TakeLine(keyText, markLine);
                    var value = ReadNode(ref parser, ref prevLine);
                    value.Line = keyLine;
                    node.Map.Add((key, value));
                }
                prevLine = parser.CurrentMark.Line;
                parser.Read();
                return node;
            }

            default:
                throw Error(prevLine, $"unexpected YAML event {parser.CurrentEventType}");
        }
    }

    // ── tree → clause ──

    private static IJamlClause ReadClause(Node node)
    {
        if (node.Map is null)
            throw Error(node.Line, "a clause must be a mapping like `- joker: Blueprint`");

        // The wire is whichever key is a known discriminator (normally the first).
        var wireIndex = node.Map.FindIndex(kv => Wires.ContainsKey(kv.Key));
        if (wireIndex < 0)
            throw Error(node.Line, $"no recognised discriminator among: {string.Join(", ", node.Map.Select(kv => kv.Key))}");

        var (wire, value) = node.Map[wireIndex];
        var (type, attr) = Wires[wire];
        var clause = (IJamlClause)Activator.CreateInstance(type)!;

        var rest = node.Map.Where((_, i) => i != wireIndex).ToList();

        // Where the bare value goes.
        if (!value.IsNull)
        {
            if (attr.RollsAreInlineValue)
            {
                value.WireKey = wire;
                rest.Insert(0, ("rolls", value));
            }
            else if (clause is LogicClause && value.Map is null)
                rest.Insert(0, ("clauses", value));      // or: [ ...arms ]
            else if (attr.ValueEnum is { } valueEnum)
            {
                // `legendaryJoker:` with an indented `jokers: / edition: / antes:` block under it.
                if (value.Map is not null)
                {
                    // Name the value the block was reaching for (`jokers: [Perkeo]` → Perkeo).
                    var named = value.Map
                        .Select(kv => kv.Value.Scalar ?? kv.Value.Items?.FirstOrDefault()?.Scalar)
                        .FirstOrDefault(v => v is not null && Enum.TryParse(valueEnum, v, ignoreCase: true, out _));
                    throw Error(value.Line,
                        $"`{wire}:` takes a name like `{wire}: {named ?? "<name>"}`, not a block of keys; "
                        + $"write {string.Join(", ", value.Map.Select(kv => kv.Key + ":"))} as sibling keys of the clause");
                }
                value.WireKey = wire;
                rest.Insert(0, (ValueProperty(type, valueEnum).Name, value));
            }
            else if (value.Map is not null)
                rest.InsertRange(0, value.Map);          // or: { mode, score, clauses } / standardCard: { rank: K }
            else
                rest.Insert(0, ("label", value));        // standardCard: Ace  (a label; rank: comes next)
        }

        Populate(clause, rest);

        // An unspecified score is worth 1, not 0. A should clause you bothered to write counts
        // for something; explicit scores (including negative penalties) still win.
        if (!rest.Any(kv => kv.Key.Equals("score", StringComparison.OrdinalIgnoreCase)))
            clause.Score = 1;

        if (attr.RollsDefault is { } rollsDefault && clause is IRollScopedClause r && r.Rolls.Length == 0)
            r.Rolls = rollsDefault;

        return clause;
    }

    private static PropertyInfo ValueProperty(Type type, Type valueEnum) =>
        type.GetProperties().FirstOrDefault(p => p.PropertyType.IsArray && p.PropertyType.GetElementType() == valueEnum)
        ?? type.GetProperties().FirstOrDefault(p => p.PropertyType == valueEnum)
        ?? throw new InvalidOperationException($"{type.Name} has no property of type {valueEnum.Name}");

    private static void Populate(object target, List<(string Key, Node Value)> map)
    {
        var type = target.GetType();
        foreach (var (rawKey, value) in map)
        {
            var key = rawKey switch
            {
                "ante" => "antes",
                "requireMega" => "requireMegaPack",
                _ => rawKey,
            };
            var prop = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null || !prop.CanWrite)
                throw Error(value.Line, $"unknown key '{rawKey}' on {type.Name}");
            prop.SetValue(target, Convert(value, prop.PropertyType, value.WireKey ?? rawKey));
        }
    }

    private static object? Convert(Node node, Type type, string key)
    {
        if (node.IsNull)
            return null;

        if (type.IsArray)
        {
            var elem = type.GetElementType()!;
            if (node.Scalar is not null && node.Scalar.Equals("any", StringComparison.OrdinalIgnoreCase))
                return Array.CreateInstanceFromArrayType(type, 0); // category any
            var items = node.Items ?? [node];                     // scalar → one-element array
            if (elem == typeof(int))
                items = ExpandRanges(items, key);                 // antes: 1-8 / [1..3, 7] / 1 to 8
            var array = Array.CreateInstanceFromArrayType(type, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                var value = Convert(items[i], elem, key);
                if (IsAnteKey(key) && value is int ante && (ante < MinAnte || ante > MaxAnte))
                    throw Error(items[i].Line, $"ante `{ante}` is out of range; antes run {MinAnte}-{MaxAnte} (key `{key}`)");
                array.SetValue(value, i);
            }
            return array;
        }

        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (typeof(IJamlClause).IsAssignableFrom(t))
            return ReadClause(node);

        // A single value where the page wrote a block or a list: say so, rather than quote an
        // empty scalar ("`` is not a MotelyJoker").
        if (t.IsEnum || t == typeof(int) || t == typeof(bool) || t == typeof(string))
        {
            if (node.Map is not null)
                throw Error(node.Line, $"`{key}` takes a single value, not a block of keys ({string.Join(", ", node.Map.Select(kv => kv.Key + ":"))})");
            if (node.Items is not null)
                throw Error(node.Line, $"`{key}` takes a single value here, not a list");
        }

        if (t.IsEnum)
        {
            if (node.Scalar is null || !Enum.TryParse(t, node.Scalar, ignoreCase: true, out var e))
                throw Error(node.Line, $"`{node.Scalar}` is not a {t.Name} (key `{key}`)");
            return e;
        }

        if (t == typeof(int))
            return node.Scalar is not null && int.TryParse(node.Scalar, out var i)
                ? i
                : throw Error(node.Line, $"`{node.Scalar}` is not an integer (key `{key}`)");

        if (t == typeof(bool))
            return node.Scalar is not null && bool.TryParse(node.Scalar, out var b)
                ? b
                : throw Error(node.Line, $"`{node.Scalar}` is not true/false (key `{key}`)");

        if (t == typeof(string))
            return node.Scalar;

        // Nested block: sources:, with:, …
        if (node.Map is null)
            throw Error(node.Line, $"`{key}` must be a block of keys");
        var nested = Activator.CreateInstance(t)!;
        Populate(nested, node.Map);
        return nested;
    }

    /// <summary>
    /// ADR-001 revisit: every <c>int[]</c> key takes range shorthand. A scalar item shaped
    /// <c>1-8</c>, <c>1..8</c> or <c>1 to 8</c> (case-insensitive) expands to the integers it
    /// names, in place, so <c>antes: 1-8</c> and <c>antes: [1..3, 7]</c> both work. Anything
    /// else passes through to the integer branch of <see cref="Convert"/> untouched, so a plain
    /// <c>-1</c> is still just an integer.
    /// </summary>
    private static List<Node> ExpandRanges(List<Node> items, string key)
    {
        List<Node>? expanded = null;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Scalar is null || !TryParseRange(item.Scalar, out var from, out var to))
            {
                expanded?.Add(item);
                continue;
            }
            if (from > to)
                throw Error(item.Line, $"`{item.Scalar}` is a descending range; write `{to}-{from}` (key `{key}`)");
            // Jaml.check runs per keystroke on untrusted text: `1-999999999`, or a thousand
            // `0-1023` items in one list, must be an error, not a billion-node allocation. The cap
            // is on everything the key holds once its ranges are expanded. The widest real key is
            // antes (0-39); source indices and rolls are single digits to low tens.
            long span = (long)to - from + 1;                      // long: 0-2147483647 is 2^31 values
            int before = expanded?.Count ?? i;
            if (span > MaxExpandedLength - before)
                throw Error(item.Line, before == 0
                    ? $"`{item.Scalar}` spans {span} values; ranges cover at most {MaxExpandedLength} values per key (key `{key}`)"
                    : $"`{item.Scalar}` brings the list to {(long)before + span} values; ranges cover at most {MaxExpandedLength} values per key (key `{key}`)");
            expanded ??= [.. items.Take(i)];
            // Count, not `v <= to; v++`: with `to == int.MaxValue` that counter wraps and never stops.
            for (int k = 0; k < (int)span; k++)                   // span <= MaxExpandedLength here
                expanded.Add(new Node { Line = item.Line, Scalar = (from + k).ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        return expanded ?? items;
    }

    internal const int MaxExpandedLength = 1024;

    internal const int MinAnte = 0;
    internal const int MaxAnte = 39;

    private static bool IsAnteKey(string key) =>
        key.Equals("antes", StringComparison.OrdinalIgnoreCase) || key.Equals("ante", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseRange(string scalar, out int from, out int to)
    {
        from = to = 0;
        var s = scalar.AsSpan().Trim();
        int sep, sepLength;
        if ((sep = s.IndexOf("..", StringComparison.Ordinal)) >= 0) sepLength = 2;
        else if ((sep = s.IndexOf(" to ", StringComparison.OrdinalIgnoreCase)) >= 0) sepLength = 4;
        else if ((sep = s.IndexOf('-')) > 0) sepLength = 1;     // > 0: a leading '-' is a sign, not a range
        else return false;
        // NumberStyles.None: ASCII digits only, so neither side can smuggle a sign or a second range.
        return int.TryParse(s[..sep].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out from)
            && int.TryParse(s[(sep + sepLength)..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out to);
    }

    private static InvalidOperationException Error(int line, string message) =>
        new($"JAML line {line}: {message}");
}
