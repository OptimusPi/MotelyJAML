namespace Motely.Filters;

/// <summary>A filter's parameters: what a seed must contain, and how much a hit is worth.</summary>
public interface IMotelyClause
{
    string? Label { get; set; }
    int Min { get; set; }
    int? Max { get; set; }
    int Score { get; set; }

    /// <summary>The SIMD filter this clause installs. Each clause type names its own, so there is
    /// no list of clause types to keep in step: a clause without one does not compile.</summary>
    IMotelySeedFilterDesc CreateFilterDesc();
}

/// <summary>
/// Capability for clauses scoped to specific antes (cards/features). Event clauses do NOT
/// implement this — they are roll-scoped, not ante-scoped.
/// </summary>
public interface IAnteScopedClause : IMotelyClause
{
    int[] Antes { get; set; }
}

/// <summary>
/// Capability for clauses scoped to specific PRNG roll indices instead of antes (the event
/// clauses — LuckyMoney, WheelOfFortune, MisprintMult, etc.).
/// </summary>
public interface IRollScopedClause : IMotelyClause
{
    int[] Rolls { get; set; }
}

/// <summary>Capability for clauses that take a <see cref="MotelyWith"/> luck block.</summary>
public interface IWithScopedClause : IMotelyClause
{
    MotelyWith With { get; set; }
}

[YamlDotNet.Serialization.YamlSerializable]
public sealed partial record MotelyWith
{
    /// <summary>Oops! All 6s multiplier — each Oops doubles the odds (X2, X4, X8…). X1 = base odds.</summary>
    public MotelyLuck Luck { get; set; } = MotelyLuck.X1;
}
