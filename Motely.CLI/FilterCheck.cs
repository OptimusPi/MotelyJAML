using System.Diagnostics.CodeAnalysis;
using Motely.Filters;

namespace Motely.CLI;

/// <summary>
/// The one filter check behind every front door (<c>motely check</c>, the agent hook, the MCP
/// <c>validate_filter</c> tool): the loader's verdict first, then the search plan's, because some
/// mistakes only surface when the plan is built.
/// </summary>
internal static class FilterCheck
{
    /// <summary>A real path (full or relative) is used as given; a bare name is YamlFilters/&lt;name&gt;.yaml.</summary>
    public static string ResolvePath(string filter) =>
        File.Exists(filter) ? filter : Path.Combine("YamlFilters", filter + ".yaml");

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

    /// <summary><c>motely check</c>: 0 when every file loads, 1 when any doesn't.</summary>
    public static int Run(IReadOnlyList<string> filters)
    {
        int failed = 0;
        foreach (var filter in filters)
        {
            var path = ResolvePath(filter);
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
        return failed == 0 ? 0 : 1;
    }
}
