using Motely.Filters.Jaml;

namespace Motely.Filters;

public enum JamlLogicScoreMode
{
    Sum = 0,
    Max = 1,
}

public abstract class LogicClause : IJamlClause
{
    public static readonly string[] ClauseKeys =
        ["min", "max", "score", "label", "clauses", "mode"];

    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public JamlLogicScoreMode Mode { get; set; } = JamlLogicScoreMode.Sum;

    public IJamlClause[] Clauses { get; set; } = [];
}
