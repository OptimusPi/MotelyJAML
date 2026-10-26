namespace Motely.Filters;

// Second spellings of JAML keys, each bound to the key it spells. They are properties, so the
// schema, the editor and the loader all know them from the types themselves. A clause that writes
// both spellings is refused (ClauseTypeDiscriminatingNodeDeserializer), so neither silently wins.
// The getters are null: a document carries one spelling, and the canonical property holds it.


public sealed partial class CommonJokerClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class ErraticRankClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class ErraticSuitClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class JokerClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class LegendaryJokerClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class PlanetCardClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class RareJokerClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class SpectralCardClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class StandardCardClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class TarotCardClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class UncommonJokerClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class BoosterPackClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class BossClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class PokerHandClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class StartingDrawClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public partial class TagClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial class VoucherClause
{
    /// <summary><c>ante: 2</c> or <c>ante: [1, 2]</c> is <c>antes:</c>.</summary>
    public int[]? Ante { get => null; set => Antes = value ?? Antes; }
}

public sealed partial record JokerSourceConfig
{
    /// <summary><c>requireMega:</c> is <c>requireMegaPack:</c>.</summary>
    public bool? RequireMega { get => null; set => RequireMegaPack = value ?? RequireMegaPack; }
}

public sealed partial record LegendaryJokerSourceConfig
{
    /// <summary><c>requireMega:</c> is <c>requireMegaPack:</c>.</summary>
    public bool? RequireMega { get => null; set => RequireMegaPack = value ?? RequireMegaPack; }
}

public sealed partial record PlanetSourceConfig
{
    /// <summary><c>requireMega:</c> is <c>requireMegaPack:</c>.</summary>
    public bool? RequireMega { get => null; set => RequireMegaPack = value ?? RequireMegaPack; }
}

public sealed partial record SpectralCardSourceConfig
{
    /// <summary><c>requireMega:</c> is <c>requireMegaPack:</c>.</summary>
    public bool? RequireMega { get => null; set => RequireMegaPack = value ?? RequireMegaPack; }
}

public sealed partial record StandardCardSourceConfig
{
    /// <summary><c>requireMega:</c> is <c>requireMegaPack:</c>.</summary>
    public bool? RequireMega { get => null; set => RequireMegaPack = value ?? RequireMegaPack; }
}

public sealed partial record TarotCardSourceConfig
{
    /// <summary><c>requireMega:</c> is <c>requireMegaPack:</c>.</summary>
    public bool? RequireMega { get => null; set => RequireMegaPack = value ?? RequireMegaPack; }
}

public sealed partial class LegendaryJokerClause
{
    /// <summary><c>soulJoker:</c> (BalatroSeedOracle's name for a joker from The Soul) is <c>legendaryJoker:</c>.</summary>
    public MotelyJoker[]? SoulJoker { get => null; set => Jokers = value ?? []; }
}
