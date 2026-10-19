using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Motely.Filters;

/// <summary>
/// YamlDotNet's parser, held to what a filter can be: one document, nested at most
/// <see cref="MaxDepth"/> levels (the root counts as one, as in JSON). YamlDotNet builds nodes and
/// objects by recursion, so a deeper document overflows the stack before anything can object, and
/// in the wasm build that kills the module. <see cref="JamlConfigLoader.FromJaml"/> and
/// <see cref="JamlConfigLoader.ToJson"/> both read through it, and its refusals are
/// <see cref="YamlException"/>s, so each reports the line as for any other YAML error.
/// </summary>
internal sealed class JamlParser(string text) : IParser
{
    /// <summary>System.Text.Json's default. A filter nests five or six levels; each <c>or:</c> adds two.</summary>
    public const int MaxDepth = 64;

    public static readonly string DepthMessage = $"the document nests deeper than {MaxDepth} levels here.";

    private readonly Parser parser = new(new StringReader(text));
    private int depth;
    private int documents;

    /// <summary>The nodes read so far: scalars, aliases, and the start of each mapping and sequence.</summary>
    public int Nodes { get; private set; }

    public ParsingEvent? Current => parser.Current;

    public bool MoveNext()
    {
        if (!parser.MoveNext())
            return false;
        var current = parser.Current!;
        switch (current)
        {
            case MappingStart or SequenceStart:
                Nodes++;
                if (++depth > MaxDepth)
                    throw new YamlException(current.Start, current.End, DepthMessage);
                break;
            case MappingEnd or SequenceEnd:
                depth--;
                break;
            case Scalar or AnchorAlias:
                Nodes++;
                break;
            case DocumentStart:
                if (++documents > 1)
                    throw new YamlException(current.Start, current.End, "a second document starts here; a filter is one document.");
                break;
        }
        return true;
    }
}
