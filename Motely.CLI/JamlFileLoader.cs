using System.Diagnostics.CodeAnalysis;
using Motely;
using Motely.Filters.Jaml;

namespace Motely.CLI;

/// <summary>
/// Thin CLI-facing shim over the shared <see cref="MotelyJamlFile"/> gateway. Kept so existing CLI
/// call-sites and their wording stay put, but path resolution, loading, and save-back all route
/// through the one core implementation shared with the TUI and DataLake — they can never disagree
/// about where a <c>--jaml</c> file lives.
/// </summary>
public static class JamlFileLoader
{
    /// <inheritdoc cref="MotelyJamlFile.ResolvePath"/>
    public static string ResolvePath(string path) => MotelyJamlFile.ResolvePath(path);

    public static bool TryLoadFromPath(
        string path,
        [NotNullWhen(true)] out JamlConfig? config,
        out string? error
    ) => MotelyJamlFile.TryLoad(path, out config, out error);

    /// <summary>
    /// Rewrite the top-level <c>seeds:</c> block of the JAML at <paramref name="path"/> with the
    /// given seeds and write it back (resolved and validated by <see cref="MotelyJamlFile"/>).
    /// </summary>
    public static bool TrySaveSeeds(string path, IReadOnlyList<string> seeds, out string? error) =>
        MotelyJamlFile.TrySaveSeeds(path, seeds, out error);
}
