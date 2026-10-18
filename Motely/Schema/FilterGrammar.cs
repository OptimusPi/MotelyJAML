using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Motely.Schema;

/// <summary>
/// TextMate grammar for the filter dialect, built from the same <see cref="FilterVocabulary"/>
/// the loader answers to: discriminator keys, root keys, block keys, and every enum name the
/// engine can bind. Regenerated with the schema, so the grammar cannot describe a word the
/// engine does not know.
/// </summary>
public static class FilterGrammar
{
    public const string ScopeName = "source.jaml";

    private static readonly INamingConvention Naming = CamelCaseNamingConvention.Instance;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Generate() => Build().ToJsonString(WriteOptions) + "\n";

    public static JsonObject Build()
    {
        var disc = Keys(FilterVocabulary.DiscriminatorTable.Select(d => d.Key));
        var others = Keys(OtherKeys().Where(k => !FilterVocabulary.IsDiscriminator(k)));
        var enums = EnumNames();

        var patterns = new JsonArray
        {
            Pattern("#.*$", "comment.line.number-sign.jaml"),
            // (?i): the loader matches keys and enum values case-insensitively, so the grammar does too.
            Pattern($@"(?i)\b(?:{disc})(?=\s*:)", "entity.name.tag.discriminator.jaml"),
            Pattern($@"(?i)\b(?:{others})(?=\s*:)", "keyword.other.key.jaml"),
            Pattern($@"(?i)\b(?:{enums})\b", "support.constant.enum.jaml"),
            Pattern(@"\b(?:true|false)\b", "constant.language.boolean.jaml"),
            Pattern(@"\b\d+\b", "constant.numeric.jaml"),
            Pattern("\"[^\"]*\"", "string.quoted.double.jaml"),
            Pattern("'[^']*'", "string.quoted.single.jaml"),
        };

        return new JsonObject
        {
            ["name"] = "JAML",
            ["scopeName"] = ScopeName,
            ["fileTypes"] = new JsonArray("jaml"),
            ["patterns"] = patterns,
        };
    }

    /// <summary>Every key that is not a discriminator: root, clause, sources, with, payload, slot, entry.</summary>
    private static IEnumerable<string> OtherKeys()
    {
        foreach (var k in FilterVocabulary.RootKeys)
            yield return k;
        foreach (var k in FilterVocabulary.WithBlockKeys)
            yield return k;
        foreach (var t in new[]
                 {
                     typeof(JamlConfig), typeof(MotelyWith), typeof(JamlStandardCardSlot),
                     typeof(AndPayload), typeof(OrPayload), typeof(SoulOrderEntry),
                 })
        foreach (var p in FilterVocabulary.ClauseProperties(t))
            yield return Naming.Apply(p.Name);

        foreach (var disc in FilterVocabulary.DiscriminatorTable)
        {
            foreach (var p in FilterVocabulary.ClauseProperties(disc.ClauseType))
                yield return Naming.Apply(p.Name);
            foreach (var k in FilterVocabulary.SourceKeysFor(disc.Key))
                yield return k;
        }
    }

    private static IReadOnlyList<string> EnumNames() =>
    [
        .. FilterVocabulary
            .ValueEnumKinds.SelectMany(k => Enum.GetNames(k.EnumType))
            .Where(IsWord)
            // Longest first: a shorter name can never shadow a longer one mid-token.
            .OrderByDescending(n => n.Length)
            .ThenBy(n => n, StringComparer.Ordinal),
    ];

    private static string Keys(IEnumerable<string> keys) =>
        string.Join(
            "|",
            keys.Where(IsWord)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(k => k.Length)
                .ThenBy(k => k, StringComparer.Ordinal)
        );

    private static JsonObject Pattern(string match, string name) =>
        new() { ["match"] = match, ["name"] = name };

    /// <summary>Grammar alternation is only ever built from plain word tokens; refuse anything else.</summary>
    private static bool IsWord(string s) => s.Length > 0 && s.All(c => char.IsLetterOrDigit(c) || c == '_');
}
