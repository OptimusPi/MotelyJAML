using System.Text;

namespace Motely.Tests;

/// <summary>
/// E17: <c>soulOrder:</c> — the run-wide order Souls turn into legendaries. Balatro keys the
/// legendary pool <c>Joker4</c> with no ante, so the Nth Soul opened in the run gets draw N.
/// With <c>showman: true</c> the pool keeps duplicates, so the effective order is the raw
/// <c>Joker4</c> sequence (the same one <c>jamlyze_seeds</c> reports, E3/S3).
/// </summary>
public sealed class LegendarySoulOrderTests
{
    private static string SoulOrderYaml(string[] legendaries, bool showman, int maxAnte = 8)
    {
        var sb = new StringBuilder();
        sb.AppendLine("name: soul-order");
        sb.AppendLine("deck: Red");
        sb.AppendLine("stake: White");
        sb.AppendLine("must:");
        sb.AppendLine("  - soulOrder:");
        foreach (var legendary in legendaries)
            sb.AppendLine($"      - {{ legendary: {legendary} }}");
        sb.AppendLine($"    maxAnte: {maxAnte}");
        sb.AppendLine($"    showman: {(showman ? "true" : "false")}");
        return sb.ToString();
    }

    [Fact]
    public void SoulOrder_LoadsAndValidates()
    {
        var config = ProofSearch.LoadOrThrow(
            """
            name: soul-order
            deck: Red
            stake: White
            must:
              - soulOrder:
                  - { legendary: Perkeo, antes: [1] }
                  - { legendary: Yorick }
                maxAnte: 8
                showman: true
            """
        );

        var clause = Assert.IsType<LegendarySoulOrderClause>(Assert.Single(config.Must));
        Assert.Equal(2, clause.Entries.Length);
        Assert.Equal(MotelyJoker.Perkeo, clause.Entries[0].Legendary);
        Assert.Equal([1], clause.Entries[0].Antes);
        Assert.Equal(MotelyJoker.Yorick, clause.Entries[1].Legendary);
        Assert.True(clause.Showman);
    }

    [Fact]
    public void SoulOrder_RejectsNonLegendary()
    {
        var ok = YamlConfigLoader.TryLoad(
            """
            name: bad
            must:
              - soulOrder:
                  - { legendary: Joker }
            """,
            out _,
            out var error
        );

        Assert.False(ok);
        Assert.Contains("not a legendary", error);
    }

    [Fact]
    public void SoulOrder_RejectsEmptyList()
    {
        var ok = YamlConfigLoader.TryLoad(
            """
            name: empty
            must:
              - soulOrder: []
            """,
            out _,
            out var error
        );

        Assert.False(ok);
        Assert.Contains("at least one soul", error);
    }

    // 43GS2F56 raw Joker4 = Perkeo, Yorick, Yorick, Yorick (E3 acceptance).
    [Fact]
    public void SoulOrder_RawSequence_MatchesRunWideOrder()
    {
        ProofSearch.MustMatchAll(
            SoulOrderYaml(["Perkeo", "Yorick", "Yorick", "Yorick"], showman: true),
            "43GS2F56"
        );
    }

    [Fact]
    public void SoulOrder_OrderIsEnforced()
    {
        ProofSearch.MustMatchNone(SoulOrderYaml(["Yorick", "Perkeo"], showman: true), "43GS2F56");
    }

    // KSUCKHKT raw Joker4 = Perkeo, Perkeo, Chicot (E3 acceptance).
    [Fact]
    public void SoulOrder_Showman_KeepsRawDuplicate()
    {
        ProofSearch.MustMatchAll(
            SoulOrderYaml(["Perkeo", "Perkeo"], showman: true),
            "KSUCKHKT"
        );
    }

    // Game-accurate (default): the second Soul cannot repeat the first, so it resamples and the
    // raw Perkeo,Perkeo,Chicot prefix no longer matches.
    [Fact]
    public void SoulOrder_Default_ResamplesDuplicate()
    {
        ProofSearch.MustMatchNone(SoulOrderYaml(["Perkeo", "Perkeo"], showman: false), "KSUCKHKT");
    }

    [Fact]
    public void SoulOrder_AnteRange_PinsTheSoul()
    {
        // Perkeo must be the first Soul; requiring it in an impossible ante must not match.
        ProofSearch.MustMatchNone(
            SoulOrderYaml(["Yorick", "Perkeo"], showman: true, maxAnte: 1),
            "43GS2F56"
        );
    }
}
