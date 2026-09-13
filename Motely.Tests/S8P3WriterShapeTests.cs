using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// S8.P3 — <c>ToJaml</c> canonical fixed point for clause shapes the corpus round-trip
/// misses: event rolls clauses, startingDraw, pokerHand, misprint, erratic, planet, and
/// nested logic. Law: load → write → reload → write reproduces the identical text, and the
/// reload carries every field the first load did.
/// </summary>
public sealed class S8P3WriterShapeTests
{
    [Theory]
    [InlineData("""
        must:
          - planetCard: [Pluto, Mercury]
            antes: [1, 2]
        """)]
    [InlineData("""
        must:
          - startingDraw:
            rank: Ace
            suit: Hearts
        """)]
    [InlineData("""
        must:
          - pokerHand: [Flush, FullHouse]
            antes: [1]
        """)]
    [InlineData("""
        must:
          - misprintMult: [0, 1]
        """)]
    [InlineData("""
        must:
          - spaceLevelup: [0, 1]
          - businessPayout: [0]
          - bloodstoneTrigger: [0]
          - parkingPayout: [0]
          - glassDestroy: [0]
          - wheelStaysFlipped: [0]
        """)]
    [InlineData("""
        must:
          - luckyMoney: [0, 1, 2]
            with:
              luck: X2
          - luckyMult: [0]
        """)]
    [InlineData("""
        deck: Erratic
        must:
          - erraticRank: [Ace, King]
            min: 2
          - erraticSuit: [Hearts]
        """)]
    [InlineData("""
        must:
          - or:
              - joker: Blueprint
              - and:
                  - voucher: Overstock
                  - tag: [CharmTag]
        """)]
    [InlineData("""
        must:
          - or:
              - smallBlindTag: NegativeTag
              - bigBlindTag: [CharmTag, DoubleTag]
              - tags: [NegativeTag]
                rolls: [0, 1, 2]
            antes: [1, 2, 3]
        """)]
    [InlineData("""
        must:
          - pokerHand: [Flush]
            rolls: [0, 1, 2]
        """)]
    [InlineData("""
        description: |
          Two lines,
          one of them with a # hash.
        must:
          - joker: [Blueprint]
            label: 'a "quoted" label: with a colon'
        """)]
    public void ToJaml_IsACanonicalFixedPoint(string jaml)
    {
        var original = JamlConfigLoader.FromJaml(jaml);
        var written = JamlConfigLoader.ToJaml(original);

        Assert.True(
            JamlConfigLoader.TryLoad(written, out var reloaded, out var error),
            $"written JAML failed to reload: {error}\n---\n{written}"
        );
        Assert.NotNull(reloaded);
        Assert.Equal(original.Must.Count, reloaded.Must.Count);
        Assert.Equal(
            original.Must.Select(c => c.GetType()).ToArray(),
            reloaded.Must.Select(c => c.GetType()).ToArray()
        );
        JamlConfigEquality.AssertEqual(original, reloaded);

        var rewritten = JamlConfigLoader.ToJaml(reloaded);
        Assert.Equal(written, rewritten);
    }
}
