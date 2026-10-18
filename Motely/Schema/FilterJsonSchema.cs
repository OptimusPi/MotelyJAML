using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely.Schema;

/// <summary>
/// JSON Schema (draft-07) for a Motely filter document, built from <see cref="FilterVocabulary"/>
/// — no checked-in key lists, no hand-synced enums. Every accepted key is here because the
/// engine binds it, and no rejected key is, because the engine would reject it:
/// <c>additionalProperties: false</c> at every level, exactly like the loader. Matching the
/// loader also means matching its leniency: enum values compare case-insensitively
/// (<c>Enum.Parse(ignoreCase: true)</c>), the <c>any</c>/<c>~</c>/<c>null</c> category words are
/// case-insensitive, and any JAML scalar becomes a string where the loader stores a string.
/// </summary>
public static class FilterJsonSchema
{
    private static readonly INamingConvention Naming = CamelCaseNamingConvention.Instance;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The words a converter reads as "the whole category" — in any case.</summary>
    private const string CategoryAnyPattern = "^(?:[aA][nN][yY]|~|[nN][uU][lL][lL])$";

    /// <summary>Per-enum case-insensitive alternation, computed once per enum type.</summary>
    private static readonly ConcurrentDictionary<Type, string> ValuePatterns = new();

    /// <summary>The schema document, indented, ready to write to disk.</summary>
    public static string Generate() => Build().ToJsonString(WriteOptions) + "\n";

