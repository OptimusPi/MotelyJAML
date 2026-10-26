namespace Motely.Filters;

/// <summary>
/// One filter document: who wrote it, which deck and stake, the seeds to replay, and the
/// must/should/mustNot clauses. Every default lives here in an initializer; the loader only
/// assigns keys the document actually wrote.
/// </summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial record JamlConfig
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }

    /// <summary>When the filter was written, as its author's tool wrote it. Metadata: the search never reads it.</summary>
    public string? DateCreated { get; set; }
    public MotelyDeck Deck { get; set; } = MotelyDeck.Red;
    public MotelyStake Stake { get; set; } = MotelyStake.White;
    public List<string> Seeds { get; set; } = [];

    /// <summary>
    /// Fixed score floor: only seeds scoring at least this are reported. When set it replaces the
    /// auto (running-maximum) cutoff; null leaves the cutoff to the host (auto by default). An
    /// explicit host cutoff (the CLI's <c>--cutoff</c>) still wins over this.
    /// </summary>
    public int? Cutoff { get; set; }

    /// <summary>Values for keys the clauses leave out (<see cref="JamlClauseDefaults"/>); the loader has already filled them in.</summary>
    public JamlClauseDefaults? Defaults { get; set; }

    public List<IMotelyClause> Must { get; set; } = [];
    public List<IMotelyClause> Should { get; set; } = [];
    public List<IMotelyClause> MustNot { get; set; } = [];

    public bool HasAnyClauses() => Must.Count != 0 || Should.Count != 0 || MustNot.Count != 0;
}
