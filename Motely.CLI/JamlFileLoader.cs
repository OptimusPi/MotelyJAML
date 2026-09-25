using System.Diagnostics.CodeAnalysis;
using Motely;
using Motely.Filters.Jaml;

namespace Motely.CLI;

public static class JamlFileLoader
{
    public static string ResolvePath(string path) => MotelyJamlFile.ResolvePath(path);

    public static bool TryLoadFromPath(
        string path,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    ) => MotelyJamlFile.TryLoad(path, out config, out error);

    public static bool TrySaveSeeds(string path, IReadOnlyList<string> seeds, out string? error) =>
        MotelyJamlFile.TrySaveSeeds(path, seeds, out error);
}
