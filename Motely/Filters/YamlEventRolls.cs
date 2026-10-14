using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary>Event-clause roll shorthand: <c>luckyMoney: 1</c>, <c>luckyMoney: []</c>, or a list.</summary>
[YamlSerializable]
public sealed class YamlEventRolls
{
    public int[] Rolls { get; set; } = [];

    public int[] ToArray() => Rolls;

    internal sealed class Converter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(YamlEventRolls);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            if (parser.Current is SequenceStart)
                return new YamlEventRolls { Rolls = ReadIntSequence(parser) };

            if (parser.Current is Scalar scalar)
            {
                parser.Consume<Scalar>();
                if (IsEmpty(scalar))
                    return new YamlEventRolls { Rolls = [] };
                return new YamlEventRolls
                {
                    Rolls = [int.Parse(scalar.Value, System.Globalization.CultureInfo.InvariantCulture)],
                };
            }

            return new YamlEventRolls { Rolls = [] };
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
            nested(value, type);

        private static int[] ReadIntSequence(IParser parser)
        {
            parser.Consume<SequenceStart>();
            var items = new List<int>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                var scalar = parser.Consume<Scalar>();
                items.Add(int.Parse(scalar.Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            return items.ToArray();
        }

        private static bool IsEmpty(Scalar scalar)
        {
            var text = scalar.Value.Trim();
            return text.Length == 0
                || text.Equals("any", StringComparison.OrdinalIgnoreCase)
                || text.Equals("~", StringComparison.OrdinalIgnoreCase)
                || text.Equals("null", StringComparison.OrdinalIgnoreCase);
        }
    }
}