    public static JsonObject Build()
    {
        var defs = new JsonObject { ["enums"] = new JsonObject() };

        // Nested blocks first, so clause types and their refs already have somewhere to land.
        foreach (var type in NestedObjectTypes())
            EnsureDef(defs, type);

        // One definition per clause type, one anyOf entry per discriminator key.
        var discEntries = new JsonArray();
        foreach (var group in FilterVocabulary.DiscriminatorTable.GroupBy(d => d.ClauseType))
        {
            var typeName = EnsureTypeDef(defs, group.Key);
            foreach (var disc in group)
                discEntries.Add(new JsonObject
                {
                    ["allOf"] = new JsonArray(
                        new JsonObject { ["$ref"] = $"#/definitions/{typeName}" },
                        new JsonObject { ["required"] = new JsonArray(disc.Key) }),
                });
        }
        defs["clause"] = new JsonObject { ["anyOf"] = discEntries };

        var properties = new JsonObject();
        foreach (var key in FilterVocabulary.RootKeys)
        {
            var prop = typeof(JamlConfig)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .First(p => Naming.Apply(p.Name).Equals(key, StringComparison.OrdinalIgnoreCase));
            properties[key] = PropertySchema(prop, defs);
        }

        return new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["title"] = "Motely Filter",
            ["description"] =
                "A Motely seed filter. Generated from the engine's own clause types — "
                + "keys, enums, and nesting are exactly what the loader binds.",
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["definitions"] = defs,
        };
    }

    // ── Property schemas ───────────────────────────────────────────────────────────────

    private static JsonNode PropertySchema(PropertyInfo prop, JsonObject defs)
    {
        var alternatives = new List<JsonNode>();
        var pt = prop.PropertyType;

        if (FilterVocabulary.SequenceElement(pt) is { } elem)
        {
            alternatives.Add(
                elem switch
                {
                    // Scalar-or-list shorthand: `jokers: Jolly`, `jokers: [Jolly, Sly]`, `jokers: any`.
                    { IsEnum: true } e => new JsonObject
                    {
                        ["anyOf"] = new JsonArray(
                            EnumValueWithCategory(defs, e),
                            ArraySchema(EnumValue(defs, e))
                        ),
                    },
                    var x when x == typeof(IMotelyClause) => new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["$ref"] = "#/definitions/clause" },
                    },
                    var x when x == typeof(string) => ArraySchema(ScalarString()),
                    var x when x == typeof(int) => ArraySchema(new JsonObject { ["type"] = "integer" }),
                    // A list of blocks (soulOrder: [{ legendary: Perkeo }, …]): the loader binds a sequence.
                    _ => ArraySchema((JsonObject)EnsureDef(defs, elem)),
                }
            );
        }
        else
        {
            switch (Unwrap(pt))
            {
                case { IsEnum: true } scalarEnum:
                    alternatives.Add(EnumValue(defs, scalarEnum));
                    break;
                case var t when t == typeof(string):
                    alternatives.Add(ScalarString());
                    break;
                case var t when t == typeof(int):
                    alternatives.Add(new JsonObject { ["type"] = "integer" });
                    break;
                case var t when t == typeof(bool):
                    alternatives.Add(new JsonObject { ["type"] = "boolean" });
                    break;
                case var t when t == typeof(JamlEventRolls):
                    // luckyMoney: 3 → one roll; [1, 2] → those rolls; any/~/null/empty → every roll.
                    alternatives.Add(new JsonObject { ["type"] = "integer" });
                    alternatives.Add(ArraySchema(new JsonObject { ["type"] = "integer" }));
                    alternatives.Add(CategoryAnySchema());
                    break;
                case var t when t == typeof(JamlStandardCardSlot):
                    // standardCard: a whole card name (any case), or a { rank, suit, … } block.
                    alternatives.Add(EnumValueWithCategory(defs, typeof(MotelyStandardCard)));
                    alternatives.Add(EnsureDef(defs, typeof(JamlStandardCardSlot)));
                    break;
                case var t when t == typeof(AndPayload) || t == typeof(OrPayload):
                    alternatives.Add(
                        new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["$ref"] = "#/definitions/clause" },
                        }
                    );
                    alternatives.Add(EnsureDef(defs, t));
                    break;
                case var t when t is not null && IsNestedObject(t):
                    alternatives.Add(EnsureDef(defs, t));
                    break;
                default:
                    alternatives.Add(new JsonObject());
                    break;
            }
        }

        if (AllowsNull(prop))
            alternatives.Add(new JsonObject { ["type"] = "null" });

        if (alternatives.Count == 1)
            return alternatives[0];
        return new JsonObject { ["anyOf"] = new JsonArray([.. alternatives]) };
    }

    /// <summary>
    /// One shared definition per enum type under <c>definitions/enums</c>: the exact-name list
    /// (what editors complete from) and the same names as a case-insensitive pattern, the forms
    /// the loader binds. A number is refused (<see cref="Filters.JamlEnumName"/>): read as the
    /// enum's value it made <c>erraticRank: 2</c> Four. An enum whose number is its meaning
    /// (<c>luck: 4</c> is X4) also takes its values. Referenced by $ref so the names are stored
    /// once instead of per property.
    /// </summary>
    private static JsonObject EnumValue(JsonObject defs, Type enumType)
    {
        var enums = (JsonObject)defs["enums"]!;
        if (!enums.ContainsKey(enumType.Name))
        {
            var names = new JsonArray([.. Enum.GetNames(enumType).Select(n => (JsonNode)n)]);
            var forms = new JsonArray(
                new JsonObject { ["type"] = "string", ["enum"] = names },
                new JsonObject { ["type"] = "string", ["pattern"] = ValuePattern(enumType) }
            );
            if (Filters.JamlEnumName.NumberIsValue(enumType))
                forms.Add(new JsonObject
                {
                    ["type"] = "integer",
                    ["enum"] = new JsonArray([.. Enum.GetValuesAsUnderlyingType(enumType).Cast<int>().Select(v => (JsonNode)v)]),
                });
            enums[enumType.Name] = new JsonObject { ["anyOf"] = forms };
        }
        return new JsonObject { ["$ref"] = $"#/definitions/enums/{enumType.Name}" };
    }

    /// <summary>An enum value, plus the category words the converters read — <c>any</c>/<c>~</c>/<c>null</c> in any case.</summary>
    private static JsonObject EnumValueWithCategory(JsonObject defs, Type enumType) =>
        new JsonObject
        {
            ["anyOf"] = new JsonArray(
                EnumValue(defs, enumType),
                CategoryAnySchema()
            ),
        };

    private static JsonObject CategoryAnySchema() =>
        new() { ["type"] = "string", ["pattern"] = CategoryAnyPattern };

    /// <summary>Any JAML scalar becomes a string where the loader stores one (`id: 12345`).</summary>
    private static JsonObject ScalarString() => new() { ["type"] = new JsonArray("string", "number", "boolean") };

    private static JsonObject ArraySchema(JsonObject items) => new() { ["type"] = "array", ["items"] = items };

    /// <summary>^(?:[Cc]haos[Tt]he[Cc]lown|…)$ — every letter in both cases.</summary>
    private static string ValuePattern(Type enumType) =>
        ValuePatterns.GetOrAdd(enumType, static type =>
            "^(?:" + string.Join("|", Enum.GetNames(type).Select(CiWord)) + ")$");

    private static string CiWord(string word)
    {
        var sb = new System.Text.StringBuilder(word.Length * 2);
        foreach (var c in word)
        {
            if (char.IsLetter(c))
                sb.Append('[').Append(char.ToLowerInvariant(c)).Append(char.ToUpperInvariant(c)).Append(']');
            else
                sb.Append(c); // digits and '_' are not regex metacharacters
        }
        return sb.ToString();
    }

    // ── Definitions ────────────────────────────────────────────────────────────────────

    /// <summary>A clause type as a definition, keyed by its schema name.</summary>
    private static string EnsureTypeDef(JsonObject defs, Type clauseType)
    {
        var name = TypeDefName(clauseType);
        if (defs.ContainsKey(name))
            return name;

        var properties = new JsonObject();
        defs[name] = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
        };
        foreach (var prop in FilterVocabulary.ClauseProperties(clauseType))
            properties[Naming.Apply(prop.Name)] = PropertySchema(prop, defs);
        return name;
    }

    /// <summary>Nested block definition (source config, payload, card slot, soul entry) — refs are by name.</summary>
    private static JsonNode EnsureDef(JsonObject defs, Type type)
    {
        var name = TypeDefName(type);
        if (!defs.ContainsKey(name))
        {
            var properties = new JsonObject();
            defs[name] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties,
            };
            foreach (var prop in FilterVocabulary.ClauseProperties(type))
                properties[Naming.Apply(prop.Name)] = PropertySchema(prop, defs);
        }
        return new JsonObject { ["$ref"] = $"#/definitions/{name}" };
    }

    /// <summary>Classes a clause property can name: source configs, payloads, soul entries, card slots.</summary>
    private static IEnumerable<Type> NestedObjectTypes()
    {
        foreach (var clauseType in FilterVocabulary.DiscriminatorTable.Select(d => d.ClauseType).Distinct())
        foreach (var prop in FilterVocabulary.ClauseProperties(clauseType))
        {
            var t = FilterVocabulary.SequenceElement(prop.PropertyType)
                ?? Nullable.GetUnderlyingType(prop.PropertyType)
                ?? prop.PropertyType;
            if (t is not null && IsNestedObject(t))
                yield return t;
        }
    }

    private static bool IsNestedObject(Type t) =>
        !t.IsEnum
        && t != typeof(string)
        && t != typeof(int)
        && t != typeof(bool)
        && !t.IsPrimitive
        && !t.IsArray
        && t != typeof(JamlEventRolls)
        && t != typeof(JamlStandardCardSlot)
        && t != typeof(AndPayload)
        && t != typeof(OrPayload)
        && !typeof(IMotelyClause).IsAssignableFrom(t);

    /// <summary>
    /// Null is a legal JAML value everywhere the loader tolerates an absent one: nullable
    /// properties, strings, converter-backed shorthands (which read empty as "any"), and
    /// nested blocks. Strict for raw arrays and value types, where an empty value would
    /// deserialize to null and reach the engine as a null deref rather than a filter.
    /// </summary>
    private static bool AllowsNull(PropertyInfo prop)
    {
        if (Nullable.GetUnderlyingType(prop.PropertyType) is not null)
            return true;
        if (prop.PropertyType == typeof(string))
            return true;
        var t = Unwrap(prop.PropertyType);
        if (t == typeof(JamlEventRolls)
            || t == typeof(JamlStandardCardSlot)
            || t == typeof(AndPayload)
            || t == typeof(OrPayload)
            || t == typeof(MotelyWith)
            || (t is not null && t.Name.EndsWith("SourceConfig", StringComparison.Ordinal)))
            return true;
        // Converter-backed enum lists read empty/any as the category-any list.
        return FilterVocabulary.SequenceElement(prop.PropertyType) is { IsEnum: true };
    }

    private static string TypeDefName(Type type)
    {
        var name = type.Name;
        if (name.StartsWith("Motely", StringComparison.Ordinal))
            name = name["Motely".Length..];
        if (name.StartsWith("Jaml", StringComparison.Ordinal))
            name = name["Jaml".Length..];
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static Type? Unwrap(Type t) =>
        FilterVocabulary.SequenceElement(t) ?? Nullable.GetUnderlyingType(t) ?? t;
}
