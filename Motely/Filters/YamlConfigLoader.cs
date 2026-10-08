using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely.Filters;

/// <summary>
/// YAML text → <see cref="JamlConfig"/> through YamlDotNet's static (source-generated)
/// deserializer: no reflection, so trimming and NativeAOT keep working. JSON is YAML, so a JSON
/// document loads through the same door.
/// </summary>
public static class YamlConfigLoader
{
    private static readonly IDeserializer Deserializer = new StaticDeserializerBuilder(new JamlYamlContext())
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithCaseInsensitivePropertyMatching()
        .WithNodeDeserializer(new ClauseNodeDeserializer())
        .Build();

    public static JamlConfig FromYaml(string text)
    {
        try
        {
            return Deserializer.Deserialize<JamlConfig?>(text)
                ?? throw new InvalidOperationException("YAML: the document is empty.");
        }
        catch (YamlException ex)
        {
            var inner = ex;
            while (inner.InnerException is YamlException next)
                inner = next;
            throw new InvalidOperationException(
                $"YAML line {inner.Start.Line}: {inner.InnerException?.Message ?? inner.Message}", ex);
        }
    }

    public static JamlConfig FromFile(string path) => FromYaml(File.ReadAllText(path));

    public static bool TryLoad(string text, [NotNullWhen(true)] out JamlConfig? config, out string? error)
    {
        try
        {
            config = FromYaml(text);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            config = null;
            error = ex.Message;
            return false;
        }
    }
}

/// <summary>
/// <c>- uncommonJoker: Blueprint</c> → <see cref="UncommonJokerClause"/>. The first clause key
/// picks the type and its value moves to the property it names (<c>jokers</c>, the event clauses'
/// <c>rolls</c>, a logic clause's <c>clauses</c>), wrapped into a list when written as one value;
/// <c>any</c> is the whole category, an empty list. A mapping value is the clause's own keys
/// (<c>- or: { mode: max, clauses: [...] }</c>). The rewritten events go back to YamlDotNet.
/// </summary>
internal sealed class ClauseNodeDeserializer : INodeDeserializer
{
    private readonly record struct ClauseKey(Type Type, string ValueProperty, bool ValueIsList);

