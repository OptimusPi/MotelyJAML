using Bootsharp;
using Motely.Filters.Jaml;

/// <summary>Filter text checks for editors. Takes text, never a config object: classes cross the
/// boundary by reference and would drag every clause type into JS.</summary>
public static partial class Jaml
{
    /// <summary>Null when the text loads, otherwise the loader's line-numbered message.</summary>
    [Export]
    public static string? Check(string text) =>
        JamlConfigLoader.TryLoad(text, out _, out var error) ? null : error;
}
