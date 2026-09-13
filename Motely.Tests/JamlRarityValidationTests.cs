using Motely.Filters;
using Motely.Filters.Jaml;
using Xunit.Abstractions;

namespace Motely.Tests;

/// <summary>
/// The oracle. <see cref="JamlPoolRarityTests"/> pins what the analytic model is supposed to say;
/// this runs the real engine over a slice of the seed space and checks the model said what the
/// engine does. Every row is a clause common enough to measure in a short slice, and the bar is
/// deliberately loose — a factor of 1.5 either way — because the job is to catch a wrong pool
/// size, a missed ante-1 rule or a mis-stated rate, not to certify the third decimal. The
/// modelled-impossible rows are held to the letter: if the model says zero, the engine must find
/// none.
/// </summary>
public sealed class JamlRarityValidationTests
{
    /// <summary>
    /// Seeds per row: three 35³ batches — enough that anything above <see cref="MinMeasurable"/>
    /// yields hundreds of hits, small enough that the scalar-confirm families keep the whole table
    /// under a minute. (A 35⁴ batch per row measured the same numbers and took four and a half.)
    /// </summary>
    private const long Seeds = 3 * 42_875;

    /// <summary>35³ seeds per batch; the slice above is whole batches of this size.</summary>
    private const int BatchCharCount = 3;

    /// <summary>Measured may sit within this factor of analytic, either way.</summary>
    private const double Ratio = 1.5;

    /// <summary>Below this the slice is too short to judge; such rows are reported, not asserted.</summary>
    private const double MinMeasurable = 0.003;

    private static readonly JamlRarityContext RedWhite = JamlRarityContext.Default;

    private readonly record struct Row(string Name, IJamlClause Clause);

    private static IEnumerable<Row> Rows()
    {
        // jokers — shop, rarity poll, pool, edition, buffoon packs, the soul path
        yield return new("joker: Joker (shop)", new JokerClause { Jokers = [MotelyJoker.Joker], Antes = [1] });
        yield return new("uncommonJoker: any", new UncommonJokerClause { Antes = [1] });
        yield return new("rareJoker: any", new RareJokerClause { Antes = [1] });
        yield return new("joker: any, foil", new JokerClause { Antes = [1], Edition = MotelyItemEdition.Foil });
        yield return new("joker: any in packs 1-3", new JokerClause { Antes = [1], Sources = new() { BoosterPacks = [1, 2, 3] } });
        yield return new("legendaryJoker: any", new LegendaryJokerClause { Antes = [1] });

        // consumables — shop weights and pack cards
        yield return new("tarotCard: TheFool", new TarotCardClause { Tarots = [MotelyTarotCard.TheFool], Antes = [1] });
        yield return new("tarotCard: any", new TarotCardClause { Antes = [1] });
        yield return new("planetCard: Mercury", new PlanetCardClause { Planets = [MotelyPlanetCard.Mercury], Antes = [1] });
        yield return new("spectralCard: Familiar (Red, default sources)", new SpectralCardClause { Spectrals = [MotelySpectralCard.Familiar], Antes = [1] });
        yield return new("spectralCard: TheSoul", new SpectralCardClause { Spectrals = [MotelySpectralCard.TheSoul], Antes = [1] });
        yield return new("standardCard: Two (default sources)", new StandardCardClause { Rank = MotelyStandardcardRank.Two, Antes = [1] });
        yield return new(
            "standardCard: Two in packs 1-3",
            new StandardCardClause { Rank = MotelyStandardcardRank.Two, Antes = [1], Sources = new() { BoosterPacks = [1, 2, 3] } }
        );

        // ante features — pools, ante-1 rules, depletion
        yield return new("tag: RareTag ante 1", new TagClause { Tags = [MotelyTag.RareTag], Antes = [1], Rolls = [0] });
        yield return new("tag: NegativeTag ante 1", new TagClause { Tags = [MotelyTag.NegativeTag], Antes = [1], Rolls = [0] });
        yield return new("tag: NegativeTag ante 2", new TagClause { Tags = [MotelyTag.NegativeTag], Antes = [2], Rolls = [0] });
        yield return new("voucher: Overstock ante 1", new VoucherClause { Vouchers = [MotelyVoucher.Overstock], Antes = [1], Rolls = [0] });
        yield return new("voucher: OverstockPlus ante 2", new VoucherClause { Vouchers = [MotelyVoucher.OverstockPlus], Antes = [2], Rolls = [0] });
        yield return new("boss: TheClub ante 1", new BossClause { Bosses = [MotelyBossBlind.TheClub], Antes = [1] });
        yield return new("boss: TheWall ante 2", new BossClause { Bosses = [MotelyBossBlind.TheWall], Antes = [2] });
        yield return new("boss: CeruleanBell ante 1", new BossClause { Bosses = [MotelyBossBlind.CeruleanBell], Antes = [1] });
        yield return new("boosterPack: Arcana ante 1 slot 1", new BoosterPackClause { Packs = [MotelyBoosterPack.Arcana], Antes = [1], Rolls = [1] });
        yield return new("boosterPack: Buffoon ante 1 slot 0", new BoosterPackClause { Packs = [MotelyBoosterPack.Buffoon], Antes = [1], Rolls = [0] });

        // the erratic deck and the starting hand
        yield return new("erraticRank: Two", new ErraticRankClause { Rank = MotelyStandardcardRank.Two, Antes = [1] });
        yield return new("erraticSuit: Spades min 15", new ErraticSuitClause { Suit = MotelyStandardcardSuit.Spades, Antes = [1], Min = 15 });
        yield return new("startingDraw: Two ante 1", new StartingDrawClause { Rank = MotelyStandardcardRank.Two, Antes = [1] });
    }


    /// <summary>The same sequential slice harness the sweep uses: one must-clause, Red/White, one thread.</summary>
    private static (long Hits, long Searched) Measure(IJamlClause clause)
    {
        var config = new JamlConfig
        {
            Id = "rarity-validation",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Must.Add(clause);

        var (startBatch, endBatchExclusive) = SeedMath.SearchIndexRangeToBatchRange(0, Seeds - 1, BatchCharCount);

        long hits = 0;
        var settings = JamlSearchBuilder
            .CreateSettings(config)
            .WithSequentialSearch()
            .WithBatchCharacterCount(BatchCharCount)
            .WithStartBatchIndex(startBatch)
            .WithEndBatchIndex(endBatchExclusive)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithSeedMatchCallback(_ => Interlocked.Increment(ref hits));

        using var search = settings.Start();
        search.AwaitCompletion();
        return (hits, search.TotalSeedsSearched);
    }
}
