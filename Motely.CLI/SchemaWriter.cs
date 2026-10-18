using Motely.Schema;

namespace Motely.CLI;

/// <summary>
/// Writes the editor artifacts that describe the filter dialect — the JSON Schema and the
/// TextMate grammar — from the engine's live vocabulary. One command, two files, no keys
/// typed twice anywhere: run it after the engine changes and the editors are current.
/// </summary>
public static class SchemaWriter
{
    private const string SchemaFileName = "motely-filter.schema.json";
    private const string GrammarFileName = "jaml.tmLanguage.json";

    /// <summary>
    /// <c>motely schema</c> — writes the schema and grammar. Without <paramref name="outDir"/> the
    /// repo root is located by walking up from the running binary, landing them in the tree they
    /// belong to (<c>schema/</c>, <c>vscode-jaml/syntaxes/</c>) no matter where it is invoked.
    /// </summary>
    public static int Run(string? outDir)
    {
        var (schemaPath, grammarPath) = outDir is { Length: > 0 }
            ? (
                Path.Combine(outDir, SchemaFileName),
                Path.Combine(outDir, GrammarFileName)
            )
            : (
                Path.Combine(RepoRoot(), "schema", SchemaFileName),
                Path.Combine(RepoRoot(), "vscode-jaml", "syntaxes", GrammarFileName)
            );

        Write(schemaPath, FilterJsonSchema.Generate());
        Write(grammarPath, FilterGrammar.Generate());

        Console.WriteLine(schemaPath);
        Console.WriteLine(grammarPath);
        return 0;
    }

    private static void Write(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, content);
    }

    /// <summary>The checkout containing this binary, or the working directory when there is none.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Motely.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
