using System.Diagnostics;
using System.Runtime.CompilerServices;
using Motely;

namespace Motely.Filters;

/// <summary>
/// <c>legendaryJoker:</c> — the legendary a Soul card gives.
///
/// Balatro keys the legendary pool <c>Joker4</c> with no ante, so the face is decided by how many
/// Souls the player has opened in the whole run, not by the ante the Soul sits in. The Souls this
/// clause targets (<see cref="Antes"/> × <see cref="Sources"/>) are the ones the player opens, in
/// ante then pack order: the first gets <c>Joker4</c> draw 0, the next draw 1, across antes. Leaving
/// a Soul out of scope is skipping it — <c>antes: [1, 8]</c> skips ante 2's Soul. The edition still
/// comes from the ante's own <c>edisou{ante}</c> stream.
/// </summary>
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class LegendaryJokerClause: IMotelyClause, IAnteScopedClause, IWithScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; } = 1;
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyJoker[] Jokers { get; set; } = [];
    public MotelyItemEdition? Edition { get; set; }
    /// <summary>Null = apply <see cref="LegendaryJokerFilterDesc.DefaultSources"/>; an explicit block
    /// overrides wholesale (same convention as every other clause's <c>Sources</c>).</summary>
    public LegendaryJokerSourceConfig? Sources { get; set; }

    /// <summary>
    /// Which opened Souls count: 0 is the first Soul opened, 1 the second, and so on through the
    /// run. Empty = every opened Soul. <c>rolls: [0, 1], min: 2</c> with Perkeo = the first two Souls
    /// both give Perkeo (sell the first before opening the second, or it resamples — see <see cref="With"/>).
    /// </summary>
    public int[] Rolls { get; set; } = [];

    /// <summary><c>with: { jokers: [Perkeo] }</c> — legendaries held while opening Souls drop out of
    /// the pool and resample; holding Showman keeps them in.</summary>
    public MotelyWith With { get; set; } = new();

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

    public IMotelySeedFilterDesc CreateFilterDesc() => new LegendaryJokerFilterDesc(this);
}

public struct LegendaryJokerFilterDesc(LegendaryJokerClause clause)
    : IMotelySeedFilterDesc<LegendaryJokerFilterDesc.LegendaryJokerFilter>
{
    private readonly LegendaryJokerClause _clause = clause;

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
            Max = _clause.Max,
            Rolls = _clause.Rolls,
            With = _clause.With,
            SoulCardOnly = _clause.SoulCardOnly,
            SoulEditionRolls = _clause.SoulEditionRolls,
            Sources = new LegendaryJokerSourceConfig
            {
                BoosterPacks = src.BoosterPacks,
                ArcanaPacks = src.ArcanaPacks,
                SpectralPacks = src.SpectralPacks,
                TagPacks = src.TagPacks,
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
            // pack/Soul/face exact law runs last in YAMLShouldScoreDesc must re-eval.
            //
            // Stage 1: soul edition stream (when edition: is set). Negative ~0.3% — rejects almost
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
[YamlDotNet.Serialization.YamlSerializable]
public sealed partial record LegendaryJokerSourceConfig
{
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

    /// <summary>
    /// Charm Tag Mega Arcana packs, opened at blind select before the ante's shop: 0 = small-blind
    /// tag, 1 = big-blind tag. Each takes the next 5 draws of the ante's ar&lt;ante&gt; / soul_Tarot&lt;ante&gt;
    /// streams, so a Soul in the Charm is opened before any shop-pack Soul. Listing a pack assumes
    /// the player skipped that blind for a Charm Tag; pair it with a <c>smallBlindTag</c>/<c>bigBlindTag</c>
    /// must. Mirrors <see cref="TarotCardSourceConfig.TagPacks"/>; always Mega.
    /// </summary>
    public int[] TagPacks { get; set; } = [];

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
