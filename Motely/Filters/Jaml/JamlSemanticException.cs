namespace Motely.Filters.Jaml;

public sealed class JamlSemanticException(string message, JamlSpan span = default)
    : InvalidOperationException(message)
{
    public JamlSpan Span { get; } = span;
}
