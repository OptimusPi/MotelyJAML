// Entry point of the browser build. Bootsharp generates the JS bindings for the modules listed
// below (guide: interop-modules) and Bootsharp.Inject wires them up (guide: dependency-injection).
// The engine itself knows nothing about JavaScript.

using Bootsharp;
using Bootsharp.Inject;
using Microsoft.Extensions.DependencyInjection;

[assembly: Export(typeof(ISearch), typeof(IAnalyze), typeof(IJaml), typeof(IJamlFiles))]

new ServiceCollection()
    // Generated implementations of the imported modules (IFileMounter when the build has
    // Bootsharp.FileSystem).
    .AddBootsharp()
    .AddSingleton<ISearch, SearchModule>()
    .AddSingleton<IAnalyze, AnalyzeModule>()
    .AddSingleton<IJaml, JamlModule>()
    .AddSingleton<IJamlFiles, JamlFilesModule>()
    .BuildServiceProvider()
    // Hands the exported modules to the generated JS bindings.
    .RunBootsharp();

/// <summary>Renaming (guide: renaming).</summary>
public static class Names
{
    /// <summary>Engine types live in several C# namespaces; JS gets one module, so
    /// <c>import { Search, MotelyDeck } from "motely-wasm"</c> works.</summary>
    [RenameModule]
    public static string Module(Type type, string @default) => "index";

    /// <summary>Modules drop the interface prefix: ISearch is <c>Search</c> in JS.</summary>
    [RenameNode]
    public static string Node(Type type, string @default) =>
        type.IsInterface && @default.Length > 1 && @default[0] == 'I' && char.IsUpper(@default[1])
            ? @default[1..]
            : @default;
}
