using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely.Filters;

/// <summary>
/// JAML text → <see cref="JamlConfig"/> through YamlDotNet's static deserializer. JSON is JAML,
/// so a JSON document loads through the same path.
/// </summary>
public static partial class JamlConfigLoader
{
    private static readonly IDeserializer Deserializer = CreateDeserializer();

    private static IDeserializer CreateDeserializer()
    {
        var builder = new StaticDeserializerBuilder(new JamlYamlContext())
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithCaseInsensitivePropertyMatching()
            .WithNodeDeserializer(new ClauseTypeDiscriminatingNodeDeserializer())
            .WithNodeDeserializer(new JamlEnumNodeDeserializer());
        foreach (var converter in JamlClauseBinding.Converters)
            builder = builder.WithTypeConverter(converter);
        return builder.Build();
    }

    public static JamlConfig FromJaml(string text)
    {
        var outer = ClauseTypeDiscriminatingNodeDeserializer.UnstatedClauses;
        var unstated = ClauseTypeDiscriminatingNodeDeserializer.UnstatedClauses = [];
        try
        {
            var config = Deserializer.Deserialize<JamlConfig?>(new JamlParser(text))
                ?? throw new InvalidOperationException("JAML: the document is empty.");
            config.Defaults?.Apply(unstated);
            return config;
        }
        catch (InvalidCastException)
        {
            // A scalar document (a CSV, a seed list) parses as one string, not a mapping.
            throw new InvalidOperationException("JAML: not a filter (expected keys like name:, must:, should:).");
        }
        catch (YamlException ex)
        {
            var inner = ex;
            while (inner.InnerException is YamlException next)
                inner = next;
            var message = $"JAML line {inner.Start.Line}: {Explain(inner.InnerException?.Message ?? inner.Message)}";
            // Mark coordinates are long; an editor needs int and no file is 2GB of lines.
            throw new JamlFilterException(
                message, (int)inner.Start.Line, (int)inner.Start.Column, ex);
        }
        finally
        {
            ClauseTypeDiscriminatingNodeDeserializer.UnstatedClauses = outer;
        }
    }

    /// <summary>
    /// YamlDotNet's <c>Property 'x' not found on type 'Motely.Filters.T'.</c>, said in JAML terms
    /// with the key meant (<see cref="JamlKeys.UnknownKeyMessage"/>); any other message as it is.
    /// </summary>
    private static string Explain(string message)
    {
        const string Head = "Property '";
        const string Middle = "' not found on type '";
        if (!message.StartsWith(Head, StringComparison.Ordinal))
            return message;
        int middle = message.IndexOf(Middle, Head.Length, StringComparison.Ordinal);
        int end = message.LastIndexOf('\'');
        if (middle < 0 || end <= middle + Middle.Length)
            return message;
        var key = message[Head.Length..middle];
        var type = message[(middle + Middle.Length)..end];
        return JamlKeys.UnknownKeyMessage(key, type) ?? message;
    }

    public static JamlConfig FromFile(string path) => FromJaml(File.ReadAllText(path));

    /// <summary>Every key that starts a JAML filter clause (<c>joker</c>, <c>legendaryJoker</c>, <c>or</c>, …).</summary>
    public static IReadOnlyCollection<string> ClauseKeys => JamlClauseBinding.ClauseKeys;

    public static bool TryLoad(string text, [NotNullWhen(true)] out JamlConfig? config, out string? error)
    {
        try
        {
            config = FromJaml(text);
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
}

[YamlStaticContext]
[YamlSerializable(typeof(IMotelyClause))]
public partial class JamlYamlContext : YamlDotNet.Serialization.StaticContext;
