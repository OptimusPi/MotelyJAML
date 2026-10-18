using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary><c>and:</c> as a child list or as <c>{ mode, clauses }</c> under the same JAML key.</summary>
[YamlSerializable]
public sealed class AndPayload
{
    public LogicScoreMode Mode { get; set; } = LogicScoreMode.Sum;
    public IMotelyClause[] Clauses { get; set; } = [];
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
}

[YamlSerializable]
internal sealed class AndPayloadMapping
{
    public LogicScoreMode Mode { get; set; } = LogicScoreMode.Sum;
    public IMotelyClause[] Clauses { get; set; } = [];
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
}

internal sealed class AndPayloadConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(AndPayload);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
    {
        if (parser.Current is SequenceStart)
            return new AndPayload { Clauses = (IMotelyClause[])nested(typeof(IMotelyClause[]))! };

        var mapped = (AndPayloadMapping)nested(typeof(AndPayloadMapping))!;
        return new AndPayload
        {
            Mode = mapped.Mode,
            Clauses = mapped.Clauses,
            Label = mapped.Label,
            Min = mapped.Min,
            Max = mapped.Max,
            Score = mapped.Score,
        };
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer nested) =>
        nested(value, type);
}
