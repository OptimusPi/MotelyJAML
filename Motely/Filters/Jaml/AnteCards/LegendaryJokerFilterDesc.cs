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
    public LegendaryJokerSourceConfig? Sources { get; set; }

    public bool SoulCardOnly { get; set; }

    public int SoulEditionRolls { get; set; }
}

public struct LegendaryJokerFilterDesc(LegendaryJokerClause clause)
    : IMotelySeedFilterDesc<LegendaryJokerFilterDesc.LegendaryJokerFilter>
{
    private readonly LegendaryJokerClause _clause = clause;

    public static string[] Discriminators => ["legendaryJoker", "legendaryJokers"];

    public static string[] ClauseKeys =>
        ["min", "max", "score", "label", "ante", "antes", "sources", "edition", "joker", "jokers", "soulCardOnly", "soulEditionRolls"];

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
            Debug.Assert(_clause.Min > 0, "LegendaryJokerClause.Min must be > 0 — loader bug.");

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

[YamlObject]
public sealed partial record LegendaryJokerSourceConfig
{
    public static readonly string[] SourceKeys =
        ["boosterPacks", "arcanaPacks", "spectralPacks", "requireMega", "requireMegaPack"];

    public int[] BoosterPacks { get; set; } = [];

    public int[] ArcanaPacks { get; set; } = [];

    public int[] SpectralPacks { get; set; } = [];

    public bool RequireMegaPack { get; set; }

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
