using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using VYaml.Parser;
using static Motely.Filters.JamlBinder;

namespace Motely.Filters;

/// <summary>
/// YAML text → <see cref="JamlConfig"/>. VYaml's event parser reads the document into a small
/// node tree that remembers each key's line, and <see cref="JamlBinder"/> fills the config from
/// it. JSON is YAML, so a JSON document loads through the same door.
/// </summary>
public static class YamlConfigLoader
{
    public static JamlConfig FromYaml(string text) => Bind(Parse(text));

    public static JamlConfig FromFile(string path) => FromYaml(File.ReadAllText(path));

    public static bool TryLoad(string text, [NotNullWhen(true)] out JamlConfig? config, out string? error)
    {
        try
        {
            config = FromYaml(text);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            config = null;
            error = ex.Message;
            return false;
        }
    }

    private static Node Parse(string yaml)
    {
        var parser = new YamlParser(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(yaml)));
        try
        {
            parser.SkipHeader();
            if (parser.End || parser.CurrentEventType is ParseEventType.DocumentEnd or ParseEventType.StreamEnd)
                throw new InvalidOperationException("YAML: the document is empty.");
            return ReadNode(ref parser, new KeyLines(yaml), depth: 1);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // VYaml's tokenizer and parser exceptions: one type, with the line named and quoted.
            throw new InvalidOperationException(DescribeYamlError(yaml, ex.Message), ex);
        }
    }

    /// <summary>
    /// VYaml ends its messages with "at Line: N, Col: C, Idx: I" (line 1-based, column 0-based)
    /// and never shows the text. Say "YAML line N:" and quote the line instead.
    /// </summary>
    private static string DescribeYamlError(string yaml, string message)
    {
        const string marker = " at Line: ";
        int at = message.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return $"YAML: {message}";
        var parts = message[(at + marker.Length)..].Split(", ");
        if (parts.Length < 2 || !int.TryParse(parts[0], out int line) || !int.TryParse(parts[1].Replace("Col: ", ""), out int col))
            return $"YAML: {message}";

        string what = message[..at];
        var lines = yaml.Split('\n');
        string text = line >= 1 && line <= lines.Length ? lines[line - 1].Trim() : "";
        return text.Length == 0
            ? $"YAML line {line}: {what} (column {col + 1})"
            : $"YAML line {line}: {what} (column {col + 1}): `{text}`";
    }

    // Bounds the recursion below, so hostile nesting is a load error and not a stack overflow.
    // 64 is as deep as a filter could ever nest.
    private const int MaxDepth = 64;

    /// <summary>
    /// Plain null is a null scalar; every other scalar is its text, typed later by the property
    /// it lands on. Keys are read as text, as written.
    /// </summary>
    private static Node ReadNode(ref YamlParser parser, KeyLines keyLines, int depth)
    {
        if (depth > MaxDepth && parser.CurrentEventType is ParseEventType.SequenceStart or ParseEventType.MappingStart)
            throw Error(keyLines.Last, $"the document nests deeper than {MaxDepth} levels.");
        switch (parser.CurrentEventType)
        {
            case ParseEventType.Scalar:
                if (parser.IsNullScalar())
                {
                    parser.Read();
                    return Scalar.Null;
                }
                return new Scalar(parser.ReadScalarAsString());

            case ParseEventType.SequenceStart:
                parser.Read();
                var items = new List<Node>();
                while (parser.CurrentEventType != ParseEventType.SequenceEnd)
                    items.Add(ReadNode(ref parser, keyLines, depth + 1));
                parser.Read();
                return new Sequence(items);

            case ParseEventType.MappingStart:
                parser.Read();
                var entries = new List<Entry>();
                while (parser.CurrentEventType != ParseEventType.MappingEnd)
                {
                    int mark = parser.CurrentMark.Line;
                    string key = parser.ReadScalarAsString() ?? "";
                    entries.Add(new Entry(key, keyLines.Find(mark, key), ReadNode(ref parser, keyLines, depth + 1)));
                }
                parser.Read();
                return new Mapping(entries);

            case ParseEventType.Alias:
                throw new InvalidOperationException("YAML: anchors and aliases (&name / *name) are not supported.");

            default:
                throw new InvalidOperationException($"YAML: unexpected YAML event {parser.CurrentEventType}.");
        }
    }

    /// <summary>
    /// The line a key is written on. VYaml's <see cref="YamlParser.CurrentMark"/> is where its
    /// tokenizer has read to, not where the key began. In block style that is just past the key's
    /// colon, on the key's own line. A flow collection can carry the tokenizer onto later lines
    /// (to the end of input on a one-line document), so walk back to the line that writes the key.
    /// </summary>
    private sealed class KeyLines(string text)
    {
        private readonly string[] lines = text.Split('\n');

        /// <summary>The last key's line. Keys come in document order, so the next is at or after it.</summary>
        public int Last { get; private set; } = 1;

        public int Find(int mark, string key)
        {
            int line = Math.Clamp(mark, 1, lines.Length);
            for (int l = line; l >= Last; l--)
                if (lines[l - 1].Contains(key, StringComparison.Ordinal))
                    return Last = l;
            return line;
        }
    }
}
