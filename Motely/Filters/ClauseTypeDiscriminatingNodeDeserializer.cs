using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.BufferedDeserialization;

namespace Motely.Filters;

/// <summary>
/// Picks a concrete clause type from Nat's JAML discriminator keys, then lets YamlDotNet bind properties.
/// A clause written as a plain string (<c>- Perkeo in ante 1 or 2</c>) is read by
/// <see cref="JamlShorthand"/> into the same clause its mapping twin binds to.
/// </summary>
internal sealed class ClauseTypeDiscriminatingNodeDeserializer : INodeDeserializer
{
    private const int MaxBufferDepth = 128;
    private const int MaxBufferLength = 1_048_576;

    /// <summary>A clause the document left without <c>antes</c> and/or <c>score</c>, for <c>defaults:</c> to fill.</summary>
    internal readonly record struct Unstated(IMotelyClause Clause, bool Antes, bool Score);

    /// <summary>
    /// While <see cref="JamlConfigLoader.FromJaml"/> reads a document: each clause read that leaves a
    /// defaultable key out. The <c>defaults:</c> block may come after the clauses, so they are filled
    /// in once the whole document is read. Null outside a load (one load per thread at a time).
    /// </summary>
    [ThreadStatic]
    internal static List<Unstated>? UnstatedClauses;

    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer
    )
    {
        value = null;
        if (expectedType != typeof(IMotelyClause))
            return false;

        if (reader.Current is Scalar scalar)
        {
            try
            {
                var reading = JamlShorthand.Parse(scalar.Value);
                value = reading.Clause;
                Note(reading.Clause, !reading.StatesAntes, !reading.StatesScore);
            }
            catch (FormatException ex)
            {
                // The scalar's own marks, so the error names the clause's line; the inner
                // exception carries the bare message the loader prints after "JAML line N:".
                throw new YamlException(scalar.Start, scalar.End, ex.Message, ex);
            }
            reader.MoveNext();
            return true;
        }

        // The node's own marks, taken before buffering: once the keys have been scanned the
        // buffer stands past the mapping, where there is no event to point at.
        var start = reader.Current ?? throw new YamlException("a clause is missing here.");
        if (start is not MappingStart)
            throw new YamlException(start.Start, start.End,
                "a clause is a mapping such as `joker: Blueprint`, or one line such as `Blueprint in ante 1`.");

        var buffer = new ParserBuffer(reader, MaxBufferDepth, MaxBufferLength);
        var values = new Dictionary<Scalar, string>();
        var keys = TopLevelKeys(buffer, values);
        var concrete = ClauseType(start, keys, values);
        RefuseBothSpellings(keys, "ante", "antes");

        bool statesScore = keys.Any(k => k.Value.Equals("score", StringComparison.OrdinalIgnoreCase));
        if (!statesScore && concrete.IsSubclassOf(typeof(LogicClause)))
        {
            // `and: { clauses: [...], score: 5 }` states the group's score inside its payload.
            buffer.Reset();
            statesScore = PayloadKeys(buffer).Any(k => k.Value.Equals("score", StringComparison.OrdinalIgnoreCase));
        }

        buffer.Reset();
        var clause = (IMotelyClause)nestedObjectDeserializer(buffer, concrete)!;
        value = clause;
        Note(clause,
            antes: !keys.Any(k => k.Value.Equals("antes", StringComparison.OrdinalIgnoreCase) || k.Value.Equals("ante", StringComparison.OrdinalIgnoreCase)),
            score: !statesScore);
        return true;
    }

    private static void Note(IMotelyClause clause, bool antes, bool score)
    {
        if ((antes || score) && UnstatedClauses is { } unstated)
            unstated.Add(new(clause, antes, score));
    }

    /// <summary>The keys of the logic clause's payload mapping (<c>and: { mode, clauses, score }</c>); none for a list payload.</summary>
    private static List<Scalar> PayloadKeys(IParser buffer)
    {
        buffer.Consume<MappingStart>();
        while (!buffer.TryConsume<MappingEnd>(out _))
        {
            var key = buffer.Current as Scalar;
            buffer.SkipThisAndNestedEvents();
            if (key is not null && JamlClauseBinding.ClauseTypeByKey.TryGetValue(key.Value, out var type)
                && type.IsSubclassOf(typeof(LogicClause)) && buffer.Current is MappingStart)
                return TopLevelKeys(buffer);
            buffer.SkipThisAndNestedEvents();
        }
        return [];
    }

    /// <summary>A clause writes one spelling of a key; with both, one would silently win.</summary>
    private static void RefuseBothSpellings(List<Scalar> keys, string alias, string key)
    {
        var a = keys.FirstOrDefault(k => k.Value.Equals(alias, StringComparison.OrdinalIgnoreCase));
        var b = keys.FirstOrDefault(k => k.Value.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (a is null || b is null)
            return;
        var later = a.Start.Index > b.Start.Index ? a : b;
        throw new YamlException(later.Start, later.End, $"this clause writes both `{alias}` and `{key}`: keep one.");
    }

    /// <summary>The mapping's own keys, in order (not the keys of blocks nested in it).</summary>
    /// <param name="values">When given, each key whose value is a plain value maps to it here.</param>
    private static List<Scalar> TopLevelKeys(IParser buffer, Dictionary<Scalar, string>? values = null)
    {
        var keys = new List<Scalar>();
        buffer.Consume<MappingStart>();
        while (!buffer.TryConsume<MappingEnd>(out _))
        {
            var key = buffer.Current as Scalar;
            if (key is not null)
                keys.Add(key);
            buffer.SkipThisAndNestedEvents(); // the key
            if (key is not null && values is not null && buffer.Current is Scalar value)
                values[key] = value.Value;
            buffer.SkipThisAndNestedEvents(); // its value
        }
        return keys;
    }

    /// <summary>
    /// The clause type the mapping's keys select. One key names the clause; another clause key may
    /// sit beside it only where the selected type also takes it as a plain key (<c>soulOrder</c>
    /// takes <c>boosterPacks</c>).
    /// </summary>
    private static Type ClauseType(ParsingEvent start, List<Scalar> keys, Dictionary<Scalar, string> values)
    {
        var clauseKeys = keys.Where(k => JamlClauseBinding.ClauseTypeByKey.ContainsKey(k.Value)).ToList();
        if (clauseKeys.Count == 0)
        {
            var (at, message) = NoClauseKey(keys, values);
            var mark = (ParsingEvent?)at ?? start;
            throw new YamlException(mark.Start, mark.End, message);
        }

        foreach (var candidate in clauseKeys)
        {
            var type = JamlClauseBinding.ClauseTypeByKey[candidate.Value];
            if (clauseKeys.All(k => k == candidate || JamlKeys.Takes(type, k.Value)))
                return type;
        }

        var first = clauseKeys[0];
        var second = clauseKeys[1];
        var firstType = JamlClauseBinding.ClauseTypeByKey[first.Value];
        if (JamlKeys.SourceKeysOf(firstType).Any(k => k.Equals(second.Value, StringComparison.OrdinalIgnoreCase)))
            throw new YamlException(second.Start, second.End,
                $"'{second.Value}' on a `{first.Value}` clause goes under `sources:`, as in `sources: {{ {second.Value}: [0] }}`.");
        throw new YamlException(second.Start, second.End,
            $"one clause has two clause keys, `{first.Value}` and `{second.Value}`: write each as its own `- ` item"
            + (JamlClauseBinding.ClauseTypeByKey[second.Value].IsSubclassOf(typeof(LogicClause)) ? $", as in `- {second.Value}:`." : "."));
    }

    /// <summary>Why no key names a clause, and the key to point at (null: the mapping itself).</summary>
    private static (Scalar? At, string Message) NoClauseKey(List<Scalar> keys, Dictionary<Scalar, string> values)
    {
        if (keys.Count == 0)
            return (null, "an empty clause: write one such as `joker: Blueprint`.");

        // BalatroSeedOracle's builder wrote a clause as { type: Joker, value: Blueprint }.
        var type = keys.FirstOrDefault(k => k.Value.Equals("type", StringComparison.OrdinalIgnoreCase));
        var value = keys.FirstOrDefault(k => k.Value.Equals("value", StringComparison.OrdinalIgnoreCase));
        if (type is not null && value is not null)
        {
            var kind = values.GetValueOrDefault(type) ?? "";
            var clauseKey = JamlClauseBinding.ClauseTypeByKey.Keys.FirstOrDefault(k => k.Equals(kind, StringComparison.OrdinalIgnoreCase));
            return (type, clauseKey is null
                ? $"a clause names its kind by its key, not by `type:` and `value:`: write it as `joker: Blueprint`, and '{kind}' is not a clause key."
                : $"a clause names its kind by its key, not by `type:` and `value:`: write `{clauseKey}: {values.GetValueOrDefault(value) ?? "..."}`.");
        }

        // A key some clause takes (score, antes, sources …) is not the misspelt clause key.
        foreach (var key in keys.Where(k => !JamlClauseBinding.ClauseTypeByKey.Values.Any(t => JamlKeys.Takes(t, k.Value))))
        {
            var closest = JamlKeys.Closest(key.Value, JamlClauseBinding.ClauseTypeByKey.Keys);
            if (closest.Count > 0)
                return (key, $"'{key.Value}' is not a clause key; did you mean {JamlKeys.JoinOr(closest.Select(k => $"`{k}`").ToList())}?");
        }
        return (keys[0], $"no clause key among {string.Join(", ", keys.Select(k => k.Value))}: a clause starts with a key such as "
            + "`joker`, `legendaryJoker`, `voucher`, `tag`, `boss`, `tarotCard`, `planetCard`, `spectralCard` or `luckyMoney`.");
    }
}
