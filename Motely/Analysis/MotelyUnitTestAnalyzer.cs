using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace Motely.Analysis;

public sealed record class MotelyUnitTestAnalysisConfig(
    string Seed,
    MotelyDeck Deck,
    MotelyStake Stake
);

/// <summary>
/// Classic String Block Format Analysis ("The Soul" layout).
///
/// This is the LEGACY TEXT analyzer: its <see cref="ToString"/> is a flat, human-readable
/// block intended for unit-test ground-truth (Verify()) and cross-tool comparison against
/// external Balatro seed tools (miaklwalker, mathisfun_), NOT for UI rendering.
/// </summary>
public sealed record class MotelyUnitTestAnalysis(
    string? Error,
    IReadOnlyList<MotelyAnteAnalysis> Antes,
    MotelyDeck? Deck = null,
    string? ErraticDeckComposition = null,
    string? ErraticDeckBreakdown = null
)
{
    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Error))
        {
            return $"❌ Error analyzing seed: {Error}";
        }

        StringBuilder sb = new();

        // Add erratic deck composition at the top if available (Erratic deck only)
        if (!string.IsNullOrEmpty(ErraticDeckComposition))
        {
            sb.AppendLine($"Erratic Deck Composition: {ErraticDeckComposition}");
            if (!string.IsNullOrEmpty(ErraticDeckBreakdown))
            {
                sb.AppendLine(ErraticDeckBreakdown);
            }
            sb.AppendLine();
        }

        // Match TheSoul's format exactly
        foreach (var ante in Antes)
        {
            sb.AppendLine($"==ANTE {ante.Ante}==");

            // Add draw order for this ante (for all decks)
            if (!string.IsNullOrEmpty(ante.DrawOrder))
            {
                sb.AppendLine($"Draw: {ante.DrawOrder}");
            }

            sb.AppendLine($"Boss: {FormatUtils.FormatBoss(ante.Boss)}");
            sb.AppendLine($"Voucher: {FormatUtils.FormatVoucher(ante.Voucher)}");

            // Tags
            sb.AppendLine(
                $"Tags: {FormatUtils.FormatTag(ante.SmallBlindTag)}, {FormatUtils.FormatTag(ante.BigBlindTag)}"
            );

            // Shop Queue - match TheSoul format exactly: "Shop Queue: " on its own line, then numbered items
            sb.AppendLine("Shop Queue: ");
            foreach ((int i, MotelyAnalyzedItem item) in ante.ShopQueue.Index())
            {
                sb.AppendLine($"{i + 1}) {FormatUtils.FormatItem(item.Item)}");
            }
            sb.AppendLine();

            // Packs - match Immolate format exactly: "Pack Name - Card1, Card2, Card3"
            sb.AppendLine("Packs: ");
            foreach (var pack in ante.Packs)
            {
                // Format: "Pack Name - Card1, Card2, Card3"
                var contents =
                    pack.Items.Count > 0
                        ? " - "
                            + string.Join(
                                ", ",
                                pack.Items.Select(item => FormatUtils.FormatItem(item.Item))
                            )
                        : "";
                sb.AppendLine($"{FormatUtils.FormatPackName(pack.Type)}{contents}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public sealed record class MotelyAnteAnalysis(
    int Ante,
    MotelyBossBlind Boss,
    MotelyVoucher Voucher,
    MotelyTag SmallBlindTag,
    MotelyTag BigBlindTag,
    IReadOnlyList<MotelyAnalyzedItem> ShopQueue,
    IReadOnlyList<MotelyBoosterPackAnalysis> Packs,
    string? DrawOrder = null,
    bool BossMatched = false,
    bool VoucherMatched = false,
    bool SmallBlindTagMatched = false,
    bool BigBlindTagMatched = false
);

public sealed record class MotelyAnalyzedItem([property: JsonIgnore] MotelyItem Item)
{
    public string Name => FormatUtils.FormatItem(Item);
    public int Value => Item.Value;
    public MotelyItemType Type => Item.Type;
    public MotelyItemTypeCategory TypeCategory => Item.TypeCategory;
    public MotelyItemSeal Seal => Item.Seal;
    public MotelyItemEnhancement Enhancement => Item.Enhancement;
    public MotelyItemEdition Edition => Item.Edition;
    public MotelyStandardcardSuit StandardcardSuit => Item.StandardcardSuit;
    public MotelyStandardcardRank StandardcardRank => Item.StandardcardRank;
    public bool IsPerishable => Item.IsPerishable;
    public bool IsEternal => Item.IsEternal;
    public bool IsRental => Item.IsRental;
    public bool IsInvalid => Item.IsInvalid;

    public static implicit operator MotelyItem(MotelyAnalyzedItem item) => item.Item;
}

public sealed record class MotelyBoosterPackAnalysis(
    MotelyBoosterPack Type,
    IReadOnlyList<MotelyAnalyzedItem> Items
);

/// <summary>
/// Legacy text-block seed analyzer. Produces the classic "The Soul" string layout via
/// <see cref="MotelyUnitTestAnalysis.ToString"/>, intended for unit-test ground-truth and
/// cross-tool comparison (miaklwalker, mathisfun_) — NOT for UI.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class MotelyUnitTestAnalyzer
{
    /// <summary>
    /// Analyzes a seed and returns structured data
    /// </summary>
    public static MotelyUnitTestAnalysis Analyze(MotelyUnitTestAnalysisConfig cfg)
    {
        try
        {
            MotelyUnitTestAnalyzerFilterDesc filterDesc = new();

            var searchSettings =
                new MotelySearchSettings<MotelyUnitTestAnalyzerFilterDesc.LegacyTextAnalyzerFilter>(
                    filterDesc
                )
                    .WithDeck(cfg.Deck)
                    .WithStake(cfg.Stake)
                    .WithSeedList([cfg.Seed]) // Single seed analysis
                    .WithThreadCount(1);

            using var search = searchSettings.CreateSearch();
            search.Start();
            search.AwaitCompletion();

            // Don't write to Console here - the caller should handle output
            // Console.Write(filterDesc.LastAnalysis);

            // The engine drops a seed it cannot search (empty, longer than 8, or holding '0')
            // without handing it to the filter. That left LastAnalysis null and every caller
            // reading .Error off it threw NullReferenceException.
            return filterDesc.LastAnalysis
                ?? new MotelyUnitTestAnalysis(
                    $"'{cfg.Seed}' is not a searchable seed (1-{MotelyGlobals.MaxSeedLength} characters, no '0').",
                    []
                );
        }
        catch (Exception ex)
        {
            return new MotelyUnitTestAnalysis(ex.ToString(), []);
        }
    }
}
