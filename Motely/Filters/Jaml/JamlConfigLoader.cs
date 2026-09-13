using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Motely.Filters.Jaml;

public static partial class JamlConfigLoader
{
    public static bool TryLoad(
        string content,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    ) => TryLoad(content, JamlLoadFormat.Auto, out config, out error);

    public static bool TryLoad(
        string content,
        JamlLoadFormat format,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    )
    {
        try
        {
            config = From(content, format);
            error = null;
            return true;
        }
        catch (JamlSemanticException ex) when (!ex.Span.IsEmpty)
        {
            config = null;
            error = $"line {ex.Span.StartLine + 1}, col {ex.Span.StartColumn + 1}: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            config = null;
            error = ex.Message;
            return false;
        }
    }

    public static JamlConfig FromJaml(string content) => From(content, JamlLoadFormat.Jaml);

    public static JamlConfig From(string content, JamlLoadFormat format)
    {
        var resolved = format == JamlLoadFormat.Auto ? Sniff(content) : format;
        try
        {
            JMap root = resolved switch
            {
                JamlLoadFormat.Json => JamlForeignTree.ParseJson(content),
                JamlLoadFormat.Yaml => JamlForeignTree.ParseYaml(content),
                _ => JamlDocumentParser.ParseJaml(content),
            };
            return ParseConfig(new NodeReader(root));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{resolved} parse error: {ex.Message}", ex);
        }
    }

    /// <summary>Leading <c>{</c> is JSON. Everything else is JAML (YAML files use FromYaml / .yaml).</summary>
    public static JamlLoadFormat Sniff(string content)
    {
        foreach (char c in content)
        {
            if (char.IsWhiteSpace(c))
                continue;
            return c == '{' ? JamlLoadFormat.Json : JamlLoadFormat.Jaml;
        }
        return JamlLoadFormat.Jaml;
    }

    public static LegendaryJokerSourceConfig CreateLegendaryJokerSources(
        LegendaryJokerSourceConfig? userConfig
    ) => userConfig ?? new LegendaryJokerSourceConfig();

    private static JamlConfig ParseConfig(NodeReader root)
    {
        ValidateKeys(root, JamlConfig.RootKeys, "JAML root");
        var name = root.GetString("name");
        var config = new JamlConfig
        {
            Id = root.GetString("id") ?? Slugify(name ?? "unnamed"),
            Name = name,
            Description = root.GetString("description"),
            Author = root.GetString("author"),
        };

        if (root.GetString("deck") is { } deck)
            config.Deck = ParseEnum<MotelyDeck>(deck, root.ValueSpan("deck"));
        if (root.GetString("stake") is { } stake)
            config.Stake = ParseEnum<MotelyStake>(stake, root.ValueSpan("stake"));

        config.Seeds.AddRange(root.GetStringArray("seeds") ?? []);
        if (root.GetString("filter") is { Length: > 0 } filterName)
        {
            if (!MotelyNativeFilterNames.TryParse(filterName, out _))
            {
                string valid = string.Join(", ", MotelyNativeFilterNames.DisplayNames);
                throw new JamlSemanticException(
                    $"Unknown native filter '{filterName}'. Valid filters: {valid}",
                    root.ValueSpan("filter")
                );
            }
            config.Filter = filterName;
        }
        config.Must.AddRange(ParseClauseList(root, "must"));
        config.Should.AddRange(ParseClauseList(root, "should"));
        config.MustNot.AddRange(ParseClauseList(root, "mustNot"));
        return config;
    }

    private static IEnumerable<IJamlClause> ParseClauseList(NodeReader root, string key)
    {
        foreach (var item in root.GetClauseList(key) ?? [])
            yield return ParseClauseSource(item);
    }

    // A clause in a list is either a structured mapping (joker: …) or a single-line JAML clause
    // ("Eternal Blueprint in antes 1 or 2"), turned into a real clause through the engine's own
    // line converter off MotelyItem identity — no second grammar.
    private static IJamlClause ParseClauseSource(ClauseSource source) =>
        (source.Line, source.Mapping) switch
        {
            ({ } line, null) => ParseLineClause(line),
            ({ } line, { } keys) => ApplyKeys(ParseLineClause(line), keys),
            _ => ParseClause(source.Mapping!),
        };

