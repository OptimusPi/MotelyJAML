using Motely.Filters;
using Motely.Filters.Jaml;

namespace Motely.Tests;

/// <summary>
/// The four joker SIMD descs, run raw (no scalar must re-eval behind them), must accept exactly
/// the seeds <see cref="JamlScoring.ClauseMeetsMinForFilter"/> accepts: a sticker list is ALL-of,
/// <c>None</c> is not a gate, and a clause naming a source the desc does not walk in SIMD is
/// confirmed per seed instead of silently counting zero.
/// </summary>
public sealed class JokerDescScalarAgreementTests
{
    private static readonly string[] WideSeeds =
    [
        "ALEEB", "MOTELY77", "UNITTEST", "5X5", "616", "696", "6J6", "7H7",
        "99", "CC", "F", "Q", "R", "VV", "H", "I", "Z", "88", "AAAAAAAA", "MOTELY",
        "474", "3X3", "GHG", "4C4", "2A2", "111", "CUC", "FMF",
    ];

    // Enough common names that judgement rolls and Gold-stake sticker pairs land on the list.
    private static readonly MotelyJoker[] SomeCommons =
    [
        MotelyJoker.Joker, MotelyJoker.GreedyJoker, MotelyJoker.LustyJoker,
        MotelyJoker.WrathfulJoker, MotelyJoker.GluttonousJoker, MotelyJoker.JollyJoker,
        MotelyJoker.ZanyJoker, MotelyJoker.MadJoker, MotelyJoker.CrazyJoker,
        MotelyJoker.DrollJoker, MotelyJoker.SlyJoker, MotelyJoker.WilyJoker,
        MotelyJoker.CleverJoker, MotelyJoker.DeviousJoker, MotelyJoker.CraftyJoker,
        MotelyJoker.HalfJoker, MotelyJoker.Banner, MotelyJoker.MysticSummit,
    ];

    /// <summary>Scalar law for the same clause, one seed at a time.</summary>
    private sealed class ScalarProbeDesc(IJamlClause clause)
        : IMotelySeedFilterDesc<ScalarProbeDesc.ScalarProbeFilter>
    {
        public ScalarProbeFilter CreateFilter(ref MotelyFilterCreationContext ctx) => new(clause);

        public readonly struct ScalarProbeFilter(IJamlClause clause) : IMotelySeedFilter
        {
            private readonly IJamlClause _clause = clause;

            public VectorMask Filter(ref MotelyVectorSearchContext ctx)
            {
                var c = _clause;
                return ctx.SearchIndividualSeeds(
                    (MotelySingleSearchContext single) =>
                        JamlScoring.ClauseMeetsMinForFilter(ref single, c) ? 1 : 0
                );
            }
        }
    }

    private static string[] Run<TFilter>(IMotelySeedFilterDesc<TFilter> desc, MotelyStake stake)
        where TFilter : struct, IMotelySeedFilter
    {
        var matched = new List<string>();
        using var search = new MotelySearchSettings<TFilter>(desc)
            .WithDeck(MotelyDeck.Red)
            .WithStake(stake)
            .WithSeedGenerator(WideSeeds, WideSeeds.Length)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(s => { lock (matched) matched.Add(s); })
            .Start();
        search.AwaitCompletion();
        return [.. matched.OrderBy(s => s, StringComparer.Ordinal)];
    }

    private static string[] Scalar(IJamlClause clause, MotelyStake stake) =>
        Run(new ScalarProbeDesc(clause), stake);

    private static string[] Simd(JokerClause c, MotelyStake stake) =>
        Run(new JokerFilterDesc(c), stake);

    private static string[] Simd(CommonJokerClause c, MotelyStake stake) =>
        Run(new CommonJokerFilterDesc(c), stake);

    private static string[] Simd(UncommonJokerClause c, MotelyStake stake) =>
        Run(new UncommonJokerFilterDesc(c), stake);

    private static string[] Simd(RareJokerClause c, MotelyStake stake) =>
        Run(new RareJokerFilterDesc(c), stake);

    private static void AssertAgreeNonEmpty(string[] simd, string[] scalar)
    {
        Assert.NotEmpty(scalar);
        Assert.Equal(scalar, simd);
    }

    // ── Stickers ──
    // Eternal/Perishable roll from Black stake, Rental from Gold
    // (MotelySingleSearchContext.Jokers.cs ApplyNextStickers), so Gold exercises every arm.

