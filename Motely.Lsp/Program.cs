using System.Text.Json;
using Motely.Lsp;

// One-shot engine jobs (do not open the stdio LSP loop).
//   Motely.Lsp --diagnose <file.jaml> | -
//   Motely.Lsp --explain <topic words…>
// Exit 0 = ok; diagnose 1 = has diagnostics; 2 = usage / IO / unknown topic.
if (args is ["--diagnose", var pathArg])
{
    try
    {
        string text =
            pathArg == "-"
                ? Console.In.ReadToEnd()
                : File.ReadAllText(pathArg);
        var diags = JamlLanguageService.Diagnose(text);
        var payload = diags
            .Select(d => new DiagnosticDto(
                d.Message,
                d.Code,
                d.Severity.ToString(),
                d.Span.StartLine,
                d.Span.StartColumn,
                d.Span.EndLine,
                d.Span.EndColumn
            ))
            .ToArray();
        Console.Out.WriteLine(
            JsonSerializer.Serialize(payload, LspJsonContext.Default.DiagnosticDtoArray)
        );
        return diags.Count == 0 ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

if (args.Length >= 1 && args[0] == "--explain")
{
    var topic = string.Join(' ', args.AsSpan(1).ToArray()).Trim();
    if (topic.Length == 0)
    {
        Console.Error.WriteLine("Usage: Motely.Lsp --explain <topic>");
        return 2;
    }
    var md = JamlLanguageService.Explain(topic);
    if (md is null)
    {
        Console.Out.WriteLine(
            JsonSerializer.Serialize(
                new ExplainDto(false, topic, null),
                LspJsonContext.Default.ExplainDto
            )
        );
        return 2;
    }
    Console.Out.WriteLine(
        JsonSerializer.Serialize(new ExplainDto(true, topic, md), LspJsonContext.Default.ExplainDto)
    );
    return 0;
}

if (args.Length > 0)
{
    Console.Error.WriteLine(
        "Usage: Motely.Lsp                 # stdio language server\n"
            + "       Motely.Lsp --diagnose <file.jaml>\n"
            + "       Motely.Lsp --diagnose -   # stdin\n"
            + "       Motely.Lsp --explain <topic>"
    );
    return 2;
}

// stdout carries the protocol; every human-readable word goes to stderr.
var server = new LspServer(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput(),
    Console.Error
);
return server.Run();
