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
    /// <summary>Shared by AndClause/OrClause — complete clause-level keys.
    /// Parent <c>antes</c> are stored here and pass through every nested arm that did not
    /// override (loader + CreateSettings re-hoist). <c>mode</c> is sum|max for <c>or:</c>.</summary>
    public static readonly string[] ClauseKeys =
        ["min", "max", "score", "label", "ante", "antes", "clauses", "mode"];

    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public JamlLogicScoreMode Mode { get; set; } = JamlLogicScoreMode.Sum;

    /// <summary>
    /// Parent ante scope for this <c>and:</c>/<c>or:</c>. Empty = no parent scope (children
    /// keep their own or get default 1..8 later). Non-empty passes into every nested clause
    /// that left <c>antes</c> empty — including nested logic — unless a child overrides.
    /// </summary>
    public int[] Antes { get; set; } = [];

    public IJamlClause[] Clauses { get; set; } = [];
}
