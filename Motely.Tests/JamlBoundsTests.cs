using Motely.Filters.Jaml;
using Motely.Lsp;

namespace Motely.Tests;

/// <summary>
/// The occurrence window, <c>min</c>/<c>max</c>, as one law from the text to the seed. The engine's
/// SIMD arms all assume <c>min ≥ 1</c> and call anything else a loader bug; a set <c>max</c> is a
/// ceiling at every value, never a "0 means unbounded" flag. So the loader rejects a window the
/// engine cannot honour, at the token, and the LSP shows exactly that.
/// </summary>
public sealed class JamlBoundsTests
{
    private static string Config(string clauseTail) =>
        $"""
        deck: Red
        stake: White
        must:
          - joker: Blueprint
            antes: [1]
        {clauseTail}
        """;

    // ── the loader ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("    max: 1")]
    [InlineData("    min: 1\n    max: 1")]
    [InlineData("    min: 2\n    max: 3")]
    [InlineData("    min: 2")]
    public void Load_AcceptsAWindowTheEngineCanHonour(string tail)
    {
        Assert.True(JamlConfigLoader.TryLoad(Config(tail), out _, out var error), error);
    }

    [Theory]
    [InlineData("    max: 0", "max: 0")]
    [InlineData("    min: 1\n    max: 0", "max: 0")]
    [InlineData("    min: 2\n    max: 1", "max: 1 is below min: 2")]
    [InlineData("    min: 0", "min: 0 must be at least 1")]
    [InlineData("    min: -1", "min: -1 must be at least 1")]
    [InlineData("    max: -1", "max: -1 is below min: 1")]
    public void Load_RejectsAWindowTheEngineCannotHonour(string tail, string expectedInMessage)
    {
        Assert.False(JamlConfigLoader.TryLoad(Config(tail), out _, out var error));
        Assert.Contains(expectedInMessage, error);
    }

    /// <summary>The terse spelling reaches the same gate through its continuation keys.</summary>
    [Fact]
    public void Load_TerseContinuation_IsHeldToTheSameWindow()
    {
        const string text = """
            deck: Red
            stake: White
            must:
              - Blueprint
                max: 0
            """;
        Assert.False(JamlConfigLoader.TryLoad(text, out _, out var error));
        Assert.Contains("max: 0", error);
    }

    // ── the LSP ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Diagnose_MaxBelowMin_UnderlinesTheMaxValue()
    {
        const string text = """
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                min: 2
                max: 1
            """;
        var d = Assert.Single(JamlLanguageService.Diagnose(text));
        Assert.Equal(JamlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("max: 1 is below min: 2", d.Message);
        Assert.Equal(5, d.Span.StartLine);
        Assert.Equal("    max: ".Length, d.Span.StartColumn);
    }

    [Fact]
    public void Diagnose_MaxZero_SaysWhyAtTheToken()
    {
        const string text = """
            deck: Red
            stake: White
            must:
              - joker: Blueprint
                max: 0
            """;
        var d = Assert.Single(JamlLanguageService.Diagnose(text));
        Assert.Contains("max: 0", d.Message);
        Assert.Contains("absence", d.Message);
        Assert.Equal(4, d.Span.StartLine);
    }

    // ── the engine ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The oracle for "max is a ceiling": over the same seeds, a clause with <c>max: 1</c> passes
    /// exactly the seeds that <c>min: 1</c> passes minus those that <c>min: 2</c> passes. No
    /// hand-typed seed list can drift from that identity; the engine is checked against itself
    /// under two different windows.
    /// </summary>
    [Fact]
    public void MaxOne_IsAtLeastOneMinusAtLeastTwo()
    {
        string[] seeds = ["ALEEB", "MOTELY77", "AAAAAAAA", "11111111", "PERKEO", "CAREFAKE", "BLUEPRNT", "12345678"];

        HashSet<string> Run(int min, int? max)
        {
            var config = new JamlConfig
            {
                Id = $"bounds-{min}-{max}",
                Deck = MotelyDeck.Red,
                Stake = MotelyStake.White,
            };
            config.Must.Add(
                new TarotCardClause
                {
                    Antes = [1, 2, 3, 4],
                    Min = min,
                    Max = max,
                    Sources = new TarotCardSourceConfig { ShopItems = [0, 1, 2, 3], BoosterPacks = [0, 1, 2, 3] },
                }
            );

            var hits = new HashSet<string>();
            using var search = JamlSearchBuilder
                .CreateSettings(config)
                .WithSeedGenerator(seeds, seeds.Length)
                .WithThreadCount(1)
                .WithQuietMode(true)
                .WithSeedMatchCallback(s => hits.Add(s))
                .Start();
            search.AwaitCompletion();
            return hits;
        }

        var atLeastOne = Run(1, null);
        var atLeastTwo = Run(2, null);
        var exactlyOne = Run(1, 1);

        Assert.True(atLeastOne.Count > 0, "the oracle needs at least one hit to say anything");
        Assert.Equal(atLeastOne.Except(atLeastTwo).ToHashSet(), exactlyOne);
    }

    [Theory]
    [InlineData(3, null, 3)]
    [InlineData(3, 2, 2)]
    [InlineData(3, 0, 0)] // a zero cap is a cap
    [InlineData(0, 0, 0)]
    public void ScoreCap_HonoursEveryCeiling(int count, int? max, int expected)
    {
        var clause = new JokerClause { Jokers = [MotelyJoker.Blueprint], Antes = [1], Min = 1, Max = max };
        Assert.Equal(expected, JamlScoring.CapScoreCountForTesting(count, clause));
    }
}
