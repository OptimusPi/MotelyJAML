using System.Diagnostics.CodeAnalysis;
using Motely.Analysis;
using Motely.Filters.Jaml;

namespace Motely.JsonRender;

public static class Program
{
    private const string Usage =
        "Usage: Motely.JsonRender --jaml <path> [--seeds A,B,C] [--rolls N] [--json <out.json>] [--html <out.html>] [--jamlui <out.json>]\n"
        + "  --jaml <path>    JAML (or JSON) filter file; a bare name resolves under JamlFilters/ like the CLI.\n"
        + "  --seeds A,B,C    Override the filter's saved top-level seeds: block.\n"
        + "  --rolls N        Event-stream rolls per seed (default 20).\n"
        + "  --json <path>    Write the JSON interchange document.\n"
        + "  --html <path>    Write the self-contained HTML report.\n"
        + "  --jamlui <path>  Write the jaml-ui dialect JSON (numeric enums, feeds JamlyzerView directly).\n"
        + "At least one of --json / --html / --jamlui is required.";

    public static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string? jamlPath = null;
        string? jsonPath = null;
        string? htmlPath = null;
        string? jamlUiPath = null;
        string? seedsArg = null;
        int eventRolls = 20;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for {arg}.\n{Usage}");
                return args[++i];
            }

            switch (arg)
            {
                case "--jaml":
                    jamlPath = Next();
                    break;
                case "--json":
                    jsonPath = Next();
                    break;
                case "--html":
                    htmlPath = Next();
                    break;
                case "--jamlui":
                    jamlUiPath = Next();
                    break;
                case "--seeds":
                    seedsArg = Next();
                    break;
                case "--rolls":
                    if (!int.TryParse(Next(), out eventRolls) || eventRolls <= 0)
                        throw new ArgumentException("--rolls expects a positive integer.");
                    break;
                case "--help" or "-h":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument '{arg}'.\n{Usage}");
                    return 1;
            }
        }

        if (string.IsNullOrWhiteSpace(jamlPath))
        {
            Console.Error.WriteLine("Missing required --jaml <path>.\n" + Usage);
            return 1;
        }
        if (jsonPath is null && htmlPath is null && jamlUiPath is null)
        {
            Console.Error.WriteLine(
                "At least one output is required: --json <path>, --html <path>, and/or --jamlui <path>.\n"
                    + Usage
            );
            return 1;
        }

        if (!TryLoadConfig(jamlPath, out var config, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        if (seedsArg is not null)
        {
            config.Seeds.Clear();
            config.Seeds.AddRange(
                seedsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            );
        }
        if (config.Seeds.Count == 0)
        {
            Console.Error.WriteLine(
                "No seeds to analyze: pass --seeds A,B,C or save a top-level seeds: block in the JAML."
            );
            return 1;
        }

        var results = MotelyJamlyzer.Analyze(config, eventRolls);

        if (jsonPath is not null || htmlPath is not null)
        {
            var report = JsonRenderDocument.Build(config, results, eventRolls);

            if (jsonPath is not null)
            {
                JsonRenderDocument.WriteJson(report, jsonPath);
                Console.WriteLine($"Wrote JSON → {jsonPath}");
            }
            if (htmlPath is not null)
            {
                JsonRenderDocument.EnsureParentDir(htmlPath);
                File.WriteAllText(htmlPath, HtmlReportRenderer.Render(report));
                Console.WriteLine($"Wrote HTML → {htmlPath}");
            }
        }
        if (jamlUiPath is not null)
        {
            JamlUiJsonRenderer.Write(config, results, eventRolls, jamlUiPath);
            Console.WriteLine($"Wrote jaml-ui JSON → {jamlUiPath}");
        }

        Console.WriteLine(
            $"Analyzed {results.Count} seed(s) from '{jamlPath}' "
                + $"(deck {config.Deck}, stake {config.Stake}, rolls {eventRolls})."
        );
        return 0;
    }

    private static string ResolvePath(string path) =>
        !Path.IsPathRooted(path) && !Path.HasExtension(path)
            ? Path.Combine("JamlFilters", path + ".jaml")
            : path;

    private static bool TryLoadConfig(
        string path,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    )
    {
        config = null;
        path = ResolvePath(path);

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = $"Error reading JAML file '{path}': {ex.Message}";
            return false;
        }

        return JamlConfigLoader.TryLoad(content, out config, out error);
    }
}
