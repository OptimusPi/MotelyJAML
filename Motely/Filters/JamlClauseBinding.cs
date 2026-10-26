using System.Collections.Frozen;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
namespace Motely.Filters;

/// <summary>JAML clause discriminators, scalar-or-list converters, and logic-clause payloads.</summary>
internal static class JamlClauseBinding
{
    internal static IReadOnlyCollection<string> ClauseKeys => DiscriminatorKeys;

    internal static IEnumerable<IYamlTypeConverter> Converters =>
    [
        new ScalarOrSequenceConverter<MotelyJoker>(),
        new ScalarOrSequenceConverter<MotelyJokerCommon>(),
        new ScalarOrSequenceConverter<MotelyJokerUncommon>(),
        new ScalarOrSequenceConverter<MotelyJokerRare>(),
        new ScalarOrSequenceConverter<MotelyJokerLegendary>(),
        new ScalarOrSequenceConverter<MotelyPlanetCard>(),
        new ScalarOrSequenceConverter<MotelySpectralCard>(),
        new ScalarOrSequenceConverter<MotelyTarotCard>(),
        new ScalarOrSequenceConverter<MotelyVoucher>(),
        new ScalarOrSequenceConverter<MotelyTag>(),
        new ScalarOrSequenceConverter<MotelyBossBlind>(),
        new ScalarOrSequenceConverter<MotelyBoosterPack>(),
        new ScalarOrSequenceConverter<MotelyPokerHand>(),
        new IntListConverter(),
        new JamlEventRolls.Converter(),
        new OrPayloadConverter(),
        new AndPayloadConverter(),
        new JamlStandardCardSlotConverter(),
    ];

