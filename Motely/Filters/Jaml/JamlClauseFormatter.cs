using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using VYaml.Emitter;
using VYaml.Parser;
using VYaml.Serialization;

namespace Motely.Filters.Jaml;

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2077", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Motely is preserved by ILLink.Descriptors.xml.")]
public sealed class JamlClauseFormatter : IYamlFormatter<IJamlClause>
{
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

    public void Serialize(ref Utf8YamlEmitter emitter, IJamlClause value, YamlSerializationContext context) =>
        throw new NotSupportedException("JAML is read-only.");

    public IJamlClause Deserialize(ref YamlParser parser, YamlDeserializationContext context) =>
        ReadClause(ReadNode(ref parser));

    private sealed class Node
    {
        public int Line;
        public string? Scalar;
        public List<Node>? Items;
        public List<(string Key, Node Value)>? Map;
        public bool IsNull => Scalar is null && Items is null && Map is null;
    }

    private static Node ReadNode(ref YamlParser parser)
    {
        var node = new Node { Line = parser.CurrentMark.Line };
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
                node.Scalar = parser.IsNullScalar() ? null : parser.ReadScalarAsString();
                if (node.Scalar is null) parser.Read();
                return node;

            case ParseEventType.SequenceStart:
                parser.Read();
                node.Items = [];
                while (parser.CurrentEventType != ParseEventType.SequenceEnd)
                    node.Items.Add(ReadNode(ref parser));
                parser.Read();
                return node;

            case ParseEventType.MappingStart:
                parser.Read();
                node.Map = [];
                while (parser.CurrentEventType != ParseEventType.MappingEnd)
                {
                    var key = parser.ReadScalarAsString() ?? "";
                    node.Map.Add((key, ReadNode(ref parser)));
                }
                parser.Read();
                return node;

            default:
                throw Error(node.Line, $"unexpected YAML event {parser.CurrentEventType}");
        }
    }

    private static IJamlClause ReadClause(Node node)
    {
        if (node.Map is null)
            throw Error(node.Line, "a clause must be a mapping like `- joker: Blueprint`");

        var wireIndex = node.Map.FindIndex(kv => Wires.ContainsKey(kv.Key));
        if (wireIndex < 0)
            throw Error(node.Line, $"no recognised discriminator among: {string.Join(", ", node.Map.Select(kv => kv.Key))}");

        var (wire, value) = node.Map[wireIndex];
        var (type, attr) = Wires[wire];
        var clause = (IJamlClause)Activator.CreateInstance(type)!;

        var rest = node.Map.Where((_, i) => i != wireIndex).ToList();

        if (!value.IsNull)
        {
            if (attr.RollsAreInlineValue)
                rest.Insert(0, ("rolls", value));
            else if (clause is LogicClause)
                rest.Insert(0, ("clauses", value));
            else if (attr.ValueEnum is { } valueEnum)
                rest.Insert(0, (ValueProperty(type, valueEnum).Name, value));
            else if (value.Map is not null)
                rest.InsertRange(0, value.Map);
            else
                throw Error(value.Line, $"`{wire}` takes a block of keys, not a bare value");
        }

        Populate(clause, rest);

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
                throw Error(value.Line, $"unknown key `{rawKey}` on {type.Name}");
            prop.SetValue(target, Convert(value, prop.PropertyType, rawKey));
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
                return Array.CreateInstanceFromArrayType(type, 0);
            var items = node.Items ?? [node];
            var array = Array.CreateInstanceFromArrayType(type, items.Count);
            for (int i = 0; i < items.Count; i++)
                array.SetValue(Convert(items[i], elem, key), i);
            return array;
        }

        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (typeof(IJamlClause).IsAssignableFrom(t))
            return ReadClause(node);

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

        if (node.Map is null)
            throw Error(node.Line, $"`{key}` must be a block of keys");
        var nested = Activator.CreateInstance(t)!;
        Populate(nested, node.Map);
        return nested;
    }

    private static InvalidOperationException Error(int line, string message) =>
        new($"JAML line {line}: {message}");
}
