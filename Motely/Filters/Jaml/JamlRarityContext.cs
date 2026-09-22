namespace Motely.Filters.Jaml;

/// <summary>
/// What a rarity model needs to know about the run that it cannot read off the clause: the deck
/// and the stake. Pool sizes and rates shift with both — Ghost adds spectrals to the shop, Zodiac
/// starts with the merchant vouchers, stickers only exist from Black stake up — so a family's odds
/// are a function of (clause, context), never of the clause alone.
/// <para>
/// Built once per estimate from the <see cref="JamlConfig"/> the search will actually run with, so
/// the model and the engine read the same deck and stake by construction.
/// </para>
/// </summary>
/// <param name="Deck">The deck the search runs on.</param>
/// <param name="Stake">The stake the search runs on.</param>
public readonly record struct JamlRarityContext(MotelyDeck Deck, MotelyStake Stake)
{
    /// <summary>Red deck, White stake — the engine's own defaults and the tests' baseline.</summary>
    public static readonly JamlRarityContext Default = new(MotelyDeck.Red, MotelyStake.White);

    /// <summary>The context a config's search will run under.</summary>
    public static JamlRarityContext From(JamlConfig config) => new(config.Deck, config.Stake);

    // ── shop item-type rates ───────────────────────────────────────────────────────────────────
    //
    // These mirror MotelySingleSearchContext.CreateShopItemStream(ante) exactly: the scoring paths
    // build every shop stream from Deck.GetDefaultRunState(), so the only run state a shop rate
    // ever sees is the deck's starting vouchers. Rather than hard-code "Zodiac → 9.6", the same
    // voucher checks are asked of the same run state, so a new deck or a changed starting voucher
    // flows through without anyone remembering this file exists.

    /// <summary>The shop's joker weight, fixed for every deck.</summary>
    public double ShopJokerRate => MotelySingleSearchContext.ShopJokerRate;

    /// <summary>The shop's tarot weight under the deck's starting vouchers.</summary>
    public double ShopTarotRate
    {
        get
        {
            var state = Deck.GetDefaultRunState();
            return state.IsVoucherActive(MotelyVoucher.TarotTycoon) ? 32
                : state.IsVoucherActive(MotelyVoucher.TarotMerchant) ? 9.6
                : 4;
        }
    }

    /// <summary>The shop's planet weight under the deck's starting vouchers.</summary>
    public double ShopPlanetRate
    {
        get
        {
            var state = Deck.GetDefaultRunState();
            return state.IsVoucherActive(MotelyVoucher.PlanetTycoon) ? 32
                : state.IsVoucherActive(MotelyVoucher.PlanetMerchant) ? 9.6
                : 4;
        }
    }

    /// <summary>
    /// The shop's playing-card weight. Zero unless Magic Trick is active, and no deck starts with
    /// it — so on the engine's scoring path a shop slot never yields a playing card. That is the
    /// engine's behaviour, not a modelling shortcut; see <c>CreateShopItemStream</c>.
    /// </summary>
    public double ShopStandardCardRate =>
        Deck.GetDefaultRunState().IsVoucherActive(MotelyVoucher.MagicTrick) ? 4 : 0;

    /// <summary>The shop's spectral weight: only the Ghost deck offers them.</summary>
    public double ShopSpectralRate => Deck == MotelyDeck.Ghost ? 2 : 0;

    /// <summary>The sum every shop item-type poll is scaled by.</summary>
    public double ShopTotalRate =>
        ShopJokerRate + ShopTarotRate + ShopPlanetRate + ShopStandardCardRate + ShopSpectralRate;
}
