namespace Motely.Filters.Jaml;

public readonly record struct JamlSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public static JamlSpan OnLine(int line, int column, int length) =>
        new(line, column, line, column + Math.Max(length, 0));

    public static JamlSpan WholeLine(int line, int length) => new(line, 0, line, length);

    public bool IsEmpty => this == default;
}
