using Bootsharp;
using Motely.Filters.Jaml;

/// <summary>
/// Filter text checks for editors. Takes the text, not a <see cref="JamlConfig"/>: the config is a
/// class and must not cross the boundary (see <c>Analyze</c>). Works on .jaml, .yaml, .yml and
/// .json text alike, whether it came from <c>JamlFiles.load</c>, a textarea or fetch.
/// </summary>
public static partial class Jaml
{
    /// <summary>Null when the filter loads. Otherwise the loader's message, which names the line:
    /// <c>JAML line 20: `1-8` is not an integer (key `antes`)</c>.</summary>
    [Export]
    public static string? Check(string text) =>
        JamlConfigLoader.TryLoad(text, out _, out var error) ? null : error;
}
