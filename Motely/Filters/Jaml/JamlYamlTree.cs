using System.Text;
using VYaml.Parser;

namespace Motely.Filters.Jaml;

/// <summary>
/// YAML text → <see cref="JMap"/> tree through VYaml's parser. The only document parser: JSON is
/// read as YAML. No <c>dynamic</c>, no Activator.
/// </summary>
/// <summary>A document that is not valid YAML, with the line the scanner stopped on.</summary>
internal sealed class JamlSyntaxException(string message, JamlSpan span, Exception inner)
    : InvalidOperationException(message, inner)
{
    public JamlSpan Span { get; } = span;
}

internal static class JamlYamlTree
{
    public static JMap Parse(string text)
    {
        // VYaml hangs on a bare "joker:" (implicit null) only when it is the last token with no
        // newline after it; followed by a newline it reads as a null scalar. Null reads as blank,
        // which a discriminator takes as Any (JamlDisc) and any other key as not written.
        // Appending at the end keeps every line/column where the author wrote it.
        text = EnsureTrailingNewline(text);
        var locator = new Locator(text);
        var parser = YamlParser.FromBytes(Encoding.UTF8.GetBytes(text));
        try
        {
            parser.SkipAfter(ParseEventType.DocumentStart);
            if (parser.End
                || parser.CurrentEventType is ParseEventType.DocumentEnd or ParseEventType.StreamEnd)
                return new JMap();
            return ReadYaml(ref parser, locator) as JMap
                ?? throw new InvalidOperationException("YAML root must be a mapping.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The scanner's mark is where it gave up (CurrentMark.Line is 1-based). At end of
            // input it sits past the last line, so step back over blank lines to the last one
            // with text — the line the author has to fix.
            var lines = text.Split('\n');
            var line = Math.Clamp(parser.CurrentMark.Line - 1, 0, lines.Length - 1);
            while (line > 0 && string.IsNullOrWhiteSpace(lines[line]))
                line--;
            var source = lines[line].Trim();
            throw new JamlSyntaxException(
                $"YAML parse error at '{source}': {ex.Message}",
                JamlSpan.WholeLine(line, Math.Max(lines[line].Length, 1)),
                ex
            );
        }
    }

    private static string EnsureTrailingNewline(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    /// <summary>
    /// Stamps source spans. VYaml's <c>CurrentMark</c> is the scanner's position, which runs past
    /// the token (a plain scalar's mark sits at the start of the next line), so it can't name
    /// where a token starts. Tokens arrive in document order, so each scalar is found by
    /// searching forward from the end of the previous one.
    /// </summary>
    private sealed class Locator(string text)
    {
        private int _cursor;

        public JamlSpan Scalar(string value)
        {
            if (value.Length > 0)
            {
                int at = text.IndexOf(value, _cursor, StringComparison.Ordinal);
                if (at >= 0)
                {
                    _cursor = at + value.Length;
                    return SpanOf(at, value.Length);
                }
            }
            return Here();
        }

        /// <summary>A zero-length span at the next non-blank character — for maps, seqs and unmatched scalars.</summary>
        public JamlSpan Here()
        {
            int at = _cursor;
            while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '-' or ':' or ',' or '[' or '{')
                at++;
            return SpanOf(Math.Min(at, text.Length), 0);
        }

        private JamlSpan SpanOf(int offset, int length)
        {
            int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
            int line = 0;
            for (int i = 0; i < lineStart; i++)
                if (text[i] == '\n')
                    line++;
            return JamlSpan.OnLine(line, offset - lineStart, length);
        }
    }

    // Each ReadYaml call consumes exactly one node (same contract as VYaml's
    // PrimitiveObjectFormatter). Extra Read() after a child was the desync:
    // empty "joker:" is EmptyScalar, not MappingEnd.
    private const int YamlPairCap = 65_536;

    private static JNode ReadYaml(ref YamlParser parser, Locator locator)
    {
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
            {
                // YAML null (`null`, `~`, JSON null) is a blank value, same as an empty scalar.
                var isNull = parser.IsNullScalar();
                var text = isNull ? "" : parser.GetScalarAsString() ?? "";
                var kind = parser.TryGetScalarAsInt32(out _)
                    ? JScalarKind.Integer
                    : JScalarKind.Bare;
                var span = locator.Scalar(text);
                parser.Read();
                return new JScalar(text, kind) { Span = span, IsNull = isNull };
            }
            case ParseEventType.MappingStart:
                return ReadYamlMap(ref parser, locator);
            case ParseEventType.SequenceStart:
                return ReadYamlSeq(ref parser, locator);
            case ParseEventType.Alias:
                throw new InvalidOperationException("YAML aliases are not supported.");
            default:
                throw new InvalidOperationException(
                    $"Unexpected YAML event {parser.CurrentEventType}."
                );
        }
    }

    private static JMap ReadYamlMap(ref YamlParser parser, Locator locator)
    {
        var map = new JMap { Span = locator.Here() };
        parser.Read();
        var n = 0;
        while (!parser.End && parser.CurrentEventType != ParseEventType.MappingEnd)
        {
            if (++n > YamlPairCap)
                throw new InvalidOperationException("YAML mapping did not terminate.");
            if (parser.CurrentEventType != ParseEventType.Scalar)
                throw new InvalidOperationException("YAML mapping key must be a scalar.");
            string key = parser.GetScalarAsString() ?? "";
            var keySpan = locator.Scalar(key);
            if (!parser.Read())
                throw new InvalidOperationException($"YAML mapping '{key}' is missing a value.");
            map.Set(key, ReadYaml(ref parser, locator), keySpan);
        }
        if (parser.CurrentEventType == ParseEventType.MappingEnd)
            parser.Read();
        return map;
    }

    private static JSeq ReadYamlSeq(ref YamlParser parser, Locator locator)
    {
        var seq = new JSeq { Span = locator.Here() };
        parser.Read();
        var n = 0;
        while (!parser.End && parser.CurrentEventType != ParseEventType.SequenceEnd)
        {
            if (++n > YamlPairCap)
                throw new InvalidOperationException("YAML sequence did not terminate.");
            seq.Items.Add(ReadYaml(ref parser, locator));
        }
        if (parser.CurrentEventType == ParseEventType.SequenceEnd)
            parser.Read();
        return seq;
    }
}
