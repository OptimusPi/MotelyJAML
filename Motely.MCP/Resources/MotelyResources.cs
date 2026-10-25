using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Motely.MCP.Resources;

/// <summary>
/// What an agent needs to write its own filter: a short JAML primer with working examples, and the
/// full JSON schema the loader binds against. Both pair with the <c>jaml</c> argument of
/// seed_validate and seed_search, so a filter never has to be saved on the server first.
/// </summary>
[McpServerResourceType]
public sealed class MotelyResources
{
    /// <summary>The primer's example filters, one per fenced block; tests load each one.</summary>
    internal static readonly string[] GuideExamples =
    [
        """
        name: Eye in ante 3 or 4
        deck: Red
        stake: White
        must:
          - boss: TheEye
            antes: [3, 4]
        """,
        """
        name: Perkeo with Black Hole
        deck: Red
        stake: White
        must:
          - legendaryJoker: Perkeo
            antes: [1, 2, 3, 4]
        should:
          - spectralCard: BlackHole
            score: 80
          - uncommonJoker: Constellation
            score: 60
        """,
        """
        name: Charm tag, no Wall
        deck: Zodiac
        stake: White
        must:
          - tag: CharmTag
            antes: [1, 2]
        mustNot:
          - boss: TheWall
            antes: [2, 3]
        """,
    ];

    [McpServerResource(UriTemplate = "jaml://guide", Name = "jaml-guide", MimeType = "text/markdown")]
    [Description("How to write a JAML filter, with working examples to pass as seed_search's `jaml`.")]
    public static string Guide() =>
        $$"""
        # Writing a JAML filter

        A filter is YAML. Pass its text as `jaml` to seed_validate, then to seed_search.

        - `deck`, `stake`: the run's deck and stake (default Red, White). They change what a seed holds.
        - `must`: every clause has to hold, or the seed is rejected. Keep it short and strict: this
          is what makes the search fast.
        - `should`: optional extras; each one that holds adds its `score` (default 1).
        - `mustNot`: any clause that holds rejects the seed.
        - `seeds`: a saved list, run by mode `replay`.

        A clause is one key naming the thing, plus the antes to look in: `antes: [1, 2]`. Always
        give `antes`. Names are PascalCase with no spaces: `TheEye`, `BlackHole`, `CharmTag`,
        `Perkeo`. Common keys: `joker`, `commonJoker`, `uncommonJoker`, `rareJoker`,
        `legendaryJoker`, `voucher`, `tag`, `boss`, `tarotCard`, `planetCard`, `spectralCard`,
        `standardCard`, `boosterPack`. A joker or card clause also takes `edition` (Foil,
        Holographic, Polychrome, Negative) and `sources`, which narrows where it may appear, e.g.
        `sources: { shopItems: [0, 1, 2], boosterPacks: [0, 1] }` (slot indexes from 0). The
        resource jaml://schema lists every key and every valid name; a wrong one makes
        seed_validate name the problem.

        `joker` finds a joker of any rarity, so use it unless you mean to narrow the source:
        `rareJoker: Blueprint` and `joker: Blueprint` both work, `commonJoker: Blueprint` is
        rejected by seed_validate. Bosses have a first ante: most can appear from ante 1, but The Arm, Fish, Flint,
        House, Mark, Mouth, Needle, Wall, Water and Wheel start at ante 2, The Eye and The Tooth at
        3, The Plant at 4, The Serpent at 5, The Ox at 6. Ante 8 is always a showdown boss
        (AmberAcorn, CeruleanBell, CrimsonHeart, VerdantLeaf, VioletVessel). seed_validate warns
        when a clause asks for an impossible ante. A search that finds 0 matches in millions of
        seeds usually means a clause that can't hold; loosen it or seed_analyze a near miss.

        Tip: filter_list shows the server's own filters, and every one of them is a working example.

        ## Examples

        ```yaml
        {{GuideExamples[0]}}
        ```

        ```yaml
        {{GuideExamples[1]}}
        ```

        ```yaml
        {{GuideExamples[2]}}
        ```
        """;

    [McpServerResource(UriTemplate = "jaml://schema", Name = "jaml-schema", MimeType = "application/json")]
    [Description("The JSON schema of a JAML filter: every clause key, option and valid name.")]
    public static string Schema()
    {
        using var stream =
            typeof(MotelyResources).Assembly.GetManifestResourceStream("motely-filter.schema.json")
            ?? throw new InvalidOperationException("motely-filter.schema.json is not embedded in Motely.MCP.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
