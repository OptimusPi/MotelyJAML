using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Motely.Filters;

/// <summary><c>or:</c> as a child list or as <c>{ mode, clauses }</c> under the same YAML key.</summary>
[YamlSerializable]
public sealed class OrPayload
{
    public LogicScoreMode Mode { get; set; } = LogicScoreMode.Sum;
    public IMotelyClause[] Clauses { get; set; } = [];
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
}

[YamlSerializable]
internal sealed class OrPayloadMapping
{
    public LogicScoreMode Mode { get; set; } = LogicScoreMode.Sum;
    public IMotelyClause[] Clauses { get; set; } = [];
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
}

internal sealed class OrPayloadConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(OrPayload);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nested)
    {
        if (parser.Current is SequenceStart)
            return new OrPayload { Clauses = (IMotelyClause[])nested(typeof(IMotelyClause[]))! };

        var mapped = (OrPayloadMapping)nested(typeof(OrPayloadMapping))!;
        return new OrPayload
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
