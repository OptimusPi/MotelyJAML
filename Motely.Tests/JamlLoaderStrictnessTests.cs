using Motely.Filters.Jaml;
using Motely.Filters.Native;

namespace Motely.Tests;

/// <summary>
/// The loader distinguishes absent from malformed. A key that is not written takes its default;
/// a key that is written with a value the grammar cannot read is a positioned error, never a
/// silent default (<c>min: two</c> used to load as min 1, <c>requireMegaPack: yes</c> as false).
/// The same rail rejects a <c>sources:</c> block that names only modifiers, keys nothing reads,
/// negative rolls, and ante 0 on the three families whose streams begin at ante 1.
/// </summary>
public sealed class JamlLoaderStrictnessTests
{
    private static string LoadError(string jaml)
    {
        Assert.False(JamlConfigLoader.TryLoad(jaml, out _, out var error), "expected the loader to reject:\n" + jaml);
        return error!;
    }

    private static JamlConfig Load(string jaml)
    {
        Assert.True(JamlConfigLoader.TryLoad(jaml, out var config, out var error), error);
        return config!;
    }

    // ── (a) malformed scalars ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("min: two", "'two'")]
    [InlineData("score: 1e3", "'1e3'")]
    [InlineData("max: x", "'x'")]
    [InlineData("min: [1, 2]", "single value")]
    public void MalformedInt_IsAnErrorAtItsLine_NotADefault(string tail, string expectedInMessage)
    {
        var error = LoadError(
            $"""
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1]
                {tail}
            """
        );
        Assert.Contains("line 6", error);
        Assert.Contains(expectedInMessage, error);
    }

    [Fact]
    public void MalformedBool_IsAnErrorAtItsLine_NotFalse()
    {
        var error = LoadError(
            """
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1]
                sources:
                  shopItems: [0, 1]
                  requireMegaPack: maybe
            """
        );
        Assert.Contains("line 8", error);
        Assert.Contains("'maybe'", error);
    }

    /// <summary>One bool spelling for the whole grammar: the block loader reads yes/no the way
    /// the clause value reader always has.</summary>
    [Theory]
    [InlineData("requireMegaPack: yes", true)]
    [InlineData("requireMega: no", false)]
    [InlineData("requireMegaPack: True", true)]
    public void YesNo_ReadsAsABool_EverywhereAFlagIsRead(string flag, bool expected)
    {
        var config = Load(
            $"""
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1]
                sources:
                  boosterPacks: [0, 1]
                  {flag}
            """
        );
        var clause = Assert.IsType<JokerClause>(config.Must[0]);
        Assert.Equal(expected, clause.Sources!.RequireMegaPack);
    }

    [Fact]
    public void BlankAndAbsentKeys_StillTakeTheirDefaults()
    {
        var config = Load(
            """
            deck: Red
            stake: White
            should:
              - joker: Blueprint
                antes: [1]
                max:
            """
        );
        var clause = config.Should[0];
        Assert.Equal(1, clause.Min);
        Assert.Null(clause.Max);
        Assert.Equal(1, clause.Score);
    }

    [Fact]
    public void MalformedAnteToken_IsAnErrorAtItsLine()
    {
        var error = LoadError(
            """
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1, two]
            """
        );
        Assert.Contains("line 5", error);
        Assert.Contains("'two'", error);
    }

    // ── (b) sources blocks that name no slot ────────────────────────────────────────────────

    [Theory]
    [InlineData("joker: Blueprint", "requireMega: true")]
    [InlineData("joker: Blueprint", "requireMegaPack: false")]
    [InlineData("tarotCard: TheFool", "charmTag: true")]
    [InlineData("spectralCard: Ankh", "etherealTag: true")]
    public void SourcesNamingOnlyModifiers_IsAnErrorAtTheSourcesKey(string clause, string flag)
    {
        var error = LoadError(
            $"""
            deck: Red
            stake: White
            must:
              - {clause}
                antes: [1]
                sources:
                  {flag}
            """
        );
        Assert.Contains("line 6", error);
        Assert.Contains("sources names no slots", error);
    }

    [Fact]
    public void EmptySourcesBlock_StaysTheMatchNowhereOverride()
    {
        var config = Load(
            """
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [1]
                sources: {}
            """
        );
        var clause = Assert.IsType<JokerClause>(config.Must[0]);
        Assert.NotNull(clause.Sources);
        Assert.Empty(clause.Sources.ShopItems);
        Assert.Empty(clause.Sources.BoosterPacks);
    }

    /// <summary>omenGlobe alone walks every arcana slot in scoring, so it is a source, not a modifier.</summary>
    [Fact]
    public void OmenGlobeAlone_IsASourceInItsOwnRight()
    {
        var config = Load(
            """
            deck: Red
            stake: White
            must:
              - spectralCard: Ankh
                antes: [1]
                sources:
                  omenGlobe: true
            """
        );
        var clause = Assert.IsType<SpectralCardClause>(config.Must[0]);
        Assert.True(clause.Sources!.OmenGlobe);
    }

