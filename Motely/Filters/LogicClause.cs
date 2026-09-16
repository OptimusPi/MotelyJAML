using Motely.Filters.Jaml;

namespace Motely.Filters;

/// <summary>
/// How an <c>or:</c> combines arm scores (deep shop chunks, multi-ante arms, …).
/// <see cref="Sum"/> totals every arm that hits; <see cref="Max"/> scores only the best arm
/// ("land on the best reroll window"). Default is <see cref="Sum"/> (historic behavior).
/// Stored on <see cref="LogicClause"/> for both <c>or</c>/<c>and</c> wire keys; AND match
/// semantics stay min-of-children conjunctions — mode does not reopen sum-of-children AND.
/// </summary>
public enum JamlLogicScoreMode
{
    Sum = 0,
    Max = 1,
}

public abstract class LogicClause : IJamlClause
{
    /// <summary>Shared by AndClause/OrClause. No antes: each child clause writes its own.
    /// <c>mode</c> is sum|max for <c>or:</c>.</summary>
    public static readonly string[] ClauseKeys =
        ["min", "max", "score", "label", "clauses", "mode"];

    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public JamlLogicScoreMode Mode { get; set; } = JamlLogicScoreMode.Sum;

    public IJamlClause[] Clauses { get; set; } = [];
}
