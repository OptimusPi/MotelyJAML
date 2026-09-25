using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Motely.Analysis;
using Motely.Enums;
using Motely.Filters.Jaml;

namespace Motely.JsonRender;

public static class JamlUiJsonRenderer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static typeInfo =>
                {
                    if (typeInfo.Type != typeof(MotelyItem))
                        return;
                    var extra = typeInfo.Properties.FirstOrDefault(p => p.Name == "isInvalid");
                    if (extra is not null)
                        typeInfo.Properties.Remove(extra);
                },
            },
        },
    };

    private sealed record JamlUiFilter(string Id, string? Name);

    private sealed record JamlUiReport(
        JamlUiFilter Filter,
        MotelyDeck Deck,
        MotelyStake Stake,
        int EventRolls,
        IReadOnlyList<MotelyJamlyzerSeedResult> Seeds
    );

    public static void Write(
        JamlConfig config,
        IReadOnlyList<MotelyJamlyzerSeedResult> results,
        int eventRolls,
        string path
    )
    {
        var report = new JamlUiReport(
            new JamlUiFilter(config.Id, config.Name),
            config.Deck,
            config.Stake,
            eventRolls,
            results
        );
        JsonRenderDocument.EnsureParentDir(path);
        File.WriteAllText(path, JsonSerializer.Serialize(report, Options) + "\n");
    }
}
