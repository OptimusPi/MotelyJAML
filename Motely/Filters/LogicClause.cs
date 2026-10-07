namespace Motely.Filters;

/// <summary>
/// How an <c>or</c> combines arm scores. <see cref="Sum"/> totals every arm that hits;
/// <see cref="Max"/> scores only the best arm. AND match semantics stay min-of-children
/// conjunctions — mode does not reopen sum-of-children AND.
/// </summary>
public enum LogicScoreMode
{
    Sum = 0,
    Max = 1,
}

public abstract class LogicClause : IMotelyClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
    public LogicScoreMode Mode { get; set; } = LogicScoreMode.Sum;

    /// <summary>No antes here: each child clause carries its own.</summary>
    public IMotelyClause[] Clauses { get; set; } = [];

    public abstract IMotelySeedFilterDesc CreateFilterDesc();
}

public sealed class AndClause : LogicClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() =>
        new AndFilterDesc([.. Clauses.Select(c => c.CreateFilterDesc())]);
}

public sealed class OrClause : LogicClause
{
    public override IMotelySeedFilterDesc CreateFilterDesc() =>
        new OrFilterDesc([.. Clauses.Select(c => c.CreateFilterDesc())], Min);
}