    /// <summary>
    /// Applies continuation keys to a clause the line converter already built, so the terse
    /// spelling reaches every key its family owns ("- Negative Perkeo" then "    ante: 0").
    /// Common keys land directly; the rest go through the clause's own desc, the same rail the
    /// structured spelling uses.
    /// </summary>
    private static IJamlClause ApplyKeys(IJamlClause clause, NodeReader keys)
    {
        foreach (var key in keys.Keys)
        {
            if (key == JamlDocumentParser.TerseLineKey)
                continue;

            switch (key)
            {
                case "ante" or "antes":
                    if (clause is IAnteScopedClause anteScoped)
                        anteScoped.Antes = keys.GetIntArray(key) ?? anteScoped.Antes;
                    else
                        throw new InvalidOperationException(
                            $"'{key}' is not a key of this clause: it is not ante-scoped."
                        );
                    break;
                case "min":
                    clause.Min = keys.GetInt(key) ?? clause.Min;
                    break;
                case "max":
                    clause.Max = keys.GetInt(key) ?? clause.Max;
                    break;
                case "score":
                    clause.Score = keys.GetInt(key) ?? clause.Score;
                    break;
                case "label":
                    clause.Label = keys.GetString(key) ?? clause.Label;
                    break;
                default:
                    if (
                        !JamlClauseDescDispatch.TrySet(
                            clause,
                            key,
                            JamlLoaderValueReader.FromScalar(
                                keys.GetString(key),
                                keys.ValueSpan(key)
                            )
                        )
                    )
                        throw new InvalidOperationException(
                            $"Unknown key '{key}' for a one-line clause of type {clause.GetType().Name}."
                        );
                    break;
            }
        }
        ValidateBounds(clause.Min, clause.Max, keys);
        return clause;
    }

    /// <summary>
    /// The occurrence window every clause carries, checked once, at the token, so the LSP can
    /// underline it. <c>min</c> is at least 1: every SIMD arm looks for at least one occurrence
    /// (each asserts <c>Min &gt; 0</c> as a loader guarantee), and there is no absence filter yet.
    /// A set <c>max</c> is a ceiling at every value, so it cannot sit below <c>min</c>; in
    /// particular <c>max: 0</c> would mean "never appears", which the engine cannot search for.
    /// </summary>
    private static void ValidateBounds(int min, int? max, IReader data)
    {
        if (min < 1)
            throw new JamlSemanticException(
                $"min: {min} must be at least 1. The vector filters look for at least one occurrence; there is no absence filter yet.",
                data.ValueSpan("min")
            );
        if (max is { } m && m < min)
            throw new JamlSemanticException(
                m == 0
                    ? "max: 0 would mean 'never appears', which needs min: 0, and the vector filters cannot search for absence yet. Drop max, or set it to 1 or more."
                    : $"max: {m} is below min: {min}. No seed can match.",
                data.ValueSpan("max")
            );
    }

    /// <summary>
    /// An unspecified score is worth 1, not 0 — a should clause you bothered to write should
    /// count for something. Explicit scores (including negative penalties) still win; this only
    /// fills the blank. Defaulting to 0 silently made unscored should clauses contribute nothing,
    /// the bug that zeroed whole filters for ~10 months. Both spellings fill from here: the block
    /// mapping in <see cref="ParseClause"/> and the one-line form in
    /// <see cref="JamlLine.TryToClause"/>, so "- Blueprint in ante 1" and
    /// "- joker: Blueprint / ante: 1" load to the same clause and the writer elides the same value.
    /// </summary>
    internal const int DefaultScore = 1;

    private static IJamlClause ParseLineClause(string line)
    {
        if (!JamlLine.TryToClause(line, out var clause, out var error))
            throw new InvalidOperationException($"Invalid JAML line '{line}': {error}");
        return clause!;
    }