    // ── (c) keys nothing consumes ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("legendaryJoker: Perkeo", "soulCard: [0]")]
    [InlineData("standardCard:", "certificate: [0]")]
    [InlineData("standardCard:", "incantation: [0]")]
    [InlineData("standardCard:", "familiar: [0]")]
    [InlineData("standardCard:", "grim: [0]")]
    [InlineData("standardCard:", "deckDraw: [0]")]
    public void SourceKeysNothingReads_AreUnknownKeys(string clause, string sourceLine)
    {
        var error = LoadError(
            $"""
            deck: Red
            stake: White
            must:
              - {clause}
                antes: [1]
                sources:
                  boosterPacks: [0]
                  {sourceLine}
            """
        );
        Assert.Contains("line 8", error);
        Assert.Contains("Unknown", error);
        Assert.Contains($"'{sourceLine.Split(':')[0]}'", error);
    }

    [Fact]
    public void WithVouchers_IsAnUnknownWithKey()
    {
        var error = LoadError(
            """
            deck: Red
            stake: White
            should:
              - luckyMoney: [0, 1]
                with:
                  luck: X2
                  vouchers: [Overstock]
            """
        );
        Assert.Contains("line 7", error);
        Assert.Contains("Unknown with key: 'vouchers'", error);
    }

    // ── (d) rolls ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("voucher: Overstock", "rolls: [-1]", 6)]
    [InlineData("tag: CharmTag", "rolls: -2", 6)]
    [InlineData("boosterPack: Arcana", "rolls: [0, -3]", 6)]
    public void NegativeRolls_AreAnErrorAtTheRollsLine(string clause, string rolls, int line)
    {
        var error = LoadError(
            $"""
            deck: Red
            stake: White
            must:
              - {clause}
                antes: [1]
                {rolls}
            """
        );
        Assert.Contains($"line {line}", error);
        Assert.Contains("negative", error);
    }

    [Fact]
    public void NegativeInlineEventRoll_IsAnErrorAtTheDiscriminatorLine()
    {
        var error = LoadError(
            """
            deck: Red
            stake: White
            should:
              - luckyMoney: [0, -1]
            """
        );
        Assert.Contains("line 4", error);
        Assert.Contains("negative", error);
    }

    // ── (e) ante 0 ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("boss: TheWall", "antes: [0, 1]")]
    [InlineData("voucher: Overstock", "ante: 0")]
    [InlineData("tag: CharmTag", "antes: [0]")]
    [InlineData("smallBlindTag: CharmTag", "antes: 0")]
    public void AnteZero_IsRejectedForBossVoucherAndTag(string clause, string antes)
    {
        var error = LoadError(
            $"""
            deck: Red
            stake: White
            must:
              - {clause}
                {antes}
            """
        );
        Assert.Contains("line 5", error);
        Assert.Contains("start at ante 1", error);
    }

    /// <summary>Ante 0 is Hieroglyph's extra pack round: the shop and pack families keep it.</summary>
    [Fact]
    public void AnteZero_StaysLegalForShopAndPackFamilies()
    {
        var config = Load(
            """
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                antes: [0, 1]
              - boosterPack: Arcana
                ante: 0
            """
        );
        Assert.Equal([0, 1], ((JokerClause)config.Must[0]).Antes);
        Assert.Equal([0], ((BoosterPackClause)config.Must[1]).Antes);
    }

    private delegate void ContextBody(ref MotelySingleSearchContext ctx);

    private static Exception ScoreOnOneSeed(ContextBody body)
    {
        var settings = new MotelySearchSettings<PassthroughFilterDesc.PassthroughFilter>(
            new PassthroughFilterDesc()
        )
            .WithDeck(MotelyDeck.Red)
            .WithStake(MotelyStake.White)
            .WithSeedGenerator(["MOTELY77"], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithJimmolate(ctx =>
            {
                body(ref ctx);
                return 1;
            });

        using var search = settings.Start();
        return Assert.ThrowsAny<Exception>(search.AwaitCompletion);
    }

    /// <summary>A boss clause that never went through PrepareRunState (or asked about an ante it
    /// never cached) is a caller bug; scoring says so instead of dereferencing null.</summary>
    [Fact]
    public void BossScoring_WithoutCachedBosses_FailsWithTheCause_NotANullReference()
    {
        var clause = new BossClause { Bosses = [MotelyBossBlind.TheWall], Antes = [1] };

        var ex = ScoreOnOneSeed(
            (ref MotelySingleSearchContext ctx) =>
                JamlScoring.CountOccurrences(ref ctx, clause, new MotelyRunState())
        );

        var thrown = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("PrepareRunState", thrown.Message);
    }

    [Fact]
    public void BossScoring_AnteOutsideTheCachedRange_FailsWithTheCause_NotAnIndexFault()
    {
        var prepared = new BossClause { Bosses = [MotelyBossBlind.TheWall], Antes = [1] };
        var beyond = new BossClause { Bosses = [MotelyBossBlind.TheWall], Antes = [4] };

        var ex = ScoreOnOneSeed(
            (ref MotelySingleSearchContext ctx) =>
            {
                var runState = new MotelyRunState();
                JamlScoring.PrepareRunState(ref ctx, [prepared], runState);
                JamlScoring.CountOccurrences(ref ctx, beyond, runState);
            }
        );

        var thrown = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("outside the cached range", thrown.Message);
    }
}
