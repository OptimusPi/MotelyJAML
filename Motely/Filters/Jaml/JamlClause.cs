namespace Motely.Filters.Jaml;

/// <summary>Common scalar contract shared by all flat clause types.</summary>
public interface IJamlClause
{
    string? Label { get; set; }
    int Min { get; set; }
    int? Max { get; set; }
    int Score { get; set; }
}

/// <summary>
/// Capability for clauses scoped to specific antes (cards/features). Event clauses do NOT
/// implement this — they are roll-scoped, not ante-scoped. This is a capability interface a
/// clause opts into, never a base class: there is no "all clauses have antes" — events don't.
/// </summary>
public interface IAnteScopedClause : IJamlClause
{
    int[] Antes { get; set; }
}

/// <summary>
/// Capability for clauses scoped to specific PRNG roll indices instead of antes (the 12 event
/// clauses — LuckyMoney, WheelOfFortune, MisprintMult, etc.). The rolls are the discriminator's
/// own bare value (<c>luckyMoney: [0, 1, 2]</c>, read via <c>node.GetIntArray(discriminator)</c>
/// in JamlConfigLoader), not a nested key — unlike IAnteScopedClause's `ante`/`antes` keys.
/// </summary>
public interface IRollScopedClause : IJamlClause
{
    int[] Rolls { get; set; }
}

/// <summary>
/// Capability for clauses that accept a <c>with:</c> luck/voucher block. Populate assigns
/// through this interface — no per-type ApplyWith switch.
/// </summary>
public interface IWithScopedClause : IJamlClause
{
    JamlWith With { get; set; }
}

public static class JamlClause
{
    /// <summary>Allowed keys inside a clause's own <c>with:</c> block (JamlWith modifiers) —
    /// only meaningful on families whose FilterDesc <c>ClauseKeys</c> list includes <c>with</c>.</summary>
    public static readonly string[] WithBlockKeys = ["luck", "vouchers"];
}
