using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.BufferedDeserialization;
using YamlDotNet.Serialization.BufferedDeserialization.TypeDiscriminators;

namespace Motely.Filters;

/// <summary>
/// Picks a concrete clause type from Nat's JAML discriminator keys, then lets YamlDotNet bind properties.
/// </summary>
internal sealed class ClauseTypeDiscriminatingNodeDeserializer : INodeDeserializer
{
    private const int MaxBufferDepth = 128;
    private const int MaxBufferLength = 1_048_576;

    private readonly UniqueKeyTypeDiscriminator _discriminator = new(typeof(IMotelyClause), JamlClauseBinding.ClauseTypeByKey);

    public bool Deserialize(
        IParser reader,
        Type expectedType,
        Func<IParser, Type, object?> nestedObjectDeserializer,
        out object? value,
        ObjectDeserializer rootDeserializer
    )
    {
        value = null;
        if (expectedType != typeof(IMotelyClause))
            return false;

        var buffer = new ParserBuffer(reader, MaxBufferDepth, MaxBufferLength);
        if (!_discriminator.TryDiscriminate(buffer, out Type? concrete) || concrete is null)
            throw new YamlException(buffer.Current!.Start, buffer.Current!.End, "no clause key among the mapping keys.");

        buffer.Reset();
        value = nestedObjectDeserializer(buffer, concrete);
        return true;
    }
}
