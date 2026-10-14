using System.Diagnostics;
using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters;

/// <summary>
/// <c>soulOrder:</c> — the run-wide order the Souls a run opens turn into legendaries (E17).
///
/// Balatro keys the legendary pool <c>Joker4</c> with no ante, so the Nth Soul the player opens in
/// the whole run gets draw N, whatever ante it sits in. <see cref="Entries"/> is that ordered list:
/// entry <c>i</c> is the (i+1)-th Soul opened. Each entry may pin an ante range (<c>antes:</c>, empty
/// = any ante). The clause matches only when every listed Soul lands on its legendary in order.
///
/// A legendary already opened is owned, so the game resamples (<c>Joker4_resample2</c>, …) rather
/// than giving a duplicate — unless Showman is held. <c>showman: true</c> keeps duplicates in the
/// pool, so the effective sequence is the raw <c>Joker4</c> draws (the same sequence
/// <c>jamlyze_seeds</c> reports).
/// </summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class LegendarySoulOrderClause: IMotelyClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;

    /// <summary>The ordered Souls: entry <c>i</c> is the (i+1)-th Soul the run opens.</summary>
    public SoulOrderEntry[] Entries { get; set; } = [];

    /// <summary>How far the walk runs. Antes past this are ignored.</summary>
    public int MaxAnte { get; set; } = 8;

    /// <summary>Booster-pack slots the walk reads Souls from. Empty = <c>[0..5]</c>.</summary>
    public int[] BoosterPacks { get; set; } = [];

    /// <summary>
    /// Holding Showman keeps legendaries already opened in the pool, so a repeat draw stays instead
    /// of resampling. Default (false) is game-accurate: the second Soul can never repeat the first.
    /// </summary>
    public bool Showman { get; set; }

    public IMotelySeedFilterDesc CreateFilterDesc() => new LegendarySoulOrderFilterDesc(this);
}

/// <summary>One Soul in a <c>soulOrder:</c> list: the legendary it must give, and the antes it may sit in.</summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial record SoulOrderEntry
{
    public MotelyJoker Legendary { get; set; }

    /// <summary>Antes this Soul may be opened in. Empty = any ante up to the clause's <c>maxAnte</c>.</summary>
    public int[] Antes { get; set; } = [];
}

public struct LegendarySoulOrderFilterDesc(LegendarySoulOrderClause clause)
    : IMotelySeedFilterDesc<LegendarySoulOrderFilterDesc.LegendarySoulOrderFilter>
{
    private readonly LegendarySoulOrderClause _clause = clause;

    public LegendarySoulOrderFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        int maxAnte = _clause.MaxAnte > 0 ? _clause.MaxAnte : 8;
        for (int ante = 0; ante <= maxAnte; ante++)
            ctx.CacheBoosterPackStream(ante, force: true);
        return new LegendarySoulOrderFilter(_clause);
    }

    public struct LegendarySoulOrderFilter(LegendarySoulOrderClause clause) : IMotelySeedFilter
    {
        private readonly LegendarySoulOrderClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            Debug.Assert(_clause.Min > 0, "LegendarySoulOrderClause.Min must be > 0 — loader bug.");

            // Rough cut only: the run-wide Soul walk is order-dependent (it threads one Joker4
            // stream across antes), so it cannot be a pure SIMD lane mask. Keep every valid lane
            // and let the scalar scoring pass confirm (IsExactFilterConfirm = false).
            uint laneMask = 0;
            for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
                if (ctx.IsLaneValid(lane))
                    laneMask |= 1u << lane;

            return new VectorMask(laneMask);
        }
    }
}
