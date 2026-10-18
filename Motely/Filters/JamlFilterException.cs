namespace Motely.Filters;

/// <summary>
/// A filter that would not load, carrying where the loader stopped: <see cref="Line"/> is
/// YamlDotNet's 1-based line and <see cref="Column"/> its 0-based column index, so an editor
/// can squiggle the offending token instead of guessing. <see cref="Message"/> keeps the exact
/// <c>JAML line N: …</c> wording the CLI and tests have always printed, and the base type
/// stays <see cref="InvalidOperationException"/>, so every existing catch site and message
/// assertion holds — this is that exception with coordinates bolted on.
/// </summary>
public sealed class JamlFilterException(string message, int line, int column, Exception innerException)
    : InvalidOperationException(message, innerException)
{
    /// <summary>1-based line number, exactly as YamlDotNet's <c>Mark.Line</c> reports it.</summary>
    public int Line { get; } = line;

    /// <summary>0-based column index, exactly as YamlDotNet's <c>Mark.Column</c> reports it.</summary>
    public int Column { get; } = column;

    /// <summary>The zero-based line LSP spans count from.</summary>
    public int ZeroBasedLine => Line > 1 ? Line - 1 : 0;
}
