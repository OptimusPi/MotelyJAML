namespace Motely.Tests;

/// <summary>
/// E1: <c>sources: { tagPacks: [...] }</c>. A Charm Tag opens a Mega Arcana at blind select, before
/// the ante's shop, and takes the ante's first five <c>ar1</c> draws. Seeds and real Charm
/// contents are from the LuckyCatBeforeR1 audit.
/// </summary>
public sealed class TagPacksTests
{
    private static string Jaml(string tarot, string tagPacks) =>
        $"""
        name: tag-packs
        deck: Red
        stake: White
        must:
          - tarotCard: {tarot}
            antes: [1]
            sources:
              tagPacks: {tagPacks}
        """;

    [Fact]
    public void Magician_InSmallBlindCharm() =>
        ProofSearch.MustMatchAll(Jaml("TheMagician", "[0]"), "5MHF1111", "KSUCKHKT");

    [Fact]
    public void Judgement_InSmallBlindCharm() =>
        ProofSearch.MustMatchAll(Jaml("Judgement", "[0]"), "5MHF1111", "KSUCKHKT", "KVK31111");

    [Fact]
    public void Judgement_NotInCharm_5NRTBOOB() =>
        ProofSearch.MustMatchNone(Jaml("Judgement", "[0]"), "5NRTBOOB");

    [Fact]
    public void Magician_NotInCharm_KVK31111() =>
        ProofSearch.MustMatchNone(Jaml("TheMagician", "[0]"), "KVK31111");

    [Fact]
    public void Judgement_InBigBlindCharm_1BDMSUN1() =>
        ProofSearch.MustMatchAll(Jaml("Judgement", "[1]"), "1BDMSUN1");
}
