using System.Diagnostics.CodeAnalysis;
using Motely.Filters;

namespace Motely.Shared;

/// <summary>
/// The one filter check behind every front door (<c>motely check</c>, the agent hook, the MCP
/// <c>validate_filter</c> tool): the loader's verdict first, then the search plan's, because some
/// mistakes only surface when the plan is built.
/// </summary>
public static class FilterCheck
{
    /// <summary>
    /// A real path (full or relative) is used as given; a bare name is &lt;filters&gt;/&lt;name&gt;.yaml, where
    /// &lt;filters&gt; is MOTELY_FILTERS_DIR when set and YamlFilters/ otherwise. Aspire sets it: its
    /// resources run in their own project directory, not the repo root.
    /// </summary>
    public static string ResolvePath(string filter) =>
        File.Exists(filter) ? filter : Path.Combine(FiltersDirectory, filter + ".yaml");

    private static string FiltersDirectory =>
        Environment.GetEnvironmentVariable("MOTELY_FILTERS_DIR") is { Length: > 0 } directory ? directory : "YamlFilters";

    /// <summary>A directory stands for every .yaml/.yml file in it; anything else goes through <see cref="ResolvePath"/>.</summary>
    private static IEnumerable<string> Expand(string filter) =>
        Directory.Exists(filter)
            ? Directory
                .EnumerateFiles(filter)
                .Where(static f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
            : [ResolvePath(filter)];

    public static bool TryCheck(
        string text,
        [NotNullWhen(true)] out JamlConfig? config,
        [NotNullWhen(true)] out JamlSearchPlan? plan,
        [NotNullWhen(false)] out string? error
    )
    {
        plan = null;
        if (!YamlConfigLoader.TryLoad(text, out config, out var loadError))
        {
            error = loadError ?? "the filter did not load.";
            return false;
        }
        try
        {
            plan = MotelySearchBuilder.CreatePlan(config);
            error = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// <c>motely check</c>: 0 when every file loads, 1 when any doesn't. A directory checks every
    /// filter in it, and one with none in it fails rather than passing on nothing.
    /// </summary>
    public static int Run(IReadOnlyList<string> filters)
    {
        int failed = 0;
        foreach (var filter in filters)
        {
            int checkedHere = 0;
            foreach (var path in Expand(filter))
            {
                checkedHere++;
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"{path}: no such file");
                    failed++;
                    continue;
                }
                if (TryCheck(File.ReadAllText(path), out var config, out _, out var error))
                    Console.WriteLine($"{path}: ok ({config.Name ?? "unnamed"}, {config.Deck} {config.Stake})");
                else
                {
                    Console.Error.WriteLine($"{path}: {error}");
                    failed++;
                }
            }
            if (checkedHere == 0)
            {
                Console.Error.WriteLine($"{filter}: no .yaml or .yml files");
                failed++;
            }
        }
        return failed == 0 ? 0 : 1;
    }
}
