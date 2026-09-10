using System.Text.Json.Serialization;

namespace Motely.Lsp;

// The one-shot jobs (--diagnose, --explain) used anonymous types through the reflection
// serializer, which NativeAOT rejects (IL2026/IL3050). Source generation needs named types,
// so these carry the wire format instead. Property names are lower-cased deliberately: they
// ARE the JSON keys the vscode extension parses, and renaming them breaks that contract.

/// <summary>One diagnostic, as <c>--diagnose</c> emits it.</summary>
internal sealed record DiagnosticDto(
    string message,
    string code,
    string severity,
    int startLine,
    int startColumn,
    int endLine,
    int endColumn
);

/// <summary>The <c>--explain</c> answer; <paramref name="markdown"/> is null when unknown.</summary>
internal sealed record ExplainDto(bool ok, string topic, string? markdown);

[JsonSerializable(typeof(DiagnosticDto[]))]
[JsonSerializable(typeof(ExplainDto))]
internal sealed partial class LspJsonContext : JsonSerializerContext;
