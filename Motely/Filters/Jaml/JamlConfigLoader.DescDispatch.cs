namespace Motely.Filters.Jaml;

// The sane path. A discriminator's own descriptor builds its clause: it declares its keys and
// assigns them itself, reading each value through a span-carrying reader so a value it rejects
// underlines itself. No switch arm, no Activator, no GetProperty, no parallel Type table — the
// desc is the one description of the clause, the same one the engine executes and the generator
// reads. Families move onto this rail one at a time; ParseClause routes here first and falls back
// to the legacy switch for the ones not yet moved. When the switch is empty, it goes.
public static partial class JamlConfigLoader
{
    private delegate IJamlClause DescBuilder(
        string discriminator, NodeReader node, IReader data,
        int[] antes, int min, int? max, int score, string? label);

    // The one place a migrated family is named. Keyed by the same normalization the switch uses,
    // so `Normalize(discriminator)` looks a family up here before the switch ever sees it.
    private static readonly Dictionary<string, DescBuilder> DescBuilders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["boss"]          = BuildViaDesc<BossFilterDesc, BossClause>,
            ["bosses"]        = BuildViaDesc<BossFilterDesc, BossClause>,
            ["tag"]           = BuildViaDesc<TagFilterDesc, TagClause>,
            ["tags"]          = BuildViaDesc<TagFilterDesc, TagClause>,
            ["smallBlindTag"] = BuildViaDesc<TagFilterDesc, TagClause>,
            ["bigBlindTag"]   = BuildViaDesc<TagFilterDesc, TagClause>,
            ["voucher"]       = BuildViaDesc<VoucherFilterDesc, VoucherClause>,
            ["vouchers"]      = BuildViaDesc<VoucherFilterDesc, VoucherClause>,
            ["erraticSuit"]   = BuildViaDesc<ErraticSuitFilterDesc, ErraticSuitClause>,
            ["erraticSuits"]  = BuildViaDesc<ErraticSuitFilterDesc, ErraticSuitClause>,
            ["startingDraw"]  = BuildViaDesc<StartingDrawFilterDesc, StartingDrawClause>,
        };

    // Common fields come straight off the clause's own interfaces — no desc, no reflection.
    // Everything the clause declared beyond those is the desc's to claim, value by value.
    private static IJamlClause BuildViaDesc<TDesc, TClause>(
        string discriminator, NodeReader node, IReader data,
        int[] antes, int min, int? max, int score, string? label)
        where TDesc : IJamlClauseDesc<TClause>
        where TClause : class, IJamlClause, new()
    {
        var clause = new TClause();
        clause.Label = label;
        clause.Min = min;
        clause.Max = max;
        clause.Score = score;

        if (clause is IAnteScopedClause anteScoped)
            anteScoped.Antes = antes;

        if (clause is IRollScopedClause rollScoped)
            rollScoped.Rolls = JamlSchema.RollsAreInlineFor(discriminator)
                ? node.GetIntArray(discriminator) ?? []
                : data.GetIntArray("rolls") ?? JamlSchema.RollsDefaultFor(discriminator) ?? [];

        // The value the discriminator itself carries: `boss: TheWall`, `boss: [TheWall, TheHook]`.
        if (!TDesc.SetDiscriminatorValue(clause, new NodeValueReader(node, discriminator)))
            throw new JamlSemanticException(
                $"'{discriminator}' did not accept its value.", node.KeySpan(discriminator));

        // Every remaining key the clause declared, handed to the desc to assign. Common fields,
        // the discriminator itself, and nested-clause discriminators are already accounted for.
        foreach (var key in data.Keys)
        {
            if (PopulatorCommonKeys.Contains(key)
                || string.Equals(key, discriminator, StringComparison.OrdinalIgnoreCase)
                || IsDiscriminator(key))
                continue;
            if (!TDesc.Set(clause, key, new NodeValueReader(data, key)))
                throw new JamlSemanticException(
                    $"Unknown '{discriminator}' key: '{key}'.", data.KeySpan(key));
        }

        return clause;
    }

    // One value pulled off the document for a desc to read: the value under <paramref name="key"/>
    // in <paramref name="reader"/>, carrying where it sits so a value the desc rejects underlines
    // itself. A malformed value throws a positioned <see cref="JamlSemanticException"/> — the
    // loader's report-and-abort — so a desc written as `if (!value.TryEnum(out var e)) return
    // false;` still behaves: the throw happens inside Try*, before the bool is ever returned.
    private sealed class NodeValueReader(IReader reader, string key) : IJamlValueReader
    {
        public string Text => reader.GetString(key) ?? "";

        // The value's own span isn't stamped by the parser yet, so point at the key it sits
        // under — the right line and clause today; a precise value span is a later increment.
        public JamlSpan Span => reader.KeySpan(key);

        public bool IsAny => JamlConfigLoader.IsAny(Text);

        public bool TryInt(out int value)
        {
            if (reader.GetInt(key) is not { } parsed)
                throw new JamlSemanticException($"'{key}' must be a whole number.", Span);
            value = parsed;
            return true;
        }

        public bool TryBool(out bool value)
        {
            if (reader.GetBool(key) is not { } parsed)
                throw new JamlSemanticException($"'{key}' must be true or false.", Span);
            value = parsed;
            return true;
        }

        public bool TryIntArray(out int[] value)
        {
            value = reader.GetIntArray(key)
                ?? throw new JamlSemanticException($"'{key}' must be a number or list of numbers.", Span);
            return true;
        }

        public bool TryRank(out MotelyStandardcardRank value)
        {
            try
            {
                value = ParseRank(Text);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                throw new JamlSemanticException(ex.Message, Span);
            }
        }

        public bool TryEnum<TEnum>(out TEnum value) where TEnum : struct, Enum
        {
            value = ParsePositioned<TEnum>(Text);
            return true;
        }

        public bool TryEnumArray<TEnum>(out TEnum[] value) where TEnum : struct, Enum
        {
            var names = reader.GetStringArray(key) ?? [];
            if (names.Length == 0
                || (names.Length == 1 && JamlDisc.IsAnyToken(names[0])))
            {
                value = [];
                return true;
            }

            var parsed = new TEnum[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                if (JamlDisc.IsAnyToken(names[i]))
                    throw new JamlSemanticException(
                        "'Any' is the whole category, not a list member.",
                        Span
                    );
                parsed[i] = ParsePositioned<TEnum>(names[i]);
            }
            value = parsed;
            return true;
        }

        // Reuse the loader's one enum parser, then attach this value's span to whatever it reports.
        private TEnum ParsePositioned<TEnum>(string text) where TEnum : struct, Enum
        {
            try
            {
                return ParseEnum<TEnum>(text);
            }
            catch (InvalidOperationException ex)
            {
                throw new JamlSemanticException(ex.Message, Span);
            }
        }
    }
}
