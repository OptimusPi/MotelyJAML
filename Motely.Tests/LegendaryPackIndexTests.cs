namespace Motely.Tests;

public sealed class LegendaryPackIndexTests
{
    private static readonly string[] AnteOneSoulSeeds = ["C3DW94B8", "3MLDLU2W", "9KIXS1TL"];

    private static readonly string[] PhantomSlotSeeds = ["6E7ZRTWV", "AF2C8QH3"];

    private static readonly string[] AnteZeroPerkeoSeeds =
    [
        "F2U88X11",
        "JX8C8X11",
        "L8FJ8X11",
        "A68EBX11",
    ];

    private static string LegendaryAtArcanaSlot(int slot) =>
        $"""
        name: legendary-slot-{slot}
        deck: Red
        stake: White
        must:
          - legendaryJoker: []
            antes: [1]
            sources:
              arcanaPacks: [{slot}]
        """;

    private static string PackAtSlot(int slot, string packs, int ante = 1) =>
        $"""
        name: pack-slot-{slot}
        deck: Red
        stake: White
        must:
          - boosterPack: [{packs}]
            antes: [{ante}]
            rolls: [{slot}]
        """;

    [Fact]
    public void LegendaryArcanaSlot_IsTheSamePhysicalPack_AsTheBoosterPackClause()
    {
        int legendaryHits = 0;
        for (int slot = 0; slot <= MotelyGlobals.EarlyAnteMaxPackSlot; slot++)
        {
            var (_, legendary) = ProofSearch.ListMatch(LegendaryAtArcanaSlot(slot), AnteOneSoulSeeds);
            var (_, arcana) = ProofSearch.ListMatch(
                PackAtSlot(slot, "Arcana, JumboArcana, MegaArcana"),
                AnteOneSoulSeeds
            );
            foreach (var seed in legendary)
                Assert.Contains(seed, arcana);
            legendaryHits += legendary.Count;
        }
        Assert.Equal(AnteOneSoulSeeds.Length, legendaryHits);
    }

    [Fact]
    public void AnteOneSlotZero_IsTheFixedBuffoon_ForLegendariesToo()
    {
        ProofSearch.MustMatchAll(PackAtSlot(0, "Buffoon"), AnteOneSoulSeeds);
        ProofSearch.MustMatchNone(LegendaryAtArcanaSlot(0), AnteOneSoulSeeds);
    }

    [Fact]
    public void AnteOne_HasNoFifthPack_ForLegendaries()
    {
        ProofSearch.MustMatchNone(
            """
            name: phantom-slot
            deck: Red
            stake: White
            must:
              - legendaryJoker: Perkeo
                antes: [1]
                sources:
                  arcanaPacks: [3]
            """,
            PhantomSlotSeeds
        );
    }

    [Fact]
    public void AnteZero_HasNoFixedBuffoon_ForAnyFamily()
    {
        const string hieroglyph = """
              - voucher: Hieroglyph
                antes: [1]
            """;

        ProofSearch.MustMatchNone(
            PackAtSlot(0, "Buffoon", ante: 0) + "\n" + hieroglyph,
            AnteZeroPerkeoSeeds
        );

        var (_, arcanaFirst) = ProofSearch.ListMatch(
            PackAtSlot(0, "Arcana", ante: 0) + "\n" + hieroglyph,
            AnteZeroPerkeoSeeds
        );
        Assert.Equal(["A68EBX11", "F2U88X11"], arcanaFirst.OrderBy(s => s, StringComparer.Ordinal));

        ProofSearch.MustMatchAll(
            """
            name: zerkeo
            deck: Anaglyph
            stake: White
            must:
              - legendaryJoker: Perkeo
                edition: Negative
                antes: [0]
                sources:
                  boosterPacks: [0, 1]
              - voucher: Hieroglyph
                antes: [1]
            """,
            AnteZeroPerkeoSeeds
        );
    }
}
