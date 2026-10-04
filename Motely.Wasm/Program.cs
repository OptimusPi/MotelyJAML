using Bootsharp;
using Bootsharp.Inject;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Entry point. Erased from JS by <see cref="Names"/>.</summary>
public static class Boot
{
    public static void Main() =>
        // AddBootsharp registers the generated implementation of every imported interface,
        // IFileMounter included when the build has Bootsharp.FileSystem.
        MotelyServices.Init(new ServiceCollection().AddBootsharp().BuildServiceProvider());
}

/// <summary>The process-wide container. Erased from JS by <see cref="Names"/>.</summary>
public static class MotelyServices
{
    private static IServiceProvider? _services;

    public static void Init(IServiceProvider services) => _services = services;

    public static T Get<T>() where T : notnull =>
        (_services ?? throw new InvalidOperationException("The runtime has not booted."))
            .GetRequiredService<T>();
}

/// <summary>
/// One module, so <c>import { Search, Analyze } from "motely-wasm"</c> works. The plumbing types
/// are erased from the JS surface.
/// </summary>
public static class Names
{
    [RenameModule]
    public static string Module(Type type, string @default) => "index";

    [RenameNode]
    public static string Node(Type type, string @default) =>
        type.Name is "Boot" or "Names" or "MotelyServices" ? null! : @default;
}
