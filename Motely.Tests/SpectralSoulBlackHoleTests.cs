namespace Motely.Tests;

/// <summary>
/// Balatro's <c>create_card</c> makes two checks for every Spectral card, both on the ante's
/// <c>soul_Spectral{ante}</c> stream: the Soul check, then the Black Hole check. The Black Hole check
/// overwrites <c>forced_key</c>, so a card that rolls both is a Black Hole, and a Soul never skips the
/// Black Hole roll. A check for a card the pack already holds is skipped and pulls no roll
/// (<c>used_jokers</c>). Immolate's <c>next_spectral</c> is the same law.
///
/// The engine used to return The Soul as soon as the Soul check passed. That kept Souls the game
/// turns into Black Holes, and it skipped the card's Black Hole roll, so every later Soul / Black Hole
/// check in the ante read the stream one roll early: Black Holes moved, and real Souls in later
/// packs became Black Holes.
///
/// Every expectation here comes from an independent LuaJIT model of the game's ante-1 packs (booster
/// pool, Soul / Black Hole checks, the <c>Spectralspe1</c> pool with in-pack resamples). On 66,500
/// seeds (6,500 picked for a Soul or Black Hole in an ante-1 Spectral pack, 60,000 random) the model
/// and the engine agree card for card on all 14,866 ante-1 Spectral packs; the old engine got 30 of
/// the first 7,041 wrong. Each test below fails on the old engine.
/// </summary>
public sealed class SpectralSoulBlackHoleTests
{
    private static MotelyItemType S(MotelySpectralCard card) =>
        (MotelyItemType)((int)MotelyItemTypeCategory.SpectralCard | (int)card);

    private static readonly MotelyItemType Soul = MotelyItemType.TheSoul;
    private static readonly MotelyItemType BlackHole = MotelyItemType.BlackHole;

    /// <summary>Every Spectral pack in ante 1's slots 0-3, card by card, per the game.</summary>
    private static readonly Dictionary<string, Dictionary<int, MotelyItemType[]>> Ante1SpectralPacks = new()
    {
        // Card 1 rolls both checks: a Black Hole, and the card the Soul used to take comes from
        // the pool. 57YFYQ9N: Soul 0.99786, Black Hole 0.99920. 6MIQH44E: 0.99751, 0.99736.
        ["57YFYQ9N"] = new() { [1] = [BlackHole, S(MotelySpectralCard.Sigil), S(MotelySpectralCard.Wraith), S(MotelySpectralCard.Grim)] },
        ["6MIQH44E"] = new() { [2] = [BlackHole, S(MotelySpectralCard.Hex)] },
        // Card 2 rolls both (Soul 0.99827, Black Hole 0.99881).
        ["LWSTA24F"] = new() { [2] = [S(MotelySpectralCard.Ankh), BlackHole] },
        // The Soul's own Black Hole roll is pulled, so the card-4 check reads the right roll.
        ["97838HCI"] = new() { [1] = [S(MotelySpectralCard.Aura), Soul, S(MotelySpectralCard.Incantation), BlackHole] },
        ["CCV8MXSF"] = new()
        {
            [2] = [S(MotelySpectralCard.Talisman), Soul, S(MotelySpectralCard.Immolate), BlackHole],
            [3] = [S(MotelySpectralCard.Incantation), S(MotelySpectralCard.Wraith)],
        },
        // A Soul in slot 1 used to shift the stream: slot 3's real Soul read as a Black Hole.
        ["1NP9F12Q"] = new()
        {
            [1] = [Soul, S(MotelySpectralCard.Aura)],
            [3] = [Soul, S(MotelySpectralCard.Ectoplasm), S(MotelySpectralCard.Sigil), S(MotelySpectralCard.Familiar)],
        },
        ["NW14R7GY"] = new()
        {
            [1] = [Soul, S(MotelySpectralCard.Incantation)],
            [3] = [S(MotelySpectralCard.Incantation), Soul],
        },
    };

    private static string SpectralCard(string card) =>
        $"""
        name: spectral-soul-black-hole
        deck: Red
        stake: White
        must:
          - spectralCard: {card}
            antes: [1]
            sources:
              boosterPacks: [0, 1, 2, 3]
        """;

