namespace Motely.Lsp;

/// <summary>
/// Where an editor answer sits: zero-based lines and columns — LSP's own coordinates — so
/// nothing has to translate on the way out. Spans are single-line in practice;
/// <see cref="EndLine"/> exists because the protocol's ranges want a full box.
/// </summary>
public readonly record struct JamlSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    /// <summary>A span covering columns <c>[start, start + length)</c> of one zero-based line.</summary>
    public static JamlSpan OnLine(int line, int start, int length) =>
        new(line, start, line, start + Math.Max(length, 0));

    /// <summary>A span covering the first <paramref name="length"/> columns of one line.</summary>
    public static JamlSpan WholeLine(int line, int length) => OnLine(line, 0, length);

    /// <summary>True when the span covers no characters — the default value is empty.</summary>
    public bool IsEmpty => StartLine == EndLine && StartColumn == EndColumn;
}
