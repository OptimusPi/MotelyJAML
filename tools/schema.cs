// Writes the artifacts that describe the filter dialect, from the engine's live vocabulary: the
// JSON Schema, the TextMate grammar, and the TypeScript declarations motely-wasm ships as
// motely-wasm/jaml. Run it after the engine changes and every consumer is current:
//
//   dotnet run tools/schema.cs              into schema/, vscode-jaml/syntaxes/, Motely.Wasm/jaml/
//   dotnet run tools/schema.cs --out <dir>  all of them into one directory
//
// The vocabulary is read off the JAML types by reflection (Motely.Schema.FilterVocabulary), which
// a trimmed binary has only in part, so the generator is this JIT app and not a command of the
// NativeAOT CLI.
#:project ../Motely/Motely.csproj
#:property PublishAot=false

using System.Runtime.CompilerServices;
using Motely.Schema;

const string SchemaFileName = "motely-filter.schema.json";
const string GrammarFileName = "jaml.tmLanguage.json";
const string TypesFileName = "index.d.ts";
const string TypesModuleFileName = "index.mjs";

int outAt = Array.IndexOf(args, "--out");
string? outDir = outAt >= 0 && outAt + 1 < args.Length ? args[outAt + 1] : null;
string root = RepoRoot();

var (schemaPath, grammarPath, packageDir) = outDir is { Length: > 0 }
    ? (Path.Combine(outDir, SchemaFileName), Path.Combine(outDir, GrammarFileName), outDir)
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

static void Write(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, content);
}

// The checkout this file sits in: its parent directory, wherever the app is run from.
static string RepoRoot([CallerFilePath] string self = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, ".."));