    private const string AnySoulAnte1 = """
        name: spectral-soul-black-hole-legendary
        deck: Red
        stake: White
        must:
          - legendaryJoker: []
            soulCardOnly: true
            antes: [1]
        """;

    [Fact]
    public void Ante1SpectralPacks_MatchTheGameCardForCard()
    {
        string[] seeds = [.. Ante1SpectralPacks.Keys];
        var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<int, MotelyItemType[]>>();

        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(seeds, seeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(ctx =>
            {
                var packs = new Dictionary<int, MotelyItemType[]>();
                var packStream = ctx.CreateBoosterPackStream(1);
                // One stream for the whole ante: Spectral packs share soul_Spectral1.
                var spectralStream = ctx.CreateSpectralPackSpectralStream(1);
                for (int slot = 0; slot <= 3; slot++)
                {
                    var pack = ctx.GetNextBoosterPack(ref packStream);
                    if (pack.GetPackType() != MotelyBoosterPackType.Spectral)
                        continue;
                    var contents = ctx.GetNextSpectralPackContents(ref spectralStream, pack.GetPackSize());
                    var cards = new MotelyItemType[contents.Length];
                    for (int i = 0; i < cards.Length; i++)
                        cards[i] = contents[i].Type;
                    packs[slot] = cards;
                }
                seen[ctx.GetSeed()] = packs;
                return 1;
            });

        using (var search = settings.Start())
            search.AwaitCompletion();

        Assert.Equal(seeds.Length, seen.Count);
        foreach (var (seed, expectedPacks) in Ante1SpectralPacks)
        {
            var actual = seen[seed];
            Assert.Equal(expectedPacks.Keys.Order(), actual.Keys.Order());
            foreach (var (slot, cards) in expectedPacks)
            {
                Assert.True(
                    cards.SequenceEqual(actual[slot]),
                    $"{seed} slot {slot}: expected [{string.Join(", ", cards)}], got [{string.Join(", ", actual[slot])}]"
                );
            }
        }
    }

    // A card that rolls both checks is a Black Hole: no Soul in the pack.
    // D771VT5H: slot 3, card 1 (Soul 0.99762, Black Hole 0.99820).
    [Fact]
    public void SoulAndBlackHoleOnOneCard_IsABlackHole()
    {
        ProofSearch.MustMatchNone(SpectralCard("TheSoul"), "D771VT5H", "LWSTA24F", "57YFYQ9N", "6MIQH44E");
        ProofSearch.MustMatchAll(SpectralCard("BlackHole"), "LWSTA24F");
    }

    // The legendary walk (LegendarySoulMatcher, GetNextSpectralPackHasTheSoul) follows the same law:
    // no Soul, no legendary. 66ONV37F's double roll sits in slot 5 (card 1: Soul 0.99930, Black Hole
    // 0.99991), which ante 1 reaches through ante 2's Hieroglyph.
    [Fact]
    public void SoulAndBlackHoleOnOneCard_GivesNoLegendary() =>
        ProofSearch.MustMatchNone(AnySoulAnte1, "D771VT5H", "66ONV37F");

    // The card a kept Soul used to take is drawn from the Spectral pool. These clauses run the SIMD
    // pack walk first and the scalar confirm after, so both halves have to see the card.
    [Fact]
    public void TheCardAfterTheBlackHole_ComesFromThePool_OnBothHalves()
    {
        ProofSearch.MustMatchAll(SpectralCard("Grim"), "57YFYQ9N");
        ProofSearch.MustMatchAll(SpectralCard("Hex"), "6MIQH44E");
    }

    // A Soul still pulls its card's Black Hole roll, so later checks in the ante stay in step.
    [Fact]
    public void ASoulStillRollsItsBlackHole_SoLaterChecksStayInStep()
    {
        ProofSearch.MustMatchAll(SpectralCard("BlackHole"), "97838HCI", "CCV8MXSF");
        ProofSearch.MustMatchNone(SpectralCard("BlackHole"), "1NP9F12Q", "NW14R7GY");
    }
}
