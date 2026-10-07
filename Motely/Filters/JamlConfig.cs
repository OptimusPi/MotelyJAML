using VYaml.Annotations;

namespace Motely.Filters;

/// <summary>
/// One filter document: who wrote it, which deck and stake, the seeds to replay, and the
/// must/should/mustNot clauses. Every default lives here in an initializer; the loader only
/// assigns keys the document actually wrote.
/// </summary>
[YamlObject]
public sealed partial record JamlConfig
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public MotelyDeck Deck { get; set; } = MotelyDeck.Red;
    public MotelyStake Stake { get; set; } = MotelyStake.White;
    public List<string> Seeds { get; set; } = [];

    public List<IMotelyClause> Must { get; set; } = [];
    public List<IMotelyClause> Should { get; set; } = [];
    public List<IMotelyClause> MustNot { get; set; } = [];

    public bool HasAnyClauses() => Must.Count != 0 || Should.Count != 0 || MustNot.Count != 0;
}
