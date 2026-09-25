namespace Motely.Filters.Jaml;

[YamlObject]
public sealed partial record JamlConfig
{
    public static readonly string[] RootKeys =
        ["id", "name", "description", "author", "dateCreated", "deck", "stake", "seeds", "filter", "must", "should", "mustNot"];

    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Author { get; set; }
    public MotelyDeck Deck { get; set; } = MotelyDeck.Red;
    public MotelyStake Stake { get; set; } = MotelyStake.White;
    public List<string> Seeds { get; set; } = [];
    public string? Filter { get; set; }

    public List<IJamlClause> Must { get; set; } = [];
    public List<IJamlClause> Should { get; set; } = [];
    public List<IJamlClause> MustNot { get; set; } = [];
}

[YamlObject]
public sealed partial record JamlWith
{
    public MotelyLuck Luck { get; set; } = MotelyLuck.X1;
}

public static class JamlConfigExtensions
{
    public static bool HasAnyClauses(this JamlConfig config) =>
        config.Must.Count != 0 || config.Should.Count != 0 || config.MustNot.Count != 0;
}
