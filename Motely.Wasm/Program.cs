using Bootsharp;
using Bootsharp.Inject;
using Microsoft.Extensions.DependencyInjection;

/// <summary>RID entry. Erased from JS by <see cref="Names"/>.</summary>
public static class Boot
{
    public static void Main()
    {
        // AddBootsharp() registers the generated implementations of every [assembly: Import]
        // interface - including IFileMounter from Bootsharp.FileSystem. JamlFiles resolves it
        // lazily through MotelyServices, so booting without the JS fs package still works.
        MotelyServices.Init(new ServiceCollection().AddBootsharp().BuildServiceProvider());
        Console.WriteLine("motely-wasm: runtime up");
    }
}

/// <summary>Static locator for the process-wide DI container. Erased from JS by <see cref="Names"/>.</summary>
public static class MotelyServices
{
    private static IServiceProvider? _services;

    public static void Init(IServiceProvider services) => _services = services;

    public static T Get<T>()
        where T : notnull =>
        (_services ?? throw new InvalidOperationException("MotelyServices.Init was never called."))
            .GetRequiredService<T>();
}

/// <summary>
/// Bootsharp renaming.md: module path, node (type), member. Null/empty node or member
/// erases that artifact from JS. Fold everything into <c>index</c> so the import is
/// <c>import { Search, Analyze } from "motely-wasm"</c>. Erase Boot, Names, and the
/// specialization machinery — JS sees the Clr type (e.g. MotelySingleSearchContext), not
/// the Import/Export proxy classes.
/// </summary>
public static class Names
{
    [RenameModule]
    public static string Module(Type type, string @default) => "index";

    [RenameNode]
    public static string Node(Type type, string @default)
    {
        if (type.Name is "Boot" or "Names" or "MotelyServices") return null!;
        if (typeof(SpecializedImport).IsAssignableFrom(type)) return null!;
        if (typeof(SpecializedExport).IsAssignableFrom(type)) return null!;
        // C# stays Motely*. TS enum names match Balatro: Joker, TarotCard, SpectralCard, …
        if (type.IsEnum && @default.StartsWith("Motely", StringComparison.Ordinal))
            return @default["Motely".Length..];
        return @default;
    }
}