    internal static readonly Dictionary<string, Type> ClauseTypeByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["joker"] = typeof(JokerClause),
        ["jokers"] = typeof(JokerClause),
        ["commonJoker"] = typeof(CommonJokerClause),
        ["commonJokers"] = typeof(CommonJokerClause),
        ["uncommonJoker"] = typeof(UncommonJokerClause),
        ["uncommonJokers"] = typeof(UncommonJokerClause),
        ["rareJoker"] = typeof(RareJokerClause),
        ["rareJokers"] = typeof(RareJokerClause),
        ["legendaryJoker"] = typeof(LegendaryJokerClause),
        ["legendaryJokers"] = typeof(LegendaryJokerClause),
        ["soulJoker"] = typeof(LegendaryJokerClause),
        ["planetCard"] = typeof(PlanetCardClause),
        ["planetCards"] = typeof(PlanetCardClause),
        ["spectralCard"] = typeof(SpectralCardClause),
        ["spectralCards"] = typeof(SpectralCardClause),
        ["tarotCard"] = typeof(TarotCardClause),
        ["tarotCards"] = typeof(TarotCardClause),
        ["standardCard"] = typeof(StandardCardClause),
        ["standardCards"] = typeof(StandardCardClause),
        ["erraticRank"] = typeof(ErraticRankClause),
        ["erraticRanks"] = typeof(ErraticRankClause),
        ["erraticSuit"] = typeof(ErraticSuitClause),
        ["erraticSuits"] = typeof(ErraticSuitClause),
        ["voucher"] = typeof(VoucherClause),
        ["vouchers"] = typeof(VoucherClause),
        ["tag"] = typeof(TagClause),
        ["tags"] = typeof(TagClause),
        ["smallBlindTag"] = typeof(SmallBlindTagClause),
        ["bigBlindTag"] = typeof(BigBlindTagClause),
        ["boss"] = typeof(BossClause),
        ["bosses"] = typeof(BossClause),
        ["boosterPack"] = typeof(BoosterPackClause),
        ["boosterPacks"] = typeof(BoosterPackClause),
        ["pokerHand"] = typeof(PokerHandClause),
        ["pokerHands"] = typeof(PokerHandClause),
        ["startingDraw"] = typeof(StartingDrawClause),
        ["luckyMoney"] = typeof(LuckyMoneyClause),
        ["luckyMult"] = typeof(LuckyMultClause),
        ["misprintMult"] = typeof(MisprintMultClause),
        ["wheelOfFortune"] = typeof(WheelOfFortuneClause),
        ["wheelStaysFlipped"] = typeof(WheelStaysFlippedClause),
        ["glassDestroy"] = typeof(GlassDestroyClause),
        ["cavendishExtinct"] = typeof(CavendishExtinctClause),
        ["grosMichelExtinct"] = typeof(GrosMichelExtinctClause),
        ["spaceLevelup"] = typeof(SpaceLevelupClause),
        ["businessPayout"] = typeof(BusinessPayoutClause),
        ["parkingPayout"] = typeof(ParkingPayoutClause),
        ["bloodstoneTrigger"] = typeof(BloodstoneTriggerClause),
        ["and"] = typeof(AndClause),
        ["or"] = typeof(OrClause),
        ["soulOrder"] = typeof(LegendarySoulOrderClause),
        ["soulOrders"] = typeof(LegendarySoulOrderClause),
    };

    private static readonly FrozenSet<string> DiscriminatorKeys =
        ClauseTypeByKey.Keys.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scalar, empty, <c>any</c>, or sequence → array (Nat's JAML filter clauses).</summary>
    internal sealed class ScalarOrSequenceConverter<T> : IYamlTypeConverter
    {
        private readonly Type _arrayType = typeof(T[]);

        public bool Accepts(Type type) => type == _arrayType;

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested) =>
            parser.Current switch
            {
                SequenceStart => ReadSequence(parser),
                Scalar scalar when IsCategoryAny(scalar) => ConsumeAndReturn(parser, Array.Empty<T>()),
                Scalar scalar when typeof(T) == typeof(int) => ConsumeAndReturn(
                    parser,
                    new[] { int.Parse(scalar.Value, System.Globalization.CultureInfo.InvariantCulture) }
                ),
                Scalar scalar => ConsumeAndReturn(
                    parser,
                    new[] { (T)JamlEnumName.Parse(typeof(T), scalar.Value) }
                ),
                _ => Array.Empty<T>(),
            };

        private static T[] ReadSequence(IParser parser)
        {
            parser.Consume<SequenceStart>();
            var items = new List<T>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                var scalar = parser.Consume<Scalar>();
                items.Add((T)JamlEnumName.Parse(typeof(T), scalar.Value));
            }

            return items.ToArray();
        }

        private static TResult ConsumeAndReturn<TResult>(IParser parser, TResult value)
        {
            parser.Consume<Scalar>();
            return value;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
            nested(value, type);
    }

    /// <summary>
    /// A list of numbers (<c>antes</c>, <c>shopItems</c>, …) as a list, or as one number for a list
    /// of one: <c>ante: 2</c> is <c>antes: [2]</c>. Each number reads as YamlDotNet reads an int; a
    /// null value stays null, as without this converter.
    /// </summary>
    internal sealed class IntListConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(int[]);

        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
        {
            if (parser.Current is Scalar scalar && scalar.Style == ScalarStyle.Plain && scalar.Value is "" or "~" or "null" or "Null" or "NULL")
            {
                parser.MoveNext();
                return null;
            }
            if (parser.Current is not SequenceStart)
                return new[] { (int)nested(typeof(int))! };

            parser.Consume<SequenceStart>();
            var items = new List<int>();
            while (!parser.TryConsume<SequenceEnd>(out _))
                items.Add((int)nested(typeof(int))!);
            return items.ToArray();
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
            nested(value, type);
    }

    private static bool IsCategoryAny(Scalar scalar)
    {
        if (scalar.Style is not (ScalarStyle.Plain or ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted))
            return false;
        var text = scalar.Value.Trim();
        return text.Length == 0
            || text.Equals("any", StringComparison.OrdinalIgnoreCase)
            || text.Equals("~", StringComparison.OrdinalIgnoreCase)
            || text.Equals("null", StringComparison.OrdinalIgnoreCase);
    }
}
