using System.Collections.Concurrent;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely.Filters;

/// <summary>
/// The keys each JAML type takes, read off the generated static context (no reflection, so it runs
/// under NativeAOT), and the one-line messages that name the right key when a document writes a
/// wrong one.
/// </summary>
internal static class JamlKeys
{
    private static readonly ITypeInspector Inspector = new JamlYamlContext().GetTypeInspector();

    private static readonly ConcurrentDictionary<Type, (string Key, Type Type)[]> Cache = new();

    /// <summary>Every type a document can name, by full name: the root, the clauses and their nested blocks.</summary>
    private static readonly Lazy<Dictionary<string, Type>> TypesByName = new(BuildTypesByName);

    /// <summary>Second spellings (JamlKeyAliases.cs): accepted, but a message names the canonical key.</summary>
    private static readonly HashSet<string> SecondSpellings = new(StringComparer.OrdinalIgnoreCase) { "ante", "requireMega", "soulJoker" };

    /// <summary>The keys <paramref name="type"/> takes, camelCase, alphabetical, with each value's type.</summary>
    public static (string Key, Type Type)[] Of(Type type) =>
        Cache.GetOrAdd(type, static t =>
            Inspector
                .GetProperties(t, null)
                .Where(p => p.CanWrite)
                .Select(p => (CamelCaseNamingConvention.Instance.Apply(p.Name), p.Type))
                .OrderBy(p => p.Item1, StringComparer.Ordinal)
                .ToArray());

    public static bool Takes(Type type, string key) =>
        Of(type).Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The value type of <paramref name="key"/> on <paramref name="type"/>, or null.</summary>
    public static Type? TypeOf(Type type, string key)
    {
        foreach (var (k, t) in Of(type))
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    /// <summary>The keys of the <c>sources:</c> block of a clause type, or empty when it has none.</summary>
    public static IEnumerable<string> SourceKeysOf(Type clauseType) =>
        TypeOf(clauseType, "sources") is { } sources ? Of(Nullable.GetUnderlyingType(sources) ?? sources).Select(p => p.Key) : [];

    /// <summary>
    /// The one-line message for a key <paramref name="typeFullName"/> does not take, in place of
    /// YamlDotNet's <c>Property 'x' not found on type 'T'</c>; null when the type is not one a
    /// document names.
    /// </summary>
    public static string? UnknownKeyMessage(string key, string typeFullName)
    {
        if (!TypesByName.Value.TryGetValue(typeFullName, out var type))
            return null;
        var where = Describe(type);

        if (typeof(IMotelyClause).IsAssignableFrom(type) && SourceKeysOf(type).FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } sourceKey)
            return $"'{key}' is not a key of {where}: it goes under `sources:`, as in `sources: {{ {sourceKey}: [0] }}`.";

        var keys = Of(type).Select(p => p.Key).Where(k => !SecondSpellings.Contains(k)).ToList();
        var closest = Closest(key, keys);
        return closest.Count > 0
            ? $"'{key}' is not a key of {where}; did you mean {JoinOr(closest.Select(k => $"`{k}`").ToList())}?"
            : $"'{key}' is not a key of {where}, which takes {string.Join(", ", keys)}.";
    }

    /// <summary>"a filter", "a `joker` clause", "`sources:` of a `joker` clause", "`with:`" …</summary>
    public static string Describe(Type type)
    {
        if (type == typeof(JamlConfig))
            return "a filter";
        if (type == typeof(MotelyWith))
            return "`with:`";
        if (ClauseKeyOf(type) is { } clauseKey)
            return $"a `{clauseKey}` clause";
        if (type.Name.EndsWith("SourceConfig", StringComparison.Ordinal))
        {
            var owner = JamlClauseBinding.ClauseTypeByKey.Values.FirstOrDefault(c => TypeOf(c, "sources") is { } s && (Nullable.GetUnderlyingType(s) ?? s) == type);
            return owner is null ? "`sources:`" : $"`sources:` on a `{ClauseKeyOf(owner)}` clause";
        }
        if (type == typeof(AndPayloadMapping))
            return "an `and:` block";
        if (type == typeof(OrPayloadMapping))
            return "an `or:` block";
        if (type == typeof(JamlStandardCardSlotMapping))
            return "a `standardCard:` block";
        return $"`{CamelCaseNamingConvention.Instance.Apply(type.Name)}`";
    }

    /// <summary>The first discriminator key that selects <paramref name="clauseType"/>, as the binding table spells it.</summary>
    public static string? ClauseKeyOf(Type clauseType)
    {
        foreach (var (key, type) in JamlClauseBinding.ClauseTypeByKey)
            if (type == clauseType)
                return key;
        return null;
    }

    private static Dictionary<string, Type> BuildTypesByName()
    {
        var found = new Dictionary<string, Type>(StringComparer.Ordinal);
        var pending = new Stack<Type>([typeof(JamlConfig), typeof(AndPayloadMapping), typeof(OrPayloadMapping), typeof(JamlStandardCardSlotMapping), .. JamlClauseBinding.ClauseTypeByKey.Values]);
        while (pending.TryPop(out var type))
        {
            if (type.FullName is not { } name || !found.TryAdd(name, type))
                continue;
            foreach (var (_, valueType) in Of(type))
            {
                var element = valueType.IsArray ? valueType.GetElementType()! : Nullable.GetUnderlyingType(valueType) ?? valueType;
                if (element.IsClass && element != typeof(string) && !element.IsInterface && element.Namespace is "Motely" or "Motely.Filters")
                    pending.Push(element);
            }
        }
        return found;
    }

    // ── Names ────────────────────────────────────────────────────────────────────────────

    /// <summary>Letters and digits only, lower case: "The Eye", "the_eye" and "TheEye" are one name.</summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Up to three of <paramref name="names"/> nearest <paramref name="text"/> by edit distance, then by containment.</summary>
    public static List<string> Closest(string text, IEnumerable<string> names)
    {
        var key = Normalize(text);
        if (key.Length == 0)
            return [];
        int limit = Math.Max(2, key.Length / 3);
        return names
            .Select(name => (Name: name, Key: Normalize(name)))
            .Select(n => (n.Name, Distance: n.Key.Contains(key, StringComparison.Ordinal) || key.Contains(n.Key, StringComparison.Ordinal)
                ? Math.Min(limit, Levenshtein(key, n.Key))
                : Levenshtein(key, n.Key)))
            .Where(c => c.Distance <= limit)
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => c.Name)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();
    }

    public static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    public static string JoinOr(IReadOnlyList<string> names) =>
        names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
}
