namespace Motely.Filters.Jaml;

/// <summary>
/// JAML document bag: id/deck/stake/must/should/mustNot. Grammar lives on FilterDescs and
/// source-config shapes colocated with those descs — not here.
/// </summary>
public sealed record JamlConfig
{
    /// <summary>The document root's own keys. "dateCreated" has no backing property here — it's
    /// accepted as human/tooling metadata only, never read by the loader; not a drift bug.</summary>
    public static readonly string[] RootKeys =
        ["id", "name", "description", "author", "dateCreated", "deck", "stake", "seeds", "filter", "must", "should", "mustNot"];

    public required string Id { get; set; }
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

/// <summary>
/// A clause's <c>with:</c> block — its modifiers. The third axis alongside WHAT (the value) and
/// WHERE (<c>sources</c>): HOW the roll behaves under owned run-state. Two kinds:
/// <list type="bullet">
///   <item><b>Probability</b> — <see cref="Luck"/>: same roll, better odds (Oops! All 6s).</item>
///   <item><b>Availability</b> — <see cref="Vouchers"/>: things appear that otherwise can't.</item>
/// </list>
/// </summary>
public sealed record JamlWith
{
    /// <summary>Oops! All 6s multiplier — each Oops doubles the odds (X2, X4, X8…). X1 = base odds.</summary>
    public MotelyLuck Luck { get; set; } = MotelyLuck.X1;

    /// <summary>
    /// Vouchers assumed owned, changing availability/rates this clause can see (e.g. Omen Globe →
    /// Spectrals in Arcana packs). Wiring each voucher into the affected source streams is
    /// per-voucher work — not done by declaring it here.
    /// </summary>
    public MotelyVoucher[] Vouchers { get; set; } = [];
}

public static class JamlConfigExtensions
{
    public static bool HasAnyClauses(this JamlConfig config) =>
        config.Must.Count != 0 || config.Should.Count != 0 || config.MustNot.Count != 0;
}
