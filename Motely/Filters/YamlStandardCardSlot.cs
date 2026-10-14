using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary>
/// <c>standardCard:</c> as a whole card scalar, <c>any</c>, or a nested <c>{ rank, suit, … }</c> block.
/// </summary>
[YamlSerializable]
public sealed class YamlStandardCardSlot
{
    public MotelyStandardCard? Card { get; set; }
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
    public MotelyItemEnhancement? Enhancement { get; set; }
    public MotelyItemSeal? Seal { get; set; }
    public MotelyItemEdition? Edition { get; set; }
}

[YamlSerializable]
internal sealed class YamlStandardCardSlotMapping
{
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
    public MotelyItemEnhancement? Enhancement { get; set; }
    public MotelyItemSeal? Seal { get; set; }
    public MotelyItemEdition? Edition { get; set; }
}

internal sealed class YamlStandardCardSlotConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(YamlStandardCardSlot);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
    {
        if (parser.Current is Scalar scalar)
        {
            parser.Consume<Scalar>();
            if (IsCategoryAny(scalar))
                return new YamlStandardCardSlot();
            return new YamlStandardCardSlot
            {
                Card = Enum.Parse<MotelyStandardCard>(scalar.Value, ignoreCase: true),
            };
        }

        var mapped = (YamlStandardCardSlotMapping)nested(typeof(YamlStandardCardSlotMapping))!;
        return new YamlStandardCardSlot
        {
            Rank = mapped.Rank,
            Suit = mapped.Suit,
            Enhancement = mapped.Enhancement,
            Seal = mapped.Seal,
            Edition = mapped.Edition,
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
        nested(value, type);

    private static bool IsCategoryAny(Scalar scalar)
    {
        var text = scalar.Value.Trim();
        return text.Length == 0
            || text.Equals("any", StringComparison.OrdinalIgnoreCase)
            || text.Equals("~", StringComparison.OrdinalIgnoreCase)
            || text.Equals("null", StringComparison.OrdinalIgnoreCase);
    }
}