    private static IJamlClause ParseClause(NodeReader node)
    {
        var discriminator =
            FindDiscriminator(node)
            ?? throw new InvalidOperationException(
                $"Clause has no recognised discriminator key. Keys: {string.Join(", ", node.Keys)}."
            );

        var value = node.GetObject(discriminator);
        ValidateClauseKeys(discriminator, node, value);
        IReader data = value is null ? node : new OverlayReader(value, node);
        var antes = data.GetIntArray("antes") ?? data.GetIntArray("ante") ?? [];
        var min = data.GetInt("min") ?? 1;
        var max = data.GetInt("max");
        ValidateBounds(min, max, data);
        var score = data.GetInt("score") ?? DefaultScore;
        var label = data.GetString("label");

        // One construction path for every desc family. Logic + the multi-rank erratic
        // sugar stay special; everything else is Populate → optional disc value.
        switch (Normalize(discriminator))
        {
            case "and":
                return ParseLogic(
                    new AndClause(),
                    data,
                    discriminator,
                    antes,
                    min,
                    max,
                    score,
                    label
                );
            case "or":
                return ParseLogic(
                    new OrClause(),
                    data,
                    discriminator,
                    antes,
                    min,
                    max,
                    score,
                    label
                );
            case "erraticrank"
                when node.GetString(discriminator) is null
                    && node.GetStringArray(discriminator) is not null:
            case "erraticranks":
                // Sugar: erraticRanks: [Ace, King] → Or of singular ErraticRank clauses.
                // A scalar-valued erraticRank uses the normal Populate path; an array on the
                // singular wire is the same sugar (it used to slip past both and load a
                // corrupt Rank the writer then emitted as a bare numeral).
                return WithMax(
                    new OrClause
                    {
                        Clauses = ParseStringArray(node, discriminator)
                            .Select(v =>
                                (IJamlClause)
                                    new ErraticRankClause
                                    {
                                        Rank = ParseRank(v, node.ValueSpan(discriminator)),
                                        Antes = antes,
                                        Min = 1,
                                    }
                            )
                            .ToArray(),
                        // How many of the listed erratic ranks must appear (child Min=1 is
                        // per-rank: each just needs one occurrence). Honor an explicit min:.
                        Min = min,
                        Score = score,
                        Label = label,
                    },
                    max
                );
            default:
                return PopulateDescClause(
                    discriminator,
                    node,
                    data,
                    antes,
                    min,
                    max,
                    score,
                    label
                );
        }
    }

    /// <summary>
    /// Single door for every <c>[JamlDiscriminator]</c> family: construct via
    /// <see cref="Populate"/>, then apply the bare disc value when the schema says the
    /// wire owns a value enum (<c>joker: Blueprint</c>). Property-shaped families
    /// (standardCard, startingDraw) and roll-inline events skip that step — keys / rolls
    /// already filled the clause.
    /// </summary>
    private static IJamlClause PopulateDescClause(
        string discriminator,
        NodeReader node,
        IReader data,
        int[] antes,
        int min,
        int? max,
        int score,
        string? label
    )
    {
        var clause = Populate(discriminator, node, data, antes, min, max, score, label);

        // ValueEnum on the attribute is the one true signal that the disc carries payload.
        // Rolls-inline events put ints under the disc key; Populate already ate those.
        if (JamlSchema.ValueEnumTypeFor(discriminator) is not null)
        {
            var discReader = DiscriminatorValueReader(node, discriminator);
            if (!JamlClauseDescDispatch.TrySetDiscriminatorValue(clause, discReader))
                throw new InvalidOperationException($"'{discriminator}' clause requires a value.");
        }

        return clause;
    }

    private static IJamlClause ParseLogic(
        LogicClause clause,
        IReader data,
        string discriminator,
        int[] antes,
        int min,
        int? max,
        int score,
        string? label
    )
    {
        var sources = data.GetClauseList("clauses") ?? data.GetClauseList(discriminator) ?? [];
        var children = sources.Select(ParseClauseSource).ToArray();
        clause.Antes = antes;
        HoistAntes(children, antes);
        clause.Clauses = children;
        clause.Min = min;
        clause.Max = max;
        clause.Score = score;
        clause.Label = label;
        clause.Mode = ParseLogicScoreMode(data);
        return clause;
    }

    /// <summary>
    /// <c>mode: sum|max</c> on <c>or:</c>/<c>and:</c>. Omitted → sum (historic or aggregate).
    /// </summary>
    private static JamlLogicScoreMode ParseLogicScoreMode(IReader data)
    {
        var text = data.GetString("mode");
        if (text is null || text.Length == 0)
            return JamlLogicScoreMode.Sum;

        if (text.Equals("sum", StringComparison.OrdinalIgnoreCase))
            return JamlLogicScoreMode.Sum;
        if (text.Equals("max", StringComparison.OrdinalIgnoreCase))
            return JamlLogicScoreMode.Max;

        throw new JamlSemanticException(
            $"Unknown logic mode '{text}'. Use mode: sum (total all arms) or mode: max (best arm only).",
            data.ValueSpan("mode")
        );
    }

