using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Motely.Filters.Jaml;

/// <summary>
/// JAML text → <see cref="JamlConfig"/>. VYaml parses (<see cref="JamlYamlTree"/>); this walks the
/// tree once. Every clause reads only its own keys — <c>and:</c>/<c>or:</c> pass nothing down, and
/// every default (min 1, score 1, antes 1..8) lives on the record or right here at the read.
/// </summary>
public static class JamlConfigLoader
{
    /// <summary>An unspecified score is worth 1, not 0 — a should clause you wrote counts.</summary>
    internal const int DefaultScore = 1;

    // Keys every clause handles here before its desc's Set sees the rest.
    private static readonly HashSet<string> CommonKeys =
        new(StringComparer.OrdinalIgnoreCase) { "min", "max", "score", "label", "ante", "antes", "rolls", "with", "sources" };

    // Keys that only shape a slot list; none opens a source by itself. omenGlobe is a source.
    private static readonly HashSet<string> SlotModifierKeys =
        new(StringComparer.OrdinalIgnoreCase) { "requireMega", "requireMegaPack", "charmTag", "etherealTag" };

    public static bool TryLoad(string content, [NotNullWhen(true)] out JamlConfig? config, out string? error)
    {
        try
        {
            config = FromJaml(content);
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

    /// <summary>Loads a filter document. YAML only; JSON is valid YAML and reads the same way.</summary>
    public static JamlConfig FromJaml(string content)
    {
        var root = JamlYamlTree.Parse(content);
        ValidateKeys(root, JamlConfig.RootKeys, "JAML root");

        var name = GetString(root, "name");
        var config = new JamlConfig
        {
            Id = GetString(root, "id") ?? Normalize(name ?? "unnamed"),
            Name = name,
            Description = GetString(root, "description"),
            Author = GetString(root, "author"),
        };
        if (GetString(root, "deck") is { } deck)
            config.Deck = ParseEnum<MotelyDeck>(deck, root.ValueSpan("deck"));
        if (GetString(root, "stake") is { } stake)
            config.Stake = ParseEnum<MotelyStake>(stake, root.ValueSpan("stake"));
        config.Seeds.AddRange(GetStringArray(root, "seeds") ?? []);

        if (GetString(root, "filter") is { Length: > 0 } filterName)
        {
            if (!MotelyNativeFilterNames.TryParse(filterName, out _))
                throw new JamlSemanticException(
                    $"Unknown native filter '{filterName}'. Valid filters: {string.Join(", ", MotelyNativeFilterNames.DisplayNames)}",
                    root.ValueSpan("filter")
                );
            config.Filter = filterName;
        }

        config.Must.AddRange(ClauseList(root, "must").Select(ParseClause));
        config.Should.AddRange(ClauseList(root, "should").Select(ParseClause));
        config.MustNot.AddRange(ClauseList(root, "mustNot").Select(ParseClause));
        return config;
    }

    // ── clauses ────────────────────────────────────────────────────────────────────────────

    private static IJamlClause ParseClause(JMap node)
    {
        var disc =
            node.Keys.FirstOrDefault(JamlSchema.IsKnownDiscriminator)
            ?? throw new JamlSemanticException(
                $"Clause has no recognised discriminator key. Keys: {string.Join(", ", node.Keys)}.",
                node.Span
            );

        // `joker:` may carry its keys in a nested block; outer keys still apply, inner ones win.
        var allowed = JamlSchema.ClauseKeysFor(disc);
        ValidateKeys(node, [.. allowed, .. JamlSchema.Discriminators], "clause");
        var data = node;
        if (node.Get(disc) is JMap inner)
        {
            ValidateKeys(inner, allowed, $"'{disc}' block");
            data = new JMap { Span = node.Span };
            foreach (var key in node.Keys)
                data.Set(key, node.Get(key)!, node.KeySpan(key));
            foreach (var key in inner.Keys)
                data.Set(key, inner.Get(key)!, inner.KeySpan(key));
        }

        var min = GetInt(data, "min") ?? 1;
        var max = GetInt(data, "max");
        ValidateBounds(min, max, data);
        var score = GetInt(data, "score") ?? DefaultScore;
        var label = GetString(data, "label");

        switch (Normalize(disc))
        {
            case "and":
            case "or":
                LogicClause logic = Normalize(disc) == "and" ? new AndClause() : new OrClause();
                logic.Clauses = ClauseList(data, data.Get("clauses") is JSeq ? "clauses" : disc).Select(ParseClause).ToArray();
                logic.Min = min;
                logic.Max = max;
                logic.Score = score;
                logic.Label = label;
                logic.Mode = ParseMode(data);
                return logic;

            case "erraticrank" when node.Get(disc) is JSeq:
            case "erraticranks":
                // Sugar: erraticRanks: [Ace, King] → or: of single-rank clauses, each with this clause's antes.
                var antes = GetIntArray(data, "antes") ?? GetIntArray(data, "ante");
                return new OrClause
                {
                    Clauses = (GetStringArray(node, disc) ?? throw MissingValue(disc))
                        .Select(v => (IJamlClause)new ErraticRankClause
                        {
                            Rank = ParseRank(v, node.ValueSpan(disc)),
                            Antes = antes ?? [1, 2, 3, 4, 5, 6, 7, 8],
                        })
                        .ToArray(),
                    Min = min,
                    Max = max,
                    Score = score,
                    Label = label,
                };
        }

        var clause = JamlSchema.CreateClause(disc);
        clause.Min = min;
        clause.Max = max;
        clause.Score = score;
        clause.Label = label;

        if (clause is IAnteScopedClause anteScoped
            && (GetIntArray(data, "antes") ?? GetIntArray(data, "ante")) is { } written)
        {
            // Ante 0 is the pre-run shop/pack round; boss, voucher and tag streams begin at ante 1.
            if (clause is BossClause or VoucherClause or TagClause && written.Any(a => a < 1))
                throw new JamlSemanticException(
                    $"'{disc}' has no ante {written.First(a => a < 1)}: bosses, vouchers and tags start at ante 1.",
                    data.ValueSpan(data.Get("antes") is null ? "ante" : "antes")
                );
            anteScoped.Antes = written;
        }

        if (clause is IRollScopedClause rollScoped)
        {
            bool inline = JamlSchema.RollsAreInlineFor(disc);
            rollScoped.Rolls = inline
                ? GetIntArray(node, disc) ?? []
                : GetIntArray(data, "rolls") ?? JamlSchema.RollsDefaultFor(disc) ?? rollScoped.Rolls;
            if (rollScoped.Rolls.Any(r => r < 0))
                throw new JamlSemanticException(
                    $"rolls: {rollScoped.Rolls.First(r => r < 0)} is negative. A roll is a zero-based draw index; the first draw is 0.",
                    inline ? node.ValueSpan(disc) : data.ValueSpan("rolls")
                );
        }

        if (clause is IWithScopedClause withScoped)
            withScoped.With = ParseWith(data);
        ApplySources(clause, data);

        foreach (var key in allowed)
        {
            if (CommonKeys.Contains(key) || data.Get(key) is null)
                continue;
            if (!JamlClauseDescDispatch.TrySet(clause, key, Reader(data, key)))
                throw new JamlSemanticException($"Unknown '{disc}' key: '{key}'.", data.KeySpan(key));
        }

        if (JamlSchema.ValueEnumTypeFor(disc) is not null
            && !JamlClauseDescDispatch.TrySetDiscriminatorValue(clause, Reader(node, disc)))
            throw MissingValue(disc);

        return clause;
    }

    /// <summary>min ≥ 1 (the vector filters look for at least one occurrence); a set max is ≥ min.</summary>
    private static void ValidateBounds(int min, int? max, JMap data)
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

    /// <summary><c>mode: sum|max</c> on <c>or:</c>/<c>and:</c>. Omitted → sum.</summary>
    private static JamlLogicScoreMode ParseMode(JMap data) =>
        GetString(data, "mode") switch
        {
            null or "" => JamlLogicScoreMode.Sum,
            var t when t.Equals("sum", StringComparison.OrdinalIgnoreCase) => JamlLogicScoreMode.Sum,
            var t when t.Equals("max", StringComparison.OrdinalIgnoreCase) => JamlLogicScoreMode.Max,
            var t => throw new JamlSemanticException(
                $"Unknown logic mode '{t}'. Use mode: sum (total all arms) or mode: max (best arm only).",
                data.ValueSpan("mode")
            ),
        };

    // Luck lives under `with: { luck }`.
    private static JamlWith ParseWith(JMap data)
    {
        var result = new JamlWith();
        if (data.Get("with") is not JMap with)
            return result;
        ValidateKeys(with, JamlClause.WithBlockKeys, "with");
        if (GetString(with, "luck") is { } text)
        {
            var span = with.ValueSpan("luck");
            result.Luck = int.TryParse(text.TrimStart('x', 'X'), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                ? Enum.IsDefined((MotelyLuck)n)
                    ? (MotelyLuck)n
                    : throw new JamlSemanticException($"Unsupported luck multiplier: {n}.", span)
                : ParseEnum<MotelyLuck>(text, span);
        }
        return result;
    }

    // ── sources ────────────────────────────────────────────────────────────────────────────

    private static void ApplySources(IJamlClause clause, JMap data)
    {
        switch (clause)
        {
            case JokerClause c: c.Sources = JokerSources(data); break;
            case CommonJokerClause c: c.Sources = JokerSources(data); break;
            case UncommonJokerClause c: c.Sources = JokerSources(data); break;
            case RareJokerClause c: c.Sources = JokerSources(data); break;
            case LegendaryJokerClause c:
                if (SourcesBlock(data, LegendaryJokerSourceConfig.SourceKeys) is { } l)
                    c.Sources = new LegendaryJokerSourceConfig
                    {
                        BoosterPacks = Ints(l, "boosterPacks"),
                        ArcanaPacks = Ints(l, "arcanaPacks"),
                        SpectralPacks = Ints(l, "spectralPacks"),
                        RequireMegaPack = RequireMega(l),
                    };
                break;
            case TarotCardClause c:
                if (SourcesBlock(data, TarotCardSourceConfig.SourceKeys) is { } t)
                    c.Sources = new TarotCardSourceConfig
                    {
                        ShopItems = Ints(t, "shopItems"),
                        BoosterPacks = Ints(t, "boosterPacks"),
                        Emperor = Ints(t, "emperor"),
                        PurpleSealOrEightBall = Ints(t, "purpleSealOrEightBall"),
                        CharmTag = GetBool(t, "charmTag") ?? false,
                        RequireMegaPack = RequireMega(t),
                    };
                break;
            case SpectralCardClause c:
                if (SourcesBlock(data, SpectralCardSourceConfig.SourceKeys) is { } s)
                    c.Sources = new SpectralCardSourceConfig
                    {
                        ShopItems = Ints(s, "shopItems"),
                        BoosterPacks = Ints(s, "boosterPacks"),
                        SixthSense = Ints(s, "sixthSense"),
                        Seance = Ints(s, "seance"),
                        EtherealTag = GetBool(s, "etherealTag") ?? false,
                        RequireMegaPack = RequireMega(s),
                        OmenGlobe = GetBool(s, "omenGlobe") ?? false,
                    };
                break;
            case PlanetCardClause c:
                if (SourcesBlock(data, PlanetSourceConfig.SourceKeys) is { } p)
                    c.Sources = new PlanetSourceConfig
                    {
                        ShopItems = Ints(p, "shopItems"),
                        BoosterPacks = Ints(p, "boosterPacks"),
                        RequireMegaPack = RequireMega(p),
                    };
                break;
            case StandardCardClause c:
                if (SourcesBlock(data, StandardCardSourceConfig.SourceKeys) is { } d)
                    c.Sources = new StandardCardSourceConfig
                    {
                        ShopItems = Ints(d, "shopItems"),
                        BoosterPacks = Ints(d, "boosterPacks"),
                        RequireMegaPack = RequireMega(d),
                    };
                break;
        }
    }

    private static JokerSourceConfig? JokerSources(JMap data) =>
        SourcesBlock(data, JokerSourceConfig.SourceKeys) is not { } b
            ? null
            : new JokerSourceConfig
            {
                ShopItems = Ints(b, "shopItems"),
                BoosterPacks = Ints(b, "boosterPacks"),
                Judgement = Ints(b, "judgement"),
                Wraith = Ints(b, "wraith"),
                RiffRaff = Ints(b, "riffRaff"),
                RareTag = Ints(b, "rareTag"),
                UncommonTag = Ints(b, "uncommonTag"),
                CommonShopJokers = Ints(b, "commonShopJokers"),
                UncommonShopJokers = Ints(b, "uncommonShopJokers"),
                RareShopJokers = Ints(b, "rareShopJokers"),
                AllShopJokers = Ints(b, "allShopJokers"),
                RequireMegaPack = RequireMega(b),
            };

    // `sources: {}` is the match-nowhere override; a block of modifiers alone matches nowhere by accident.
    private static JMap? SourcesBlock(JMap data, string[] sourceKeys)
    {
        if (data.Get("sources") is not JMap block)
            return null;
        ValidateKeys(block, sourceKeys, "source");
        if (block.Keys.Count > 0 && block.Keys.All(SlotModifierKeys.Contains))
            throw new JamlSemanticException(
                $"sources names no slots: {string.Join(", ", block.Keys)} only modifies a slot list. Name the slots to look in (e.g. shopItems, boosterPacks), or write sources: {{}} to match nowhere.",
                data.KeySpan("sources")
            );
        return block;
    }

    private static int[] Ints(JMap block, string key) => GetIntArray(block, key) ?? [];

    private static bool RequireMega(JMap block) =>
        GetBool(block, "requireMegaPack") ?? GetBool(block, "requireMega") ?? false;

    // ── reading values off the tree ────────────────────────────────────────────────────────

    private static void ValidateKeys(JMap map, IEnumerable<string> allowed, string scope)
    {
        foreach (var key in map.Keys)
            if (!allowed.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase)))
                throw new JamlSemanticException($"Unknown {scope} key: '{key}'.", map.KeySpan(key));
    }

    // Every clause-list entry is a mapping; anything else fails loudly, never silently dropped.
    private static IEnumerable<JMap> ClauseList(JMap map, string key)
    {
        if (map.Get(key) is not JSeq seq)
            return [];
        return seq.Items.Select(item => item as JMap
            ?? throw new JamlSemanticException(
                $"Clause list '{key}' has an entry that is not a clause mapping (e.g. '- joker: Blueprint').",
                item.Span
            ));
    }

    private static string? GetString(JMap map, string key) =>
        map.Get(key) is JScalar { IsNull: false } s ? s.Value : null;

    // Blank (`max:`) or absent → null. Present with the wrong shape → positioned error, never a default.
    private static string? Written(JMap map, string key) =>
        map.Get(key) switch
        {
            null or JMap { Keys.Count: 0 } => null,
            JScalar s => string.IsNullOrWhiteSpace(s.Value) ? null : s.Value,
            _ => throw new JamlSemanticException($"'{key}' takes a single value, not a list or a block.", map.ValueSpan(key)),
        };

    private static int? GetInt(JMap map, string key) =>
        Written(map, key) is not { } text ? null
        : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v
        : throw new JamlSemanticException($"'{key}': '{text}' is not a whole number.", map.ValueSpan(key));

    private static bool? GetBool(JMap map, string key) =>
        Written(map, key) is not { } text ? null
        : JamlLoaderValueReader.TryParseBool(text, out var v) ? v
        : throw new JamlSemanticException($"'{key}': '{text}' is not a bool. Write true or false.", map.ValueSpan(key));

    // A block or null under the key reads as absent. Range tokens ("0-39", "1..8") expand inclusively.
    private static int[]? GetIntArray(JMap map, string key)
    {
        var value = map.Get(key);
        if (value is null or JMap or JScalar { IsNull: true })
            return null;
        var tokens = value is JSeq seq
            ? seq.Items.Select(i => i is JScalar { IsNull: false } s ? s.Value : "")
            : [((JScalar)value).Value];
        var list = new List<int>();
        foreach (var token in tokens)
        {
            if (JamlIntRange.TrySplit(token, out int lo, out int hi))
                JamlIntRange.Append(list, lo, hi);
            else if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var one))
                list.Add(one);
            else
                throw new JamlSemanticException(
                    $"'{key}': '{token}' is not a valid integer or range (e.g. '0-39' or '1..8').",
                    map.ValueSpan(key)
                );
        }
        return [.. list];
    }

    private static string[]? GetStringArray(JMap map, string key) =>
        map.Get(key) switch
        {
            JSeq seq => seq.Items.Select(i => i is JScalar { IsNull: false } s ? s.Value : "").ToArray(),
            JScalar { IsNull: false } s => [s.Value],
            _ => null,
        };

    // The node itself is the reader — no copy, and it carries its own span.
    private static JamlLoaderValueReader Reader(JMap map, string key) =>
        new(map.Get(key), map.ValueSpan(key));

    private static Exception MissingValue(string key) =>
        new InvalidOperationException($"'{key}' clause requires a value.");

    // ── scalar grammar shared with the value reader ────────────────────────────────────────

    internal static MotelyStandardcardRank ParseRank(string value, JamlSpan span = default)
    {
        // Pips count up from Two, so the enum order is the table: 2 → Two … 10 → Ten.
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pip))
            return pip is >= 2 and <= 10
                ? MotelyStandardcardRank.Two + (pip - 2)
                : throw new JamlSemanticException($"Unsupported rank pip value: {pip}.", span);

        return value.ToUpperInvariant() switch
        {
            "J" => MotelyStandardcardRank.Jack,
            "Q" => MotelyStandardcardRank.Queen,
            "K" => MotelyStandardcardRank.King,
            "A" => MotelyStandardcardRank.Ace,
            _ => ParseEnum<MotelyStandardcardRank>(value, span),
        };
    }

    internal static T ParseEnum<T>(string value, JamlSpan span = default)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
        || Enum.TryParse(Normalize(value), ignoreCase: true, out parsed)
            ? parsed
            : throw new JamlSemanticException(JamlEnumMessages.CannotParse(value, typeof(T)), span);

    private static string Normalize(string value) =>
        value.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
}
