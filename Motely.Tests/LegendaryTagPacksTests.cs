namespace Motely.Tests;

/// <summary>
/// E2: <c>sources: { tagPacks: [...] }</c> on <c>legendaryJoker:</c>. A Charm Tag opens a Mega
/// Arcana at blind select, before the ante's shop, and takes the ante's next five
/// <c>soul_Tarot&lt;ante&gt;</c> draws. The Soul in it is opened before any shop-pack Soul, so it is the
/// run's next <c>Joker4</c> draw — the same walk <see cref="LegendarySoulMatcher"/> runs for shop
/// packs, just earlier in the stream. <c>0</c> = the ante's first Mega (small-blind Charm), <c>1</c>
/// = the second (big-blind Charm); each takes the next five draws, exactly as the E1 tarot walk in
/// <see cref="TagPacksTests"/> reads the matching <c>ar&lt;ante&gt;</c> stream.
///
/// Seeds were found by the probe filters and audited against the raw <c>soul_Tarot1</c> stream:
/// <see cref="CharmSouls_AreTheAntesFirstFiveSoulDraws"/> reads those draws straight from the PRNG,
/// with no clause involved.
/// </summary>
public sealed class LegendaryTagPacksTests
{
    // The ante-1 Charm's five soul draws contain The Soul; no others do.
    private static readonly string[] SmallBlindCharm = ["AWN76TUC", "VEHJ7CJS", "FN82QGCK", "8XAYE7EW", "U8P5EATI", "N1ONX4AI"];

    // The ante-1 Charm draws 1-5 are clean and draws 6-10 contain The Soul, so only the second
    // (big-blind) Charm has it.
    private static readonly string[] BigBlindCharm = ["1PZDYIIZ", "1SLSJR64", "27Y2V2AE", "2PMH5A32", "GAQWKO66", "GRQNO3T6"];

    private static string SoulOnly(string tagPacks) =>
        $"""
        name: legendary-tag-packs
        deck: Red
        stake: White
        must:
          - legendaryJoker: []
            soulCardOnly: true
            antes: [1]
            sources:
              tagPacks: {tagPacks}
        """;

    private static string Legendary(string face, string tagPacks) =>
        $"""
        name: legendary-tag-packs
        deck: Red
        stake: White
        must:
          - legendaryJoker: {face}
            antes: [1]
            sources:
              tagPacks: {tagPacks}
        """;

    [Fact]
    public void Soul_InSmallBlindCharm() =>
        ProofSearch.MustMatchAll(SoulOnly("[0]"), SmallBlindCharm);

    [Fact]
    public void Soul_NotInSmallBlindCharm_WhenTheSoulIsInTheSecondMega() =>
        ProofSearch.MustMatchNone(SoulOnly("[0]"), BigBlindCharm);

    [Fact]
    public void Soul_InBigBlindCharm_IsTheSecondMega() =>
        ProofSearch.MustMatchAll(SoulOnly("[1]"), BigBlindCharm);

    [Fact]
    public void Soul_NotInBigBlindCharm_WhenOnlyTheFirstMegaHasIt() =>
        ProofSearch.MustMatchNone(SoulOnly("[1]"), SmallBlindCharm);

    [Fact]
    public void Legendary_FromSmallBlindCharm_IsTheRunsFirstSoul()
    {
        // VEHJ7CJS's Charm Soul is the run's first Soul, so it draws Joker4 #0 = Perkeo.
        ProofSearch.MustMatchAll(Legendary("Perkeo", "[0]"), "VEHJ7CJS");
        // Same Charm, other faces: the walk gave Chicot and Triboulet.
        ProofSearch.MustMatchNone(Legendary("Perkeo", "[0]"), "AWN76TUC", "N1ONX4AI");
        // 27Y2V2AE's first Soul (Perkeo) is in the *second* Mega, out of this clause's scope.
        ProofSearch.MustMatchNone(Legendary("Perkeo", "[0]"), "27Y2V2AE");
    }

    [Fact]
    public void Legendary_FromBigBlindCharm_IsTheRunsFirstSoul()
    {
        ProofSearch.MustMatchAll(Legendary("Yorick", "[1]"), "1PZDYIIZ", "1SLSJR64");
        // Perkeo and Triboulet come from the same second Mega: the face is right, the filter isn't.
        ProofSearch.MustMatchNone(Legendary("Yorick", "[1]"), "27Y2V2AE", "2PMH5A32");
    }

    /// <summary>
    /// Reads the seed's ante-1 Charm soul draws straight from the PRNG — the same five draws
    /// <c>TagPacksTests</c> reads from <c>ar1</c> for the tarot cards. One draw &gt; 0.997 is The Soul
    /// (Balatro's <c>soul_Tarot1</c> rate). No clause, no matcher: this is the game law the seeds above
    /// were chosen for.
    /// </summary>
    [Fact]
    public void CharmSouls_AreTheAntesFirstFiveSoulDraws()
    {
        var draws = new Dictionary<string, double[]>();
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(["VEHJ7CJS", "1PZDYIIZ"], 2)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(ctx =>
            {
                var tarot = ctx.CreateArcanaPackTarotStream(1, soulOnly: true);
                var rolls = new double[10];
                for (int i = 0; i < rolls.Length; i++)
                    rolls[i] = ctx.GetNextRandom(ref tarot.SoulPrngStream);

                lock (draws)
                    draws[ctx.GetSeed()] = rolls;
                return 0;
            });

        using var search = settings.Start();
        search.AwaitCompletion();

        var first = draws["VEHJ7CJS"];
        Assert.True(first[0] > 0.997, "VEHJ7CJS opens The Soul on its first Charm draw.");
        Assert.Single(first.Take(5), r => r > 0.997);
        Assert.DoesNotContain(first.Skip(5), r => r > 0.997);

        var second = draws["1PZDYIIZ"];
        Assert.DoesNotContain(second.Take(5), r => r > 0.997);
        Assert.Single(second.Skip(5), r => r > 0.997);
    }
}
