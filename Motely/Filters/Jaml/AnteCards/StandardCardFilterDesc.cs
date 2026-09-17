using System.Runtime.CompilerServices;

namespace Motely.Filters.Jaml;

[JamlDiscriminator("standardCard", "standardCards",
    SourceConfigType = typeof(StandardCardSourceConfig))]
[YamlObject]
public sealed partial class StandardCardClause : IJamlClause, IAnteScopedClause
{
    public string? Label { get; set; }
    public int Min { get; set; } = 1;
    public int? Max { get; set; }
    public int Score { get; set; }
    public int[] Antes { get; set; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public MotelyStandardcardRank? Rank { get; set; }
    public MotelyStandardcardSuit? Suit { get; set; }
    public MotelyItemEnhancement? Enhancement { get; set; }
    public MotelyItemSeal? Seal { get; set; }
    public MotelyItemEdition? Edition { get; set; }

    // null = no sources: in JAML → filter DefaultSources at CreateFilter/score (not parse).
    public StandardCardSourceConfig? Sources { get; set; }
}

public struct StandardCardFilterDesc(StandardCardClause clause)
    : IMotelySeedFilterDesc<StandardCardFilterDesc.StandardCardFilter>,
      IJamlClauseDesc<StandardCardClause>
{
    private readonly StandardCardClause _clause = clause;

    /// <inheritdoc/>
    public static string[] Discriminators => ["standardCard", "standardCards"];

    /// <inheritdoc/>
    public static string[] ClauseKeys => ["min", "max", "score", "label", "ante", "antes", "sources", "rank", "suit", "enhancement", "seal", "edition"];

    /// <inheritdoc/>
    public static bool Set(StandardCardClause clause, string key, IJamlValueReader value)
    {
        switch (key.ToLowerInvariant())
        {
            case "rank":
                if (!value.TryEnum<MotelyStandardcardRank>(out var rank)) return false;
                clause.Rank = rank;
                return true;
            case "suit":
                if (!value.TryEnum<MotelyStandardcardSuit>(out var suit)) return false;
                clause.Suit = suit;
                return true;
            case "enhancement":
                if (!value.TryEnum<MotelyItemEnhancement>(out var enh)) return false;
                clause.Enhancement = enh;
                return true;
            case "seal":
                if (!value.TryEnum<MotelyItemSeal>(out var seal)) return false;
                clause.Seal = seal;
                return true;
            case "edition":
                if (!value.TryEnum<MotelyItemEdition>(out var edition)) return false;
                clause.Edition = edition;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Filter-layer default when Sources is null: every booster-pack slot, Standard packs only.
    /// The shop is left out because its playing-card weight is the Magic Trick weight and no deck
    /// starts with that voucher (<see cref="JamlRarityContext.ShopStandardCardRate"/>), so a
    /// shop-only default matched nothing on every deck. Shop slots need an explicit
    /// <c>sources:</c>. Slot range is the engine's
    /// <see cref="MotelyGlobals.LateAntesMaxPackSlot"/>; scoring clamps ante 1 to its four.
    /// </summary>
    internal static readonly StandardCardSourceConfig DefaultSources = new()
    {
        BoosterPacks = Enumerable.Range(0, MotelyGlobals.LateAntesMaxPackSlot + 1).ToArray(),
    };

    public StandardCardFilter CreateFilter(ref MotelyFilterCreationContext ctx)
    {
        var sources = _clause.Sources ?? DefaultSources;
        foreach (var ante in _clause.Antes)
        {
            if (sources.ShopItems.Length > 0)
                ctx.CacheShopStream(ante);
            if (sources.BoosterPacks.Length > 0)
                ctx.CacheBoosterPackStream(ante);
        }

        return new StandardCardFilter(_clause);
    }

    public struct StandardCardFilter(StandardCardClause clause) : IMotelySeedFilter
    {
        private readonly StandardCardClause _clause = clause;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VectorMask Filter(ref MotelyVectorSearchContext ctx)
        {
            // Single match core: same PrepareRunState + count as should-scoring.
            var clause = _clause;
            return ctx.SearchIndividualSeeds(
                (MotelySingleSearchContext singleCtx) =>
                    JamlScoring.ClauseMeetsMinForFilter(ref singleCtx, clause) ? 1 : 0
            );
        }
    }
}

/// <summary>
/// <c>sources:</c> block for <c>standardCard:</c>. Colocated with <see cref="StandardCardFilterDesc"/> (T5).
/// </summary>
[YamlObject]
public sealed partial record StandardCardSourceConfig
{
    /// <summary>requireMega/requireMegaPack: both real aliases for RequireMegaPack below.</summary>
    public static readonly string[] SourceKeys =
    [
        "shopItems",
        "boosterPacks",
        "requireMega",
        "requireMegaPack",
    ];

    public int[] ShopItems { get; set; } = [];
    public int[] BoosterPacks { get; set; } = [];

    /// <summary>When true, only Mega-sized Standard packs count (Normal/Jumbo still advance the stream).</summary>
    public bool RequireMegaPack { get; set; }
}