    /// <summary>
    /// Parent <paramref name="antes"/> pass into every nested arm with empty antes, including
    /// nested <c>and:</c>/<c>or:</c> (re-applied down the tree). A child that set its own
    /// <c>antes</c> keeps them and blocks further hoist into that subtree only when it is
    /// ante-scoped; nested logic always continues hoist with the same parent scope so bare
    /// grandchildren still inherit unless they override.
    /// </summary>
    internal static void HoistAntes(IJamlClause[] clauses, int[] antes)
    {
        if (antes.Length == 0)
            return;
        foreach (var clause in clauses)
        {
            if (clause is IAnteScopedClause anteScoped)
            {
                if (anteScoped.Antes.Length == 0)
                    anteScoped.Antes = antes;
                // override or filled — do not push parent past an explicit leaf scope
            }
            else if (clause is LogicClause logic)
            {
                // Nested logic: if it has no own parent antes, inherit; then hoist into its arms.
                if (logic.Antes.Length == 0)
                    logic.Antes = antes;
                HoistAntes(logic.Clauses, logic.Antes.Length > 0 ? logic.Antes : antes);
            }
        }
    }

    // Allowed keys = FilterDesc.ClauseKeys (via generated JamlSchema) plus every wire
    // discriminator so the outer map may carry the disc key itself.
    private static void ValidateClauseKeys(string discriminator, IReader outer, IReader? inner)
    {
        var allowed = JamlSchema.ClauseKeysFor(discriminator);
        ValidateKeys(outer, [.. allowed, .. JamlSchema.Discriminators], "clause");
        if (inner != null)
            ValidateKeys(inner, allowed, $"'{discriminator}' block");
    }

