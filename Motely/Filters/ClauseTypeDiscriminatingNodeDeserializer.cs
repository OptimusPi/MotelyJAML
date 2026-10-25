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
                value = JamlShorthand.Parse(scalar.Value);
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
        var keys = TopLevelKeys(buffer);
        var concrete = ClauseType(start, keys);

        buffer.Reset();
        value = nestedObjectDeserializer(buffer, concrete);
        return true;
    }

    /// <summary>The mapping's own keys, in order (not the keys of blocks nested in it).</summary>
    private static List<Scalar> TopLevelKeys(IParser buffer)
    {
        var keys = new List<Scalar>();
        buffer.Consume<MappingStart>();
        while (!buffer.TryConsume<MappingEnd>(out _))
        {
            if (buffer.Current is Scalar key)
                keys.Add(key);
            buffer.SkipThisAndNestedEvents(); // the key
            buffer.SkipThisAndNestedEvents(); // its value
        }
        return keys;
    }

    /// <summary>
    /// The clause type the mapping's keys select. One key names the clause; another clause key may
    /// sit beside it only where the selected type also takes it as a plain key (<c>soulOrder</c>
    /// takes <c>boosterPacks</c>).
    /// </summary>
    private static Type ClauseType(ParsingEvent start, List<Scalar> keys)
    {
        var clauseKeys = keys.Where(k => JamlClauseBinding.ClauseTypeByKey.ContainsKey(k.Value)).ToList();
        if (clauseKeys.Count == 0)
        {
            var (at, message) = NoClauseKey(keys);
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
    private static (Scalar? At, string Message) NoClauseKey(List<Scalar> keys)
    {
        if (keys.Count == 0)
            return (null, "an empty clause: write one such as `joker: Blueprint`.");

        // BalatroSeedOracle's builder wrote a clause as { type: Joker, value: Blueprint }.
        var type = keys.FirstOrDefault(k => k.Value.Equals("type", StringComparison.OrdinalIgnoreCase));
        if (type is not null && keys.Any(k => k.Value.Equals("value", StringComparison.OrdinalIgnoreCase)))
            return (type, "a clause names its kind by its key, not by `type:` and `value:`: write `joker: Blueprint` for { type: Joker, value: Blueprint }.");

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
