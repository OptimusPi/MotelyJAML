namespace Motely.Lsp.Core;

using Motely.Filters.Jaml;

/// <summary>How urgently an editor should paint a <see cref="JamlDiagnostic"/>.</summary>
public enum JamlDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

/// <summary>One squiggle: where it sits, what it says, and a stable code editors can key on.</summary>
public sealed record JamlDiagnostic(
    JamlSpan Span,
    string Message,
    JamlDiagnosticSeverity Severity,
    string Code
);

/// <summary>Hover content for the word under the cursor, as markdown, with the word's span.</summary>
public sealed record JamlHoverInfo(JamlSpan Span, string Markdown);

/// <summary>
/// One node of the document outline. <see cref="Kind"/> is an LSP <c>SymbolKind</c> number,
/// chosen to agree with the semantic-token type the same word gets: root keys are Namespace,
/// clause discriminators are Class, everything else is Property. <see cref="Range"/> covers the
/// key and its whole indented block; <see cref="SelectionRange"/> covers just the key and is
/// always contained in <see cref="Range"/>, which clients require.
/// </summary>
public sealed record JamlDocumentSymbol(
    string Name,
    int Kind,
    JamlSpan Range,
    JamlSpan SelectionRange,
    IReadOnlyList<JamlDocumentSymbol> Children
);

/// <summary>One completion candidate. <see cref="Kind"/> is a coarse category the shells map
/// onto their own item kinds: "key", "value", "discriminator". When
/// <see cref="ReplaceSpan"/> is non-empty, the client replaces that range with
/// <see cref="Label"/> (typed prefix overtype).</summary>
public sealed record JamlCompletionItem(
    string Label,
    string Kind,
    string? Detail = null,
    JamlSpan ReplaceSpan = default
);
