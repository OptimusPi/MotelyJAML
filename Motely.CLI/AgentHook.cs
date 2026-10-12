using System.Text.Json;
using System.Text.RegularExpressions;

namespace Motely.CLI;

/// <summary>
/// <c>motely hook</c>: a post-edit hook for coding agents (Claude Code, and VS Code agent plugins
/// in the Claude layout). The event arrives as JSON on stdin; when the edited file is a Motely
/// filter that no longer loads, the error goes to stderr with exit code 2, which is how a hook
/// hands feedback back to the agent. Anything else, including YAML that isn't a filter, exits 0.
/// </summary>
internal static partial class AgentHook
{
    private const int ExitFeedback = 2;

    public static int Run()
    {
        if (!TryReadEditedPath(Console.In.ReadToEnd(), out var path))
            return 0;
        if (!path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (!File.Exists(path))
            return 0;

        var text = File.ReadAllText(path);
        if (!IsFilter(path, text))
            return 0;
        if (FilterCheck.TryCheck(text, out _, out _, out var error))
            return 0;

        Console.Error.WriteLine($"Motely filter {path} no longer loads: {error}");
        return ExitFeedback;
    }

    /// <summary>The edited file from the event's <c>tool_input</c>, made absolute against its <c>cwd</c>.</summary>
    private static bool TryReadEditedPath(string json, out string path)
    {
        path = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tool_input", out var input)
                || input.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var name in (ReadOnlySpan<string>)["file_path", "filePath", "path"])
            {
                if (input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } found)
                {
                    path = found;
                    break;
                }
            }
            if (path.Length == 0)
                return false;

            if (!Path.IsPathRooted(path)
                && root.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String
                && cwd.GetString() is { Length: > 0 } directory)
                path = Path.Combine(directory, path);
            path = Path.GetFullPath(path);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Filters live under YamlFilters/, or carry a root must:/should:/mustNot: list. Other YAML
    /// (CI configs, compose files) is none of this hook's business.
    /// </summary>
    private static bool IsFilter(string path, string text) =>
        path.Replace('\\', '/').Contains("/YamlFilters/", StringComparison.OrdinalIgnoreCase)
        || ClauseListKey().IsMatch(text);

    [GeneratedRegex(@"^(must|should|mustNot)\s*:", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ClauseListKey();
}
