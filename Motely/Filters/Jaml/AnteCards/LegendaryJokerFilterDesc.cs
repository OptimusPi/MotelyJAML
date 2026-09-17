using System.Diagnostics;
using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("legendaryJoker", "legendaryJokers",
    ValueEnum = typeof(MotelyJoker), SourceConfigType = typeof(LegendaryJokerSourceConfig))]
[YamlObject]
public sealed partial class LegendaryJokerClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyJoker[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    /// <summary>Null = apply <see cref="LegendaryJokerFilterDesc.DefaultSources"/>; an explicit block
    /// overrides wholesale (same convention as every other clause's <c>Sources</c>).</summary>
    public LegendaryJokerSourceConfig? Sources { get; set; }

    /// <summary>
    /// When true, match as soon as The Soul appears in a targeted arcana/Spectral pack (Tarot/Spectral
    /// card), without rolling the legendary joker. Use for "any" + soul-card-only searches.
    /// </summary>
    public bool SoulCardOnly { get; set; }

    /// <summary>
    /// Extra soul-stream edition reads per ante for the fast edition vector prefilter (0 = use
    /// <see cref="LegendaryJokerSourceConfig.BoosterPacks"/> length). Raise when rare multi-soul antes
    /// could otherwise false-negative the prefilter.
    /// </summary>
    public int SoulEditionRolls { get; set; }
}

public struct LegendaryJokerFilterDesc(LegendaryJokerClause clause)
    : IMotelySeedFilterDesc<LegendaryJokerFilterDesc.LegendaryJokerFilter>
{
    private readonly LegendaryJokerClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["legendaryJoker", "legendaryJokers"];

    /// <inheritdoc/>
    public static string[] ClauseKeys =>

    /// <summary>Source of truth for legendary/Soul defaults when a clause gives no <c>sources:</c> block:
    /// the SIMD/vector path walks all 6 booster-pack slots (legacy type-agnostic mode — both arcana and
    /// Spectral packs gate The Soul). Shop stays empty (shops never offer legendaries). The single-seed
    /// scoring path narrows this per ante via <c>ClampBoosterPackSlotForAnte</c> (Hieroglyph/Petroglyph
    /// pack-count routing). Applied only when <c>Sources</c> is null; an explicit block overrides wholesale.</summary>
    /// <summary>
    /// Filter-layer default when Sources is null. Legendaries default to booster pack slots only
    /// (no shop). Soul/arcana/spectral/mega need explicit sources:. Loader leaves Sources null.
    /// </summary>
    internal static readonly LegendaryJokerSourceConfig DefaultSources = new()
    {
        BoosterPacks = [0, 1, 2, 3, 4, 5],
    };

    public LegendaryJokerFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var src = _clause.Sources ?? DefaultSources;

        var normalizedClause = new LegendaryJokerClause
        {
            Label = _clause.Label,
            Score = _clause.Score,
            Jokers = _clause.Jokers,
            Edition = _clause.Edition,
            Antes = _clause.Antes,
            Min = _clause.Min,
            SoulCardOnly = _clause.SoulCardOnly,
            SoulEditionRolls = _clause.SoulEditionRolls,
            Sources = new LegendaryJokerSourceConfig
            {
                BoosterPacks = src.BoosterPacks,
                ArcanaPacks = src.ArcanaPacks,
                SpectralPacks = src.SpectralPacks,
                RequireMegaPack = src.RequireMegaPack,
            },
        };

        foreach (var ante in normalizedClause.Antes)
            ctx.CacheBoosterPackStream(ante, force: true);

        // Edition prefilter applies for any Min: keep lanes that see the edition on the soul
        // stream somewhere (over-permissive for Min>1; scalar confirm enforces count).
        if (normalizedClause.Edition.HasValue)
        {
            foreach (var ante in normalizedClause.Antes)
                ctx.CacheLegendaryJokerStream(
                    ante,
                    MotelyJokerFixedRarityStreamFlags.ExcludeJokerType
                        | MotelyJokerFixedRarityStreamFlags.ExcludeStickers,
                    force: true
                );
        }

        return new LegendaryJokerFilter(normalizedClause);
    }

    public struct LegendaryJokerFilter(LegendaryJokerClause clause) : IMotelySeedFilter
    {
        private readonly LegendaryJokerClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // empty Jokers = any legendary; SoulCardOnly is separate
            Debug.Assert(_clause.Min > 0, "LegendaryJokerClause.Min must be > 0 — loader bug.");

            // Pure SIMD additional-filter link only. Never SearchIndividualSeeds here —
            // pack/Soul/face exact law runs last in JamlShouldScoreDesc must re-eval.
            //
            // Slice 1: soul edition stream (when edition: is set). Negative ~0.3% — kills almost
            // every lane before score. No edition → all valid lanes; scoring does the full walk.
            // Do not prefilter soul face before packs (order-dependent; see LegendarySoulMatcher).
            uint laneMask = 0;
            for (int lane = 0; lane < MotelyGlobals.MaxVectorWidth; lane++)
            {
                if (ctx.IsLaneValid(lane))
                    laneMask |= 1u << lane;
            }

            if (_clause.Edition.HasValue)
                return LegendarySoulEditionPrefilter.Apply(ref ctx, _clause, laneMask);

            return new VectorMask(laneMask);
        }
    }
}

/// <summary>
/// <c>sources:</c> block for <c>legendaryJoker:</c>. Colocated with
/// <see cref="LegendaryJokerFilterDesc"/> (T5).
/// </summary>
[YamlObject]
public sealed partial record LegendaryJokerSourceConfig
{
    /// <summary>
    /// requireMega/requireMegaPack are both real aliases the loader accepts for the one
    /// RequireMegaPack bool below — the only case where this list has two entries for one
    /// property, and it's a deliberate alias, not drift.
    /// </summary>
    public static readonly string[] SourceKeys =
        ["boosterPacks", "arcanaPacks", "spectralPacks", "requireMega", "requireMegaPack"];

    // No ShopItems: shops never offer legendary/Soul jokers, so a shop slot would silently match
    // nothing. The loader rejects a `shopItems:` key on legendaryJoker sources outright.

    /// <summary>
    /// Legacy: pack offering slots where The Soul may count from either arcana or Spectral path.
    /// Ignored for slot matching when <see cref="ArcanaPacks"/> or <see cref="SpectralPacks"/> is non-empty.
    /// </summary>
    public int[] BoosterPacks { get; set; } = [];

    /// <summary>
    /// If non-empty (or <see cref="SpectralPacks"/> non-empty), only listed slots are checked on the arcana/Tarot path.
    /// </summary>
    public int[] ArcanaPacks { get; set; } = [];

    /// <summary>Only listed slots on the Spectral pack path.</summary>
    public int[] SpectralPacks { get; set; } = [];

    /// <summary>If true, only Mega-sized booster packs (e.g. Charm Tag Mega arcana) match.</summary>
    public bool RequireMegaPack { get; set; }

    /// <summary>Largest referenced pack slot index across all booster source arrays (-1 if none).</summary>
    public int MaxReferencedBoosterSlot()
    {
        int m = -1;
        foreach (var x in BoosterPacks)
            if (x > m)
                m = x;
        foreach (var x in ArcanaPacks)
            if (x > m)
                m = x;
        foreach (var x in SpectralPacks)
            if (x > m)
                m = x;
        return m;
    }
}
