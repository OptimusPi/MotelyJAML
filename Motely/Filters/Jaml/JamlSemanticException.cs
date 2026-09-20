namespace Motely.Filters.Jaml;

/// <summary>
/// A document that parsed as YAML but does not mean anything in JAML: an unknown wire name,
/// a value the grammar rejects, a key in the wrong place. <see cref="Span"/> is where to
/// underline, and is empty when the error has no single position.
/// </summary>
public sealed class JamlSemanticException(string message, JamlSpan span = default)
    : InvalidOperationException(message)
{
    public JamlSpan Span { get; } = span;
}
