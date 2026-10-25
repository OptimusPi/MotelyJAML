using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.BufferedDeserialization;
using YamlDotNet.Serialization.BufferedDeserialization.TypeDiscriminators;

namespace Motely.Filters;

/// <summary>
/// Picks a concrete clause type from Nat's JAML discriminator keys, then lets YamlDotNet bind properties.
/// A clause written as a plain string (<c>- Perkeo in ante 1 or 2</c>) is read by
/// <see cref="JamlShorthand"/> into the same clause its mapping twin binds to.
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

        if (reader.Current is Scalar scalar)
        {
            try
            {
                value = JamlShorthand.Parse(scalar.Value);
            }
            catch (FormatException ex)
            {
                // The scalar's own marks, so the error names the clause's line; the inner
                // exception carries the bare message the loader prints after "JAML line N:".
                throw new YamlException(scalar.Start, scalar.End, ex.Message, ex);
            }
            reader.MoveNext();
            return true;
        }

        var buffer = new ParserBuffer(reader, MaxBufferDepth, MaxBufferLength);
        if (!_discriminator.TryDiscriminate(buffer, out Type? concrete) || concrete is null)
            throw new YamlException(buffer.Current!.Start, buffer.Current!.End, "no clause key among the mapping keys.");

        buffer.Reset();
        value = nestedObjectDeserializer(buffer, concrete);
        return true;
    }
}
