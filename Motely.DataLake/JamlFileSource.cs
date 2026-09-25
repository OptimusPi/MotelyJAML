using System.Diagnostics.CodeAnalysis;
using Motely.Filters.Jaml;

namespace Motely.DataLake;

public static class JamlFileSource
{
    public static bool TryLoadFromFile(
        string path,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    ) => MotelyJamlFile.TryLoad(path, out config, out error);

    public static string? ResolvePath(string path) => MotelyJamlFile.TryResolveExisting(path);
}
