using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Motely;

public ref struct MotelyVectorShopItemStream
{
    // Keep existing streams
    public MotelyVectorPrngStream ItemTypeStream;
    public MotelyVectorJokerStream JokerStream;
    public MotelyVectorTarotStream TarotStream;
    public MotelyVectorPlanetStream PlanetStream;
    public MotelyVectorSpectralStream SpectralStream;

    // Rates
    public Vector512<double> TarotRate;
    public Vector512<double> PlanetRate;
    public Vector512<double> PlayingCardRate;
    public Vector512<double> SpectralRate;
    public Vector512<double> TotalRate;

    public readonly bool DoesProvideJokers => !JokerStream.IsNull;
    public readonly bool DoesProvideTarots => !TarotStream.IsNull;
    public readonly bool DoesProvidePlanets => !PlanetStream.IsNull;
    public readonly bool DoesProvideSpectrals => !SpectralStream.IsNull;
}

ref partial struct MotelyVectorSearchContext
{
    private const int ShopJokerRate = 20;

    public MotelyVectorShopItemStream CreateShopItemStream(
        int ante,
        MotelyShopStreamFlags flags = MotelyShopStreamFlags.Default,
        MotelyJokerStreamFlags jokerFlags = MotelyJokerStreamFlags.Default,
        bool isCached = false
    )
    {
        return CreateShopItemStream(ante, Deck.GetDefaultRunState(), flags, jokerFlags, isCached);
    }

    public MotelyVectorShopItemStream CreateShopItemStream(
        int ante,
        MotelyRunState runState,
        MotelyShopStreamFlags flags = MotelyShopStreamFlags.Default,
        MotelyJokerStreamFlags jokerFlags = MotelyJokerStreamFlags.Default,
        bool isCached = false
    )
    {
        MotelyVectorShopItemStream stream = new()
        {
            ItemTypeStream = CreatePrngStream(MotelyPrngKeys.ShopItemType + ante, isCached),
            JokerStream = CreateShopJokerStream(ante, jokerFlags, isCached),
            TarotStream = CreateShopTarotStream(ante, isCached),
            PlanetStream = CreateShopPlanetStream(ante, isCached),
            SpectralStream = CreateShopSpectralStream(ante, isCached),

            TarotRate = Vector512.Create(4.0),
            PlanetRate = Vector512.Create(4.0),
            PlayingCardRate = Vector512.Create(0.0),
            SpectralRate = Deck == MotelyDeck.Ghost ? Vector512.Create(2.0) : Vector512.Create(0.0),
        };

        if (runState.IsVoucherActive(MotelyVoucher.TarotTycoon))
            stream.TarotRate = Vector512.Create(32.0);
        else if (runState.IsVoucherActive(MotelyVoucher.TarotMerchant))
            stream.TarotRate = Vector512.Create(9.6);

        if (runState.IsVoucherActive(MotelyVoucher.PlanetTycoon))
            stream.PlanetRate = Vector512.Create(32.0);
        else if (runState.IsVoucherActive(MotelyVoucher.PlanetMerchant))
            stream.PlanetRate = Vector512.Create(9.6);

        if (runState.IsVoucherActive(MotelyVoucher.MagicTrick))
            stream.PlayingCardRate = Vector512.Create(4.0);

        stream.TotalRate =
            Vector512.Create((double)ShopJokerRate)
            + stream.TarotRate
            + stream.PlanetRate
            + stream.PlayingCardRate
            + stream.SpectralRate;

        return stream;
    }

    // Fixed GetNextShopItem that maintains position sync
    public MotelyItemVector GetNextShopItem(ref MotelyVectorShopItemStream stream)
    {
        // Get slot type (ALL lanes advance together - this is correct!)
        var itemTypePoll = GetNextRandom(ref stream.ItemTypeStream) * stream.TotalRate;

        // Determine what type each lane needs
        var shopJokerRate = Vector512.Create(20.0);
        var isJoker = Vector512.LessThan(itemTypePoll, shopJokerRate);
        itemTypePoll -= shopJokerRate;

        var isTarot = Vector512.AndNot(Vector512.LessThan(itemTypePoll, stream.TarotRate), isJoker);
        itemTypePoll -= stream.TarotRate;

        var isPlanet = Vector512.AndNot(
            Vector512.AndNot(Vector512.LessThan(itemTypePoll, stream.PlanetRate), isJoker),
            isTarot
        );
        itemTypePoll -= stream.PlanetRate;

        var isPlayingCard = Vector512.AndNot(
            Vector512.AndNot(
                Vector512.AndNot(Vector512.LessThan(itemTypePoll, stream.PlayingCardRate), isJoker),
                isTarot
            ),
            isPlanet
        );
        itemTypePoll -= stream.PlayingCardRate;

        var isSpectral = Vector512.AndNot(
            Vector512.AndNot(
                Vector512.AndNot(
                    Vector512.AndNot(
                        Vector512.LessThan(itemTypePoll, stream.SpectralRate),
                        isJoker
                    ),
                    isTarot
                ),
                isPlanet
            ),
            isPlayingCard
        );

        // Get items ONLY for lanes that need them (masked advancement)
        var joker = stream.DoesProvideJokers
            ? GetNextJoker(ref stream.JokerStream, isJoker)
            : new MotelyItemVector(new MotelyItem(MotelyItemType.JokerExcludedByStream));

        var tarot = stream.DoesProvideTarots
            ? GetNextTarot(ref stream.TarotStream, in isTarot)
            : new MotelyItemVector(new MotelyItem(MotelyItemType.TarotExcludedByStream));

        var planet = stream.DoesProvidePlanets
            ? GetNextPlanet(ref stream.PlanetStream, in isPlanet)
            : new MotelyItemVector(new MotelyItem(MotelyItemType.PlanetExcludedByStream));

        var spectral = stream.DoesProvideSpectrals
            ? GetNextSpectral(ref stream.SpectralStream, in isSpectral)
            : new MotelyItemVector(new MotelyItem(MotelyItemType.SpectralExcludedByStream));

        // Combine results based on slot type
        var jokerMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(isJoker);
        var tarotMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(isTarot);
        var planetMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(isPlanet);
        var spectralMask = MotelyVectorUtils.ShrinkDoubleMaskToInt(isSpectral);

        // Select the appropriate item for each lane
        var result = Vector256.ConditionalSelect(
            jokerMask,
            joker.Value,
            Vector256.ConditionalSelect(
                tarotMask,
                tarot.Value,
                Vector256.ConditionalSelect(
                    planetMask,
                    planet.Value,
                    Vector256.ConditionalSelect(
                        spectralMask,
                        spectral.Value,
                        Vector256.Create((int)MotelyItemType.NotImplemented)
                    )
                )
            )
        );

        return new MotelyItemVector(result);
    }
}
