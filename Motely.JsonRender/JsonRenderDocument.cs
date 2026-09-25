using System.Text.Json;
using System.Text.Json.Serialization;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters.Jaml;

namespace Motely.JsonRender;

public static class JsonRenderDocument
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static RenderReport Build(
        JamlConfig config,
        IReadOnlyList<MotelyJamlyzerSeedResult> results,
        int eventRolls
    ) =>
        new(
            new RenderFilter(config.Id, config.Name, config.Description, config.Author),
            config.Deck,
            config.Stake,
            eventRolls,
            DateTimeOffset.UtcNow,
            [.. results.Select(BuildSeed)]
        );

    public static string ToJson(RenderReport report) => JsonSerializer.Serialize(report, Options);

    public static void WriteJson(RenderReport report, string path)
    {
        EnsureParentDir(path);
        File.WriteAllText(path, ToJson(report) + "\n");
    }

    internal static void EnsureParentDir(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static RenderSeed BuildSeed(MotelyJamlyzerSeedResult seed) =>
        new(
            seed.Seed,
            seed.Score,
            [.. seed.Antes.Select(BuildAnte)],
            seed.Events,
            seed.StreamStates,
            seed.ErraticDeck is { Length: > 0 } deck ? [.. deck.Select(BuildItem)] : null
        );

    private static RenderAnte BuildAnte(MotelyJamlyzerAnteResult ante) =>
        new(
            ante.Ante,
            ante.Boss,
            ante.Voucher,
            ante.SmallBlindTag,
            ante.BigBlindTag,
            [.. ante.ShopItems.Select(BuildItem)],
            [.. ante.Packs.Select(p => new RenderPack(p.Pack, [.. p.Items.Select(BuildItem)]))],
            BuildPulls(ante.Pulls),
            BuildShopStreams(ante.ShopStreams)
        );

    private static RenderPulls BuildPulls(MotelyJamlyzerPulls pulls) =>
        new(
            [.. pulls.JudgementJokers.Select(BuildItem)],
            [.. pulls.WraithJokers.Select(BuildItem)],
            [.. pulls.EmperorTarots.Select(BuildItem)],
            [.. pulls.PurpleSealTarots.Select(BuildItem)],
            [.. pulls.SixthSenseSpectrals.Select(BuildItem)],
            [.. pulls.SeanceSpectrals.Select(BuildItem)],
            [.. pulls.RiffRaffJokers.Select(BuildItem)],
            [.. pulls.RareTagJokers.Select(BuildItem)],
            [.. pulls.UncommonTagJokers.Select(BuildItem)],
            [.. pulls.LegendaryJokers.Select(BuildItem)],
            pulls.VoucherSequence
        );

    private static RenderShopStreams BuildShopStreams(MotelyJamlyzerShopStreams streams) =>
        new(
            [.. streams.ShopJokers.Select(BuildItem)],
            [.. streams.CommonShopJokers.Select(BuildItem)],
            [.. streams.UncommonShopJokers.Select(BuildItem)],
            [.. streams.RareShopJokers.Select(BuildItem)],
            [.. streams.ShopTarots.Select(BuildItem)],
            [.. streams.ShopPlanets.Select(BuildItem)],
            [.. streams.ShopSpectrals.Select(BuildItem)]
        );

    internal static RenderItem BuildItem(MotelyItem item)
    {
        var stickers = new List<string>(3);
        if (item.IsEternal)
            stickers.Add("Eternal");
        if (item.IsPerishable)
            stickers.Add("Perishable");
        if (item.IsRental)
            stickers.Add("Rental");

        bool isStandardCard = item.TypeCategory == MotelyItemTypeCategory.Standardcard;
        return new RenderItem(
            FormatUtils.FormatItem(item),
            item.Type,
            item.TypeCategory,
            item.Edition,
            item.Enhancement,
            item.Seal,
            stickers,
            isStandardCard ? item.StandardcardSuit.ToString() : null,
            isStandardCard ? item.StandardcardRank.ToString() : null
        );
    }
}