    private static readonly Dictionary<string, ClauseKey> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["joker"] = new(typeof(JokerClause), "jokers", true),
        ["jokers"] = new(typeof(JokerClause), "jokers", true),
        ["commonJoker"] = new(typeof(CommonJokerClause), "jokers", true),
        ["commonJokers"] = new(typeof(CommonJokerClause), "jokers", true),
        ["uncommonJoker"] = new(typeof(UncommonJokerClause), "jokers", true),
        ["uncommonJokers"] = new(typeof(UncommonJokerClause), "jokers", true),
        ["rareJoker"] = new(typeof(RareJokerClause), "jokers", true),
        ["rareJokers"] = new(typeof(RareJokerClause), "jokers", true),
        ["legendaryJoker"] = new(typeof(LegendaryJokerClause), "jokers", true),
        ["legendaryJokers"] = new(typeof(LegendaryJokerClause), "jokers", true),
        ["planetCard"] = new(typeof(PlanetCardClause), "planets", true),
        ["planetCards"] = new(typeof(PlanetCardClause), "planets", true),
        ["spectralCard"] = new(typeof(SpectralCardClause), "spectrals", true),
        ["spectralCards"] = new(typeof(SpectralCardClause), "spectrals", true),
        ["tarotCard"] = new(typeof(TarotCardClause), "tarots", true),
        ["tarotCards"] = new(typeof(TarotCardClause), "tarots", true),
        ["standardCard"] = new(typeof(StandardCardClause), "label", false),
        ["standardCards"] = new(typeof(StandardCardClause), "label", false),
        ["erraticRank"] = new(typeof(ErraticRankClause), "rank", false),
        ["erraticRanks"] = new(typeof(ErraticRankClause), "rank", false),
        ["erraticSuit"] = new(typeof(ErraticSuitClause), "suit", false),
        ["erraticSuits"] = new(typeof(ErraticSuitClause), "suit", false),
        ["voucher"] = new(typeof(VoucherClause), "vouchers", true),
        ["vouchers"] = new(typeof(VoucherClause), "vouchers", true),
        ["tag"] = new(typeof(TagClause), "tags", true),
        ["tags"] = new(typeof(TagClause), "tags", true),
        ["smallBlindTag"] = new(typeof(SmallBlindTagClause), "tags", true),
        ["bigBlindTag"] = new(typeof(BigBlindTagClause), "tags", true),
        ["boss"] = new(typeof(BossClause), "bosses", true),
        ["bosses"] = new(typeof(BossClause), "bosses", true),
        ["boosterPack"] = new(typeof(BoosterPackClause), "packs", true),
        ["boosterPacks"] = new(typeof(BoosterPackClause), "packs", true),
        ["pokerHand"] = new(typeof(PokerHandClause), "pokerHands", true),
        ["pokerHands"] = new(typeof(PokerHandClause), "pokerHands", true),
        ["startingDraw"] = new(typeof(StartingDrawClause), "label", false),
        ["luckyMoney"] = new(typeof(LuckyMoneyClause), "rolls", true),
        ["luckyMult"] = new(typeof(LuckyMultClause), "rolls", true),
        ["misprintMult"] = new(typeof(MisprintMultClause), "rolls", true),
        ["wheelOfFortune"] = new(typeof(WheelOfFortuneClause), "rolls", true),
        ["wheelStaysFlipped"] = new(typeof(WheelStaysFlippedClause), "rolls", true),
        ["glassDestroy"] = new(typeof(GlassDestroyClause), "rolls", true),
        ["cavendishExtinct"] = new(typeof(CavendishExtinctClause), "rolls", true),
        ["grosMichelExtinct"] = new(typeof(GrosMichelExtinctClause), "rolls", true),
        ["spaceLevelup"] = new(typeof(SpaceLevelupClause), "rolls", true),
        ["businessPayout"] = new(typeof(BusinessPayoutClause), "rolls", true),
        ["parkingPayout"] = new(typeof(ParkingPayoutClause), "rolls", true),
        ["bloodstoneTrigger"] = new(typeof(BloodstoneTriggerClause), "rolls", true),
        ["and"] = new(typeof(AndClause), "clauses", true),
        ["or"] = new(typeof(OrClause), "clauses", true),
    };

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

        var start = reader.Consume<MappingStart>();
        var entries = new List<(Scalar Key, List<ParsingEvent> Value)>();
        while (!reader.TryConsume<MappingEnd>(out _))
            entries.Add((reader.Consume<Scalar>(), ReadNode(reader)));

        int index = entries.FindIndex(e => Keys.ContainsKey(e.Key.Value));
        if (index < 0)
            throw new YamlException(start.Start, start.End,
                $"no clause key among: {string.Join(", ", entries.Select(e => e.Key.Value))}.");

        var (wire, wireValue) = entries[index];
        var key = Keys[wire.Value];
        entries.RemoveAt(index);

        if (wireValue[0] is MappingStart)
        {
            // `- or: { mode: max, clauses: [...] }`: the block is the clause's own keys.
            var block = new EventReplay(wireValue);
            block.Consume<MappingStart>();
            while (!block.TryConsume<MappingEnd>(out _))
                AddOnce(entries, (block.Consume<Scalar>(), ReadNode(block)), wire);
        }
        else if (!IsNull(wireValue))
        {
            if (key.ValueIsList && wireValue[0] is Scalar scalar)
                wireValue = scalar.Value.Trim().Equals("any", StringComparison.OrdinalIgnoreCase)
                    ? [Sequence(scalar), new SequenceEnd(scalar.Start, scalar.End)]
                    : [Sequence(scalar), scalar, new SequenceEnd(scalar.Start, scalar.End)];
            var property = new Scalar(AnchorName.Empty, TagName.Empty, key.ValueProperty,
                ScalarStyle.Plain, true, false, wire.Start, wire.End);
            AddOnce(entries, (property, wireValue), wire);
        }

        var events = new List<ParsingEvent> { start };
        foreach (var (k, v) in entries)
        {
            events.Add(k);
            events.AddRange(v);
        }
        events.Add(new MappingEnd(start.Start, start.End));

        value = nestedObjectDeserializer(new EventReplay(events), key.Type);
        return true;
    }

    private static SequenceStart Sequence(Scalar at) =>
        new(AnchorName.Empty, TagName.Empty, true, SequenceStyle.Flow, at.Start, at.End);

    private static bool IsNull(List<ParsingEvent> node) =>
        node is [Scalar { Style: ScalarStyle.Plain, Value: "" or "~" or "null" or "Null" or "NULL" }];

    private static void AddOnce(
        List<(Scalar Key, List<ParsingEvent> Value)> entries,
        (Scalar Key, List<ParsingEvent> Value) entry,
        Scalar wire
    )
    {
        if (entries.Find(e => e.Key.Value.Equals(entry.Key.Value, StringComparison.OrdinalIgnoreCase)) is { Key: { } own })
            throw new YamlException(own.Start, own.End,
                $"`{wire.Value}:` already sets `{entry.Key.Value}`; it is also written as its own key.");
        entries.Add(entry);
    }

    /// <summary>One whole node's events: a scalar, or a collection through its matching end.</summary>
    private static List<ParsingEvent> ReadNode(IParser reader)
    {
        var events = new List<ParsingEvent>();
        int depth = 0;
        do
        {
            var current = reader.Current ?? throw new YamlException("YAML: the document ends inside a clause.");
            depth += current.NestingIncrease;
            events.Add(current);
            reader.MoveNext();
        } while (depth > 0);
        return events;
    }

    /// <summary>A recorded event list played back as a parser.</summary>
    private sealed class EventReplay(List<ParsingEvent> events) : IParser
    {
        private int index;

        public ParsingEvent? Current => index < events.Count ? events[index] : null;

        public bool MoveNext() => ++index < events.Count;
    }
}

[YamlStaticContext]
[YamlSerializable(typeof(IMotelyClause))]
public partial class JamlYamlContext : YamlDotNet.Serialization.StaticContext;