    [Fact]
    public void Joker_SingleEternal_Gold_AgreesWithScalar()
    {
        var clause = new JokerClause
        {
            Jokers = SomeCommons,
            Stickers = [MotelyJokerSticker.Eternal],
            Antes = [1, 2, 3],
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.Gold), Scalar(clause, MotelyStake.Gold));
    }

    /// <summary>
    /// Two stickers is ALL-of. The ANY-of union is provably wider on this list, so a desc that
    /// ORs the sticker masks cannot pass.
    /// </summary>
    [Fact]
    public void Joker_EternalAndRental_Gold_IsAllOfNotAnyOf()
    {
        JokerClause With(params MotelyJokerSticker[] stickers) => new()
        {
            Jokers = SomeCommons,
            Stickers = stickers,
            Antes = [1, 2, 3, 4],
        };

        var both = With(MotelyJokerSticker.Eternal, MotelyJokerSticker.Rental);
        var scalarBoth = Scalar(both, MotelyStake.Gold);
        var anyOf = Scalar(With(MotelyJokerSticker.Eternal), MotelyStake.Gold)
            .Union(Scalar(With(MotelyJokerSticker.Rental), MotelyStake.Gold))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.True(anyOf.Length > scalarBoth.Length, "fixture must separate ALL-of from ANY-of");
        Assert.Equal(scalarBoth, Simd(both, MotelyStake.Gold));
    }

    [Fact]
    public void CommonWildcard_EternalAndRental_Gold_IsAllOfNotAnyOf()
    {
        CommonJokerClause With(params MotelyJokerSticker[] stickers) => new()
        {
            Stickers = stickers,
            Antes = [1, 2],
        };

        var both = With(MotelyJokerSticker.Eternal, MotelyJokerSticker.Rental);
        var scalarBoth = Scalar(both, MotelyStake.Gold);
        var anyOf = Scalar(With(MotelyJokerSticker.Eternal), MotelyStake.Gold)
            .Union(Scalar(With(MotelyJokerSticker.Rental), MotelyStake.Gold))
            .ToArray();

        Assert.True(anyOf.Length > scalarBoth.Length, "fixture must separate ALL-of from ANY-of");
        Assert.NotEmpty(scalarBoth);
        Assert.Equal(scalarBoth, Simd(both, MotelyStake.Gold));
    }

    [Fact]
    public void UncommonWildcard_EternalAndRental_Gold_IsAllOfNotAnyOf()
    {
        UncommonJokerClause With(params MotelyJokerSticker[] stickers) => new()
        {
            Stickers = stickers,
            Antes = [1, 2, 3, 4],
        };

        var both = With(MotelyJokerSticker.Eternal, MotelyJokerSticker.Rental);
        var scalarBoth = Scalar(both, MotelyStake.Gold);
        var anyOf = Scalar(With(MotelyJokerSticker.Eternal), MotelyStake.Gold)
            .Union(Scalar(With(MotelyJokerSticker.Rental), MotelyStake.Gold))
            .ToArray();

        Assert.True(anyOf.Length > scalarBoth.Length, "fixture must separate ALL-of from ANY-of");
        Assert.Equal(scalarBoth, Simd(both, MotelyStake.Gold));
    }

    [Fact]
    public void RareWildcard_EternalAndRental_Gold_IsAllOfNotAnyOf()
    {
        RareJokerClause With(params MotelyJokerSticker[] stickers) => new()
        {
            Stickers = stickers,
            Antes = [1, 2, 3, 4],
        };

        var both = With(MotelyJokerSticker.Eternal, MotelyJokerSticker.Rental);
        var scalarBoth = Scalar(both, MotelyStake.Gold);
        var anyOf = Scalar(With(MotelyJokerSticker.Eternal), MotelyStake.Gold)
            .Union(Scalar(With(MotelyJokerSticker.Rental), MotelyStake.Gold))
            .ToArray();

        Assert.True(anyOf.Length > scalarBoth.Length, "fixture must separate ALL-of from ANY-of");
        Assert.Equal(scalarBoth, Simd(both, MotelyStake.Gold));
    }

    /// <summary>Scalar MatchJoker treats <c>None</c> as always satisfied; so must the SIMD masks.</summary>
    [Fact]
    public void StickerNone_IsNoGate_AllFourDescs()
    {
        var joker = new JokerClause { Jokers = SomeCommons, Antes = [1, 2] };
        var jokerNone = new JokerClause
        {
            Jokers = SomeCommons,
            Antes = [1, 2],
            Stickers = [MotelyJokerSticker.None],
        };
        var plain = Simd(joker, MotelyStake.Gold);
        Assert.NotEmpty(plain);
        Assert.Equal(plain, Simd(jokerNone, MotelyStake.Gold));
        Assert.Equal(plain, Scalar(jokerNone, MotelyStake.Gold));

        var common = new CommonJokerClause { Antes = [1], Stickers = [MotelyJokerSticker.None] };
        AssertAgreeNonEmpty(Simd(common, MotelyStake.Gold), Scalar(common, MotelyStake.Gold));

        var uncommon = new UncommonJokerClause
        {
            Antes = [1, 2],
            Stickers = [MotelyJokerSticker.None],
        };
        AssertAgreeNonEmpty(Simd(uncommon, MotelyStake.Gold), Scalar(uncommon, MotelyStake.Gold));

        var rare = new RareJokerClause
        {
            Antes = [1, 2, 3, 4],
            Stickers = [MotelyJokerSticker.None],
        };
        AssertAgreeNonEmpty(Simd(rare, MotelyStake.Gold), Scalar(rare, MotelyStake.Gold));
    }

    // ── Sources the SIMD walk does not cover ──

    [Fact]
    public void Joker_JudgementSource_ConfirmsPerSeed()
    {
        var clause = new JokerClause
        {
            Jokers = SomeCommons,
            Antes = [1, 2],
            Sources = new JokerSourceConfig { Judgement = [0, 1] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    [Fact]
    public void Joker_RawShopJokerSources_ConfirmPerSeed()
    {
        var clause = new JokerClause
        {
            Jokers = SomeCommons,
            Antes = [1, 2],
            Sources = new JokerSourceConfig { CommonShopJokers = [0, 1], AllShopJokers = [0] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    [Fact]
    public void CommonJoker_JudgementSource_ConfirmsPerSeed()
    {
        var clause = new CommonJokerClause
        {
            Antes = [1],
            Sources = new JokerSourceConfig { Judgement = [0] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    [Fact]
    public void CommonJoker_RiffRaffSource_ConfirmsPerSeed()
    {
        var clause = new CommonJokerClause
        {
            Jokers = [MotelyJokerCommon.Joker, MotelyJokerCommon.GreedyJoker, MotelyJokerCommon.LustyJoker,
                MotelyJokerCommon.WrathfulJoker, MotelyJokerCommon.GluttonousJoker, MotelyJokerCommon.JollyJoker],
            Antes = [1, 2],
            Sources = new JokerSourceConfig { RiffRaff = [0, 1] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    [Fact]
    public void UncommonJoker_WraithSource_ConfirmsPerSeed()
    {
        var clause = new UncommonJokerClause
        {
            Antes = [1, 2],
            Sources = new JokerSourceConfig { Wraith = [0, 1] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    /// <summary>Uncommon keeps its native raw shop joker walks; they must still match scalar.</summary>
    [Fact]
    public void UncommonJoker_RawShopJokerSources_StillAgree()
    {
        var clause = new UncommonJokerClause
        {
            Antes = [1],
            Sources = new JokerSourceConfig { UncommonShopJokers = [0], AllShopJokers = [0, 1] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    [Fact]
    public void RareJoker_RareTagSource_ConfirmsPerSeed()
    {
        var clause = new RareJokerClause
        {
            Antes = [1],
            Sources = new JokerSourceConfig { RareTag = [0] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    [Fact]
    public void RareJoker_RareShopJokersSource_ConfirmsPerSeed()
    {
        var clause = new RareJokerClause
        {
            Antes = [1],
            Sources = new JokerSourceConfig { RareShopJokers = [0] },
        };
        AssertAgreeNonEmpty(Simd(clause, MotelyStake.White), Scalar(clause, MotelyStake.White));
    }

    /// <summary>End to end through the JAML pipeline: a must clause on a spawn source finds seeds.</summary>
    [Fact]
    public void Jaml_MustJudgementSource_FindsSeeds()
    {
        var (matching, matched) = ProofSearch.ListMatch(
            """
            name: joker-desc-judgement
            deck: Red
            stake: White
            must:
              - commonJoker: []
                antes: [1]
                sources:
                  judgement: [0]
            """,
            WideSeeds
        );
        var scalar = Scalar(
            new CommonJokerClause { Antes = [1], Sources = new JokerSourceConfig { Judgement = [0] } },
            MotelyStake.White
        );
        Assert.True(matching > 0);
        Assert.Equal(scalar, matched.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }
}
