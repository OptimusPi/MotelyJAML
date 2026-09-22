namespace Motely.Filters.Jaml;

/// <summary>
/// Where a piece of a JAML document sits in its source text. Zero-based on both axes, which is
/// what LSP and CodeMirror both want, so an editor renders a span without doing arithmetic on it.
/// The end column is exclusive, so <c>Length</c> of a single-line span is <c>EndColumn - StartColumn</c>.
/// </summary>
/// <param name="StartLine">Zero-based line the span opens on.</param>
/// <param name="StartColumn">Zero-based column the span opens at.</param>
/// <param name="EndLine">Zero-based line the span closes on.</param>
/// <param name="EndColumn">Zero-based column just past the span's last character.</param>
public readonly record struct JamlSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    /// <summary>A span covering <paramref name="length"/> characters from one point on one line.</summary>
    public static JamlSpan OnLine(int line, int column, int length) =>
        new(line, column, line, column + length);

    /// <summary>A span covering a whole line, for a diagnostic that can't narrow it further.</summary>
    public static JamlSpan WholeLine(int line, int length) => new(line, 0, line, length);

    /// <summary>A span covering a whole line, taking the length from the line itself.</summary>
    public static JamlSpan OfLine(int line, string text) => new(line, 0, line, text.Length);

    /// <summary>True when this span carries no location — the value a node has before it's stamped.</summary>
    public bool IsEmpty => this == default;

    /// <summary>True when <paramref name="line"/>/<paramref name="column"/> falls inside this span.</summary>
    public bool Contains(int line, int column)
    {
        if (line < StartLine || line > EndLine)
            return false;
        if (line == StartLine && column < StartColumn)
            return false;
        if (line == EndLine && column >= EndColumn)
            return false;
        return true;
    }
}
