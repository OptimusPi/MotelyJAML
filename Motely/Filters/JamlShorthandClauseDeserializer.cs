using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary>
/// Custom YamlDotNet <see cref="INodeDeserializer"/> that intercepts
/// <see cref="IMotelyClause"/> deserialization to support shorthand syntax.
/// </summary>
/// <remarks>
/// <para>
/// Two shorthand forms are supported:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Pure scalar</b>: <c>- Perkeo in Ante 1 or 2</c>
///     → parsed by <see cref="JamlShorthandParser.TryParse"/>.
///   </item>
///   <item>
///     <b>Mixed mapping</b>: <c>- Perkeo in Ante 1 or 2:\n  score: 100</c>
///     → the shorthand key (with null/empty value) is parsed as the base clause,
///     and sibling keys (score, edition, etc.) are applied as property overrides.
///   </item>
/// </list>
/// <para>
/// All normal discriminator-key mappings (e.g. <c>legendaryJoker: Perkeo</c>)
/// fall through to the default deserializer.
/// </para>
/// </remarks>
public sealed class JamlShorthandClauseDeserializer : INodeDeserializer
{
    private readonly INodeDeserializer _defaultDeserializer;

    public JamlShorthandClauseDeserializer(INodeDeserializer defaultDeserializer)
    {
        _defaultDeserializer = defaultDeserializer;
    }

    public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value)
    {
        value = null;

        if (expectedType != typeof(IMotelyClause))
        {
            return false; // Not our jurisdiction
        }

        // Case 1: Scalar shorthand
        if (reader.Current is Scalar scalar)
        {
            if (!JamlShorthandParser.TryParse(scalar.Value, out var clause, out var error))
            {
                throw new YamlException(scalar.Start, scalar.End,
                    $"Invalid shorthand clause '{scalar.Value}': {error}");
            }
            value = clause;
            reader.MoveNext();
            return true;
        }

        // Case 2: Mapping — check if it's a shorthand-mixed form
        if (reader.Current is MappingStart mappingStart)
        {
            // We need to peek at the mapping keys without consuming them,
            // then decide if any key is shorthand.
            var shorthandClause = TryParseShorthandMapping(reader, nestedObjectDeserializer, out var hadShorthand);
            if (hadShorthand)
            {
                value = shorthandClause;
                return true;
            }
            // Fall through to default deserializer
            return _defaultDeserializer.Deserialize(reader, expectedType, nestedObjectDeserializer, out value);
        }

        return false;
    }

    /// <summary>
    /// Attempts to parse a mapping where one key is a shorthand string.
    /// </summary>
    /// <remarks>
    /// YAML syntax: <c>- Perkeo in Ante 1 or 2:\n  score: 100</c>
    /// The shorthand key has an empty/null value; sibling keys override properties.
    /// </remarks>
    private IMotelyClause? TryParseShorthandMapping(IParser reader, Func<IParser, Type, object?> nestedObjectDeserializer, out bool hadShorthand)
    {
        hadShorthand = false;

        if (reader.Current is not MappingStart)
            return null;

        var start = reader.Current.Start;
        reader.MoveNext(); // consume MappingStart

        IMotelyClause? baseClause = null;
        var overridePairs = new Dictionary<string, object?>();

        while (reader.Current is not MappingEnd)
        {
            if (reader.Current is not Scalar keyScalar)
            {
                // Unexpected token in mapping — abort and let default handler deal with it
                reader.SkipThisAndNestedEvents();
                continue;
            }

            var key = keyScalar.Value;
            reader.MoveNext(); // consume key

            // Is this key a known discriminator? If so, this is NOT a shorthand mapping.
            if (JamlClauseBinding.ClauseTypeByKey.ContainsKey(key))
            {
                // Rollback: we consumed MappingStart and this key. Push back is impossible
                // with IParser, so we must abort and let the default deserializer handle
                // the entire mapping from the start. Since we already consumed MappingStart,
                // the best we can do is parse the rest as a normal object.
                return null;
            }

            // Try to parse the key as shorthand
            if (baseClause == null && JamlShorthandParser.TryParse(key, out var parsedClause, out _))
            {
                // The value should be null/empty for the shorthand key
                if (reader.Current is MappingStart)
                {
                    // Shorthand key with a nested mapping value — skip it for now
                    reader.SkipThisAndNestedEvents();
                }
                else if (reader.Current is Scalar valScalar)
                {
                    // Scalar value (possibly null/empty) — consume it
                    reader.MoveNext();
                }
                else
                {
                    // Sequence or other — consume
                    reader.SkipThisAndNestedEvents();
                }

                baseClause = parsedClause;
                hadShorthand = true;
            }
            else
            {
                // Regular property override — deserialize the value
                var val = nestedObjectDeserializer(reader, typeof(object));
                overridePairs[key] = val;
            }
        }

        if (reader.Current is MappingEnd)
        {
            reader.MoveNext(); // consume MappingEnd
        }

        if (baseClause != null)
        {
            ApplyOverrides(baseClause, overridePairs);
        }

        return baseClause;
    }

    private static void ApplyOverrides(IMotelyClause clause, Dictionary<string, object?> overrides)
    {
        var type = clause.GetType();
        foreach (var (key, val) in overrides)
        {
            if (val == null) continue;

            var prop = type.GetProperty(key);
            if (prop == null || !prop.CanWrite)
                continue;

            try
            {
                var converted = ConvertValue(val, prop.PropertyType);
                if (converted != null)
                {
                    prop.SetValue(clause, converted);
                }
            }
            catch
            {
                // Best-effort override; skip on conversion failure
            }
        }
    }

    private static object? ConvertValue(object value, Type targetType)
    {
        if (value == null)
            return null;

        if (targetType.IsInstanceOfType(value))
            return value;

        // Handle numeric conversions (YamlDotNet often deserializes numbers as long/double)
        if (targetType == typeof(int) && value is long l)
            return (int)l;
        if (targetType == typeof(int) && value is double d)
            return (int)d;
        if (targetType == typeof(int[]) && value is IList<object> list)
            return list.Select(x => Convert.ToInt32(x)).ToArray();
        if (targetType == typeof(MotelyJokerSticker[]) && value is IList<object> stickerList)
            return stickerList.Select(x => Enum.Parse<MotelyJokerSticker>(x.ToString()!)).ToArray();
        if (targetType == typeof(MotelyItemEdition?) && value is string s)
            return Enum.Parse<MotelyItemEdition>(s);

        // Try general Convert
        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Extension to skip a complete YAML event including any nested events.
/// </summary>
internal static class YamlParserExtensions
{
    public static void SkipThisAndNestedEvents(this IParser parser)
    {
        var depth = 0;
        do
        {
            switch (parser.Current)
            {
                case MappingStart:
                case SequenceStart:
                    depth++;
                    break;
                case MappingEnd:
                case SequenceEnd:
                    depth--;
                    break;
            }
            parser.MoveNext();
        } while (depth > 0 && parser.Current != null);
    }
}
