using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace Motely.Analysis;

public sealed record class MotelyUnitTestAnalysisConfig(
    string Seed,
    MotelyDeck Deck,
    MotelyStake Stake
);

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

        if (!string.IsNullOrEmpty(ErraticDeckComposition))
        {
            sb.AppendLine($"Erratic Deck Composition: {ErraticDeckComposition}");
            if (!string.IsNullOrEmpty(ErraticDeckBreakdown))
            {
                sb.AppendLine(ErraticDeckBreakdown);
            }
            sb.AppendLine();
        }

        foreach (var ante in Antes)
        {
            sb.AppendLine($"==ANTE {ante.Ante}==");

            if (!string.IsNullOrEmpty(ante.DrawOrder))
            {
                sb.AppendLine($"Draw: {ante.DrawOrder}");
            }

            sb.AppendLine($"Boss: {FormatUtils.FormatBoss(ante.Boss)}");
            sb.AppendLine($"Voucher: {FormatUtils.FormatVoucher(ante.Voucher)}");

            sb.AppendLine(
                $"Tags: {FormatUtils.FormatTag(ante.SmallBlindTag)}, {FormatUtils.FormatTag(ante.BigBlindTag)}"
            );

            sb.AppendLine("Shop Queue: ");
            foreach ((int i, MotelyAnalyzedItem item) in ante.ShopQueue.Index())
            {
                sb.AppendLine($"{i + 1}) {FormatUtils.FormatItem(item.Item)}");
            }
            sb.AppendLine();

            sb.AppendLine("Packs: ");
            foreach (var pack in ante.Packs)
            {
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

[EditorBrowsable(EditorBrowsableState.Never)]
public static partial class MotelyUnitTestAnalyzer
{
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
                    .WithSeedList([cfg.Seed])
                    .WithThreadCount(1);

            using var search = searchSettings.CreateSearch();
            search.Start();
            search.AwaitCompletion();

            Debug.Assert(filterDesc.LastAnalysis != null);

            return filterDesc.LastAnalysis;
        }
        catch (Exception ex)
        {
            return new MotelyUnitTestAnalysis(ex.ToString(), []);
        }
    }
}
