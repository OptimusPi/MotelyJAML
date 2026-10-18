using Motely.Schema;

namespace Motely.CLI;

/// <summary>
/// Writes the artifacts that describe the filter dialect — the JSON Schema, the TextMate grammar,
/// and the TypeScript declarations motely-wasm ships as <c>motely-wasm/jaml</c> — from the
/// engine's live vocabulary. One command, no keys typed twice anywhere: run it after the engine
/// changes and every consumer is current.
/// </summary>
public static class SchemaWriter
{
    private const string SchemaFileName = "motely-filter.schema.json";
    private const string GrammarFileName = "jaml.tmLanguage.json";
    private const string TypesFileName = "index.d.ts";
    private const string TypesModuleFileName = "index.mjs";

    /// <summary>
    /// <c>motely schema</c> — writes the schema, grammar and TypeScript declarations. Without
    /// <paramref name="outDir"/> the repo root is located by walking up from the running binary,
    /// landing them in the tree they belong to (<c>schema/</c>, <c>vscode-jaml/syntaxes/</c>,
    /// <c>Motely.Wasm/jaml/</c>) no matter where it is invoked.
    /// </summary>
    public static int Run(string? outDir)
    {
        var root = RepoRoot();
        var (schemaPath, grammarPath, packageDir) = outDir is { Length: > 0 }
            ? (
                Path.Combine(outDir, SchemaFileName),
                Path.Combine(outDir, GrammarFileName),
                outDir
            )
            : (
                Path.Combine(root, "schema", SchemaFileName),
                Path.Combine(root, "vscode-jaml", "syntaxes", GrammarFileName),
                Path.Combine(root, "Motely.Wasm", "jaml")
            );

        var schema = FilterJsonSchema.Generate();
        Write(schemaPath, schema);
        Write(grammarPath, FilterGrammar.Generate());
        Write(Path.Combine(packageDir, SchemaFileName), schema);
        Write(Path.Combine(packageDir, TypesFileName), FilterTypeScript.Generate());
        // Types only: the module exists so a plain (non-type) import of motely-wasm/jaml resolves.
        Write(Path.Combine(packageDir, TypesModuleFileName), "export {};\n");

        Console.WriteLine(schemaPath);
        Console.WriteLine(grammarPath);
        Console.WriteLine(Path.Combine(packageDir, TypesFileName));
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
