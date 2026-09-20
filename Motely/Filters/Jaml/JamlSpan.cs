namespace Motely.Filters.Jaml;

/// <summary>
/// Zero-based source range for a JAML diagnostic: the text a message underlines.
/// Lines and columns are zero-based (editor convention); VYaml's marks are one-based
/// and are converted at the single point they enter, in <see cref="JamlYamlTree"/>.
/// </summary>
public readonly record struct JamlSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    /// <summary>A range on one line, starting at <paramref name="column"/>.</summary>
    public static JamlSpan OnLine(int line, int column, int length) =>
        new(line, column, line, column + Math.Max(length, 0));

    /// <summary>The whole of one line.</summary>
    public static JamlSpan WholeLine(int line, int length) => new(line, 0, line, length);

    /// <summary>Nothing to underline — the message stands on its own.</summary>
    public bool IsEmpty => this == default;
}
