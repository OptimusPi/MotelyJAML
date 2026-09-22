namespace Motely.Filters.Jaml;

/// <summary>
/// A semantic error the loader raises once the document has parsed cleanly but says something the
/// grammar rejects — an unknown key, a value outside its enum. Derives from
/// <see cref="InvalidOperationException"/> so every caller that already catches that (TryLoad,
/// FromJaml's own rethrow, the loader tests) is untouched, while carrying the
/// <see cref="JamlSpan"/> the loader already knew at the throw site. That is what lets an editor
/// underline the offending token directly, instead of regexing the word back out of the message
/// and guessing which occurrence in the document it meant — the drift this type exists to end.
/// </summary>
internal sealed class JamlSemanticException(string message, JamlSpan span)
    : InvalidOperationException(message)
{
    /// <summary>Where the rejected token sits in the source. Empty only when the throw site
    /// genuinely has no node in hand; a diagnostic falls back to the first line then.</summary>
    public JamlSpan Span { get; } = span;
}