    private static void ValidateKeys(IReader reader, IEnumerable<string> allowed, string scope)
    {
        foreach (var key in reader.Keys)
        {
            if (!allowed.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase)))
                // The parser tracked where this key sits; hand that span to the diagnostic
                // rather than making an editor rediscover the typo by string-searching.
                throw new JamlSemanticException($"Unknown {scope} key: '{key}'.", reader.KeySpan(key));
        }
    }

    // Luck/vouchers live under `with: { luck, vouchers }`. Bare `luck:`/`vouchers:` and
    // `sources: {luck}` are unknown keys and die in ValidateClauseKeys before this runs.
    private static JamlWith ParseWith(IReader data)
    {
        var with = data.GetObject("with");
        if (with == null)
            return new JamlWith();

        ValidateKeys(with, JamlClause.WithBlockKeys, "with");
        var result = new JamlWith();
        if (with.GetString("luck") is { } luckText)
            result.Luck = ParseLuck(luckText, with.ValueSpan("luck"));
        else if (with.GetInt("luck") is { } luckInt)
            result.Luck = ParseLuck(luckInt, with.ValueSpan("luck"));
        if (with.GetStringArray("vouchers") is { } vouchers)
            result.Vouchers = vouchers
                .Select(v => ParseEnum<MotelyVoucher>(v, with.ValueSpan("vouchers")))
                .ToArray();
        return result;
    }

    private static TClause WithMax<TClause>(TClause clause, int? max)
        where TClause : IJamlClause
    {
        clause.Max = max;
        return clause;
    }

    private static TEnum[] ParseEnumArray<TEnum>(NodeReader node, string key)
        where TEnum : struct, Enum
    {
        var span = node.ValueSpan(key);
        return ParseStringArray(node, key).Select(v => ParseEnum<TEnum>(v, span)).ToArray();
    }

    private static TEnum[] ParseEnumArray<TEnum>(IReader node, string key, bool allowMissing)
        where TEnum : struct, Enum
    {
        var values = node.GetStringArray(key);
        if (values is null)
            return allowMissing ? [] : throw MissingValue(key);
        var span = node.ValueSpan(key);
        return values.Select(v => ParseEnum<TEnum>(v, span)).ToArray();
    }

    private static string[] ParseStringArray(NodeReader node, string key) =>
        node.GetStringArray(key) ?? throw MissingValue(key);

    private static string? ScalarValue(NodeReader node, string key) => node.GetString(key);

    private static Exception MissingValue(string key) =>
        new InvalidOperationException($"'{key}' clause requires a value.");

    private static string? FindDiscriminator(IReader node)
    {
        foreach (var key in node.Keys)
        {
            if (IsDiscriminator(key))
                return key;
        }
        return null;
    }

    // Generated JamlSchema is the one wire list — a hand copy here is how plurals once
    // silently stopped parsing.
    private static bool IsDiscriminator(string key) =>
        JamlSchema.IsKnownDiscriminator(key);

    private static MotelyStandardcardRank? ParseOptionalRank(string? value, JamlSpan span = default) =>
        value is null ? null : ParseRank(value, span);

    internal static MotelyStandardcardRank ParseRank(string value, JamlSpan span = default)
    {
        // Pips count up from Two, so the enum's own order is the table: 2 → Two … 10 → Ten.
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pip))
        {
            if (pip is >= 2 and <= 10)
                return MotelyStandardcardRank.Two + (pip - 2);
            throw new JamlSemanticException($"Unsupported rank pip value: {pip}.", span);
        }

        // Card shorthand, the one thing the enum names cannot spell.
        return value.ToUpperInvariant() switch
        {
            "J" => MotelyStandardcardRank.Jack,
            "Q" => MotelyStandardcardRank.Queen,
            "K" => MotelyStandardcardRank.King,
            "A" => MotelyStandardcardRank.Ace,
            _ => ParseEnum<MotelyStandardcardRank>(value, span),
        };
    }

    private static T? ParseOptionalEnum<T>(string? value, JamlSpan span = default)
        where T : struct, Enum => value is null ? null : ParseEnum<T>(value, span);

    internal static T ParseEnum<T>(string value, JamlSpan span = default)
        where T : struct, Enum
    {
        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
            return parsed;

        var normalized = value
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);
        if (Enum.TryParse<T>(normalized, ignoreCase: true, out parsed))
            return parsed;

        throw new JamlSemanticException(JamlEnumMessages.CannotParse(value, typeof(T)), span);
    }

    private static MotelyLuck ParseLuck(string value, JamlSpan span = default)
    {
        if (
            int.TryParse(
                value.TrimStart('x', 'X'),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var numeric
            )
        )
            return ParseLuck(numeric, span);
        return ParseEnum<MotelyLuck>(value, span);
    }

    // MotelyLuck's backing value IS the multiplier, so the enum is the whole table —
    // a new Oops tier lands in JAML the moment it lands in the enum.
    private static MotelyLuck ParseLuck(int value, JamlSpan span = default) =>
        Enum.IsDefined((MotelyLuck)value)
            ? (MotelyLuck)value
            : throw new JamlSemanticException($"Unsupported luck multiplier: {value}.", span);

    private static bool IsAny(string value) => JamlDisc.IsAnyToken(value);

    private static string Normalize(string value) =>
        value
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string Slugify(string name) => Normalize(name);

    // One entry in a clause list: a structured mapping, or a single-line JAML clause.
    private readonly record struct ClauseSource(NodeReader? Mapping, string? Line);

    private interface IReader
    {
        IReadOnlyList<string> Keys { get; }
        JamlSpan KeySpan(string key);
        JamlSpan ValueSpan(string key);
        string? GetString(string key);
        int? GetInt(string key);
        bool? GetBool(string key);
        int[]? GetIntArray(string key);
        string[]? GetStringArray(string key);
        IReader? GetObject(string key);
        IReadOnlyList<NodeReader>? GetObjectList(string key);
        IReadOnlyList<ClauseSource>? GetClauseList(string key);
    }

    private sealed class OverlayReader(IReader primary, IReader fallback) : IReader
    {
        public IReadOnlyList<string> Keys =>
            primary.Keys.Concat(fallback.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // The key belongs to whichever reader actually wrote it down; prefer the primary's span
        // and fall back only when the primary never saw the key (empty span).
        public JamlSpan KeySpan(string key) =>
            primary.KeySpan(key) is { IsEmpty: false } span ? span : fallback.KeySpan(key);

        public JamlSpan ValueSpan(string key) =>
            primary.ValueSpan(key) is { IsEmpty: false } span ? span : fallback.ValueSpan(key);

        public string? GetString(string key) => primary.GetString(key) ?? fallback.GetString(key);

        public int? GetInt(string key) => primary.GetInt(key) ?? fallback.GetInt(key);

        public bool? GetBool(string key) => primary.GetBool(key) ?? fallback.GetBool(key);

        public int[]? GetIntArray(string key) =>
            primary.GetIntArray(key) ?? fallback.GetIntArray(key);

        public string[]? GetStringArray(string key) =>
            primary.GetStringArray(key) ?? fallback.GetStringArray(key);

        public IReader? GetObject(string key) => primary.GetObject(key) ?? fallback.GetObject(key);

        public IReadOnlyList<NodeReader>? GetObjectList(string key) =>
            primary.GetObjectList(key) ?? fallback.GetObjectList(key);

        public IReadOnlyList<ClauseSource>? GetClauseList(string key) =>
            primary.GetClauseList(key) ?? fallback.GetClauseList(key);
    }

    // Backed by JAML's own tree (JMap/JSeq/JScalar from JamlDocumentParser).
    private sealed class NodeReader : IReader
    {
        private readonly JMap _map;

        public NodeReader(JMap map) => _map = map;

        public IReadOnlyList<string> Keys => _map.Keys;

        public JamlSpan KeySpan(string key) => _map.KeySpan(key);

        /// <summary>Value token span when stamped; otherwise the key span (same line for inline values).</summary>
        public JamlSpan ValueSpan(string key)
        {
            var value = _map.ValueSpan(key);
            if (!value.IsEmpty)
                return value;
            return _map.KeySpan(key);
        }

        public string? GetString(string key) => Scalar(_map.Get(key));

        public int? GetInt(string key) =>
            int.TryParse(
                GetString(key),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value
            )
                ? value
                : null;

        public bool? GetBool(string key) =>
            bool.TryParse(GetString(key), out var value) ? value : null;

        public int[]? GetIntArray(string key)
        {
            var value = _map.Get(key);
            if (value is null)
                return null;
            if (value is JSeq sequence)
                return sequence.Items
                    .SelectMany(item => ParseIntOrRange(Scalar(item) ?? "", key))
                    .ToArray();
            if (Scalar(value) is { } scalar)
                return ParseIntOrRange(scalar, key).ToArray();
            return null;
        }

        // A range token ("0-39", "1..8", "3–6", ascending or descending) expands inclusively through
        // JamlLine.TrySplitRange, the same grammar the one-liner syntax reads. Plain "N" is one value.
        private static IEnumerable<int> ParseIntOrRange(string token, string key)
        {
            if (JamlLine.TrySplitRange(token, out int lo, out int hi))
            {
                var values = new List<int>(Math.Abs(hi - lo) + 1);
                JamlLine.AppendRange(values, lo, hi);
                return values;
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var single))
                return [single];

            throw new InvalidOperationException($"'{key}': '{token}' is not a valid integer or range (e.g. '0-39' or '1..8').");
        }

        public string[]? GetStringArray(string key)
        {
            var value = _map.Get(key);
            if (value is null)
                return null;
            if (value is JSeq sequence)
                return sequence.Items.Select(item => Scalar(item) ?? "").ToArray();
            if (Scalar(value) is { } scalar)
                return [scalar];
            return null;
        }

        public IReader? GetObject(string key) =>
            _map.Get(key) is JMap map ? new NodeReader(map) : null;

        public IReadOnlyList<NodeReader>? GetObjectList(string key)
        {
            var value = _map.Get(key);
            if (value is null)
                return null;
            if (value is JSeq sequence)
                return sequence.Items
                    .OfType<JMap>()
                    .Select(static item => new NodeReader(item))
                    .ToArray();
            return null;
        }

        // A clause-list entry is either a mapping (structured clause) or a scalar (a single-line
        // JAML clause). Anything else fails loudly — the loader never silently drops a list entry.
        public IReadOnlyList<ClauseSource>? GetClauseList(string key)
        {
            if (_map.Get(key) is not JSeq sequence)
                return null;
            var items = new List<ClauseSource>();
            foreach (var element in sequence.Items)
            {
                switch (element)
                {
                    case JMap map:
                        // A terse line with continuation keys arrives as a mapping carrying the
                        // line under TerseLineKey; both halves travel so the clause is built from
                        // JamlLine and then the keys are applied on top.
                        items.Add(
                            new ClauseSource(
                                new NodeReader(map),
                                (map.Get(JamlDocumentParser.TerseLineKey) as JScalar)?.Value
                            )
                        );
                        break;
                    case JScalar { Value: { } raw }:
                        items.Add(new ClauseSource(null, raw));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Clause list '{key}' has an entry that is neither a clause mapping nor a one-line clause."
                        );
                }
            }
            return items;
        }

        private static string? Scalar(JNode? element) =>
            element switch
            {
                JScalar value => value.Value,
                _ => null,
            };
    }
}
