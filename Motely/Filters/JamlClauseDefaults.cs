namespace Motely.Filters;

/// <summary>
/// A filter's <c>defaults:</c> block: values for keys its clauses leave out. Each key fills in
/// every clause — in <c>must</c>, <c>should</c>, <c>mustNot</c> and inside <c>and:</c>/<c>or:</c>,
/// written as a mapping or as shorthand — that takes the key and does not write it, exactly as if
/// the clause had written it. A clause that writes the key keeps its own value.
/// <code>
/// defaults:
///   antes: [1, 2]      # every clause that looks in antes and names none
///   score: 5           # every clause that names no score
/// </code>
/// </summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial record JamlClauseDefaults
{
    /// <summary>Antes for each clause that looks in antes (not the event clauses, <c>soulOrder</c>, or <c>and</c>/<c>or</c>, which pass none down) and names none.</summary>
    public int[]? Antes { get; set; }

    /// <summary><c>ante:</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }

    /// <summary>Score for each clause that names none.</summary>
    public int? Score { get; set; }

    /// <summary>Fills the defaults into the clauses the loader saw leave the keys out.</summary>
    internal void Apply(IEnumerable<ClauseTypeDiscriminatingNodeDeserializer.Unstated> unstated)
    {
        foreach (var (clause, antes, score) in unstated)
        {
            if (antes && Antes is { } a && clause is IAnteScopedClause scoped)
                scoped.Antes = [.. a];
            if (score && Score is { } s)
                clause.Score = s;
        }
    }
}
