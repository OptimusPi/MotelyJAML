using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Motely;

public struct MotelySingleSpectralStream(
    string resampleKey,
    MotelySingleResampleStream resampleStream,
    MotelySinglePrngStream blackHoleStream
)
{
    public readonly bool IsNull => ResampleKey == null;
    public readonly string ResampleKey = resampleKey;
    public MotelySingleResampleStream ResampleStream = resampleStream;
    public MotelySinglePrngStream SoulBlackHolePrngStream = blackHoleStream;
    public readonly bool IsSoulBlackHoleable => !SoulBlackHolePrngStream.IsInvalid;
}

public partial class MotelySingleSearchContext
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MotelySingleSpectralStream CreateSpectralStream(
        string source,
        int ante,
        bool searchSpectral,
        bool soulBlackHoleable,
        bool isCached
    )
    {
        return new(
            MotelyPrngKeys.Spectral + source + ante,
            searchSpectral
                ? CreateResampleStream(MotelyPrngKeys.Spectral + source + ante, isCached)
                : MotelySingleResampleStream.Invalid,
            soulBlackHoleable
                ? CreatePrngStream(
                    MotelyPrngKeys.SpectralSoulBlackHole + MotelyPrngKeys.Spectral + ante,
                    isCached
                )
                : MotelySinglePrngStream.Invalid
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelySingleSpectralStream CreateSpectralPackSpectralStream(
        int ante,
        bool soulOnly = false,
        bool isCached = false
    ) =>
        CreateSpectralStream(
            MotelyPrngKeys.SpectralPackItemSource,
            ante,
            !soulOnly,
            true,
            isCached
        );

    /// <summary>
    /// Spectral cards generated inside Arcana packs under Omen Globe (create_card type Spectral
    /// in Arcana pack area). Uses Arcana pack item source so keys stay distinct from Spectral packs.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelySingleSpectralStream CreateArcanaOmenSpectralStream(
        int ante,
        bool isCached = false
    ) =>
        CreateSpectralStream(
            MotelyPrngKeys.ArcanaPackItemSource,
            ante,
            searchSpectral: true,
            soulBlackHoleable: true,
            isCached
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelySingleSpectralStream CreateShopSpectralStream(int ante, bool isCached = false) =>
        CreateSpectralStream(MotelyPrngKeys.ShopItemSource, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelySingleSpectralStream CreateSixthSenseSpectralStream(
        int ante,
        bool isCached = false
    ) => CreateSpectralStream(MotelyPrngKeys.JokerSixthSense, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelySingleSpectralStream CreateSeanceSpectralStream(int ante, bool isCached = false) =>
        CreateSpectralStream(MotelyPrngKeys.JokerSeance, ante, true, false, isCached);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool GetNextSpectralPackHasTheSoul(
        ref MotelySingleSpectralStream spectralStream,
        MotelyBoosterPackSize size
    )
    {
        Debug.Assert(spectralStream.IsSoulBlackHoleable, "Spectral pack does not have the soul.");

        int cardCount = MotelyBoosterPackType.Spectral.GetCardCount(size);

        // Balatro's create_card rolls the Soul check, then the Black Hole check, on the same
        // soul_Spectral{ante} stream, and the Black Hole check overwrites forced_key: a card that
        // rolls both is a Black Hole, not a Soul (Immolate's next_spectral agrees). Once the pack
        // holds a Soul or a Black Hole, that check is skipped for the rest of the pack
        // (used_jokers) and pulls no roll. Every card is walked so the stream ends where the game
        // leaves it for the ante's next Spectral pack.
        bool hasSoul = false;
        bool hasBlackHole = false;

        for (int i = 0; i < cardCount; i++)
        {
            bool soul =
                !hasSoul && GetNextRandom(ref spectralStream.SoulBlackHolePrngStream) > 0.997;
            bool blackHole =
                !hasBlackHole
                && GetNextRandom(ref spectralStream.SoulBlackHolePrngStream) > 0.997;

            if (blackHole)
                hasBlackHole = true;
            else if (soul)
                hasSoul = true;
        }

        return hasSoul;
    }

    public MotelySingleItemSet GetNextSpectralPackContents(
        ref MotelySingleSpectralStream spectralStream,
        MotelyBoosterPackSize size
    ) =>
        GetNextSpectralPackContents(
            ref spectralStream,
            MotelyBoosterPackType.Spectral.GetCardCount(size)
        );

    public MotelySingleItemSet GetNextSpectralPackContents(
        ref MotelySingleSpectralStream spectralStream,
        int size
    )
    {
        Debug.Assert(size <= MotelySingleItemSet.MaxLength);

        MotelySingleItemSet pack = new();

        for (int i = 0; i < size; i++)
            pack.Append(GetNextSpectral(ref spectralStream, pack));

        return pack;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MotelyItem GetNextSpectral(ref MotelySingleSpectralStream spectralStream)
    {
        if (spectralStream.IsSoulBlackHoleable)
        {
            // create_card: both checks roll; the Black Hole check runs second and wins.
            bool soul = GetNextRandom(ref spectralStream.SoulBlackHolePrngStream) > 0.997;
            bool blackHole = GetNextRandom(ref spectralStream.SoulBlackHolePrngStream) > 0.997;

            if (blackHole)
                return MotelyItemType.BlackHole;
            if (soul)
                return MotelyItemType.TheSoul;
        }

        MotelyItemType Spectral =
            (MotelyItemType)MotelyItemTypeCategory.SpectralCard
            | (MotelyItemType)GetNextRandomInt(
                ref spectralStream.ResampleStream.InitialPrngStream,
                0,
                MotelyEnum<MotelySpectralCard>.ValueCount
            );

        int resampleCount = 0;

        while (true)
        {
            if (Spectral != MotelyItemType.TheSoul && Spectral != MotelyItemType.BlackHole)
            {
                return Spectral;
            }

            Spectral =
                (MotelyItemType)MotelyItemTypeCategory.SpectralCard
                | (MotelyItemType)GetNextRandomInt(
                    ref GetResamplePrngStream(
                        ref spectralStream.ResampleStream,
                        spectralStream.ResampleKey,
                        resampleCount
                    ),
                    0,
                    MotelyEnum<MotelySpectralCard>.ValueCount
                );

            ++resampleCount;
        }
    }

    public MotelyItem GetNextSpectral(
        ref MotelySingleSpectralStream spectralStream,
        in MotelySingleItemSet itemSet
    )
    {
        if (spectralStream.IsSoulBlackHoleable)
        {
            // create_card rolls the Soul check, then the Black Hole check, and the Black Hole
            // check overwrites forced_key, so a card that rolls both is a Black Hole. A Soul does
            // not skip the Black Hole roll. A check for a card the pack already holds is skipped
            // (used_jokers) and pulls no roll.
            bool soul =
                !itemSet.Contains(MotelyItemType.TheSoul)
                && GetNextRandom(ref spectralStream.SoulBlackHolePrngStream) > 0.997;
            bool blackHole =
                !itemSet.Contains(MotelyItemType.BlackHole)
                && GetNextRandom(ref spectralStream.SoulBlackHolePrngStream) > 0.997;

            if (blackHole)
                return MotelyItemType.BlackHole;
            if (soul)
                return MotelyItemType.TheSoul;
        }

        MotelyItemType Spectral =
            (MotelyItemType)MotelyItemTypeCategory.SpectralCard
            | (MotelyItemType)GetNextRandomInt(
                ref spectralStream.ResampleStream.InitialPrngStream,
                0,
                MotelyEnum<MotelySpectralCard>.ValueCount
            );

        int resampleCount = 0;

        while (true)
        {
            if (
                !itemSet.Contains(Spectral)
                && Spectral != MotelyItemType.TheSoul
                && Spectral != MotelyItemType.BlackHole
            )
            {
                return Spectral;
            }

            Spectral =
                (MotelyItemType)MotelyItemTypeCategory.SpectralCard
                | (MotelyItemType)GetNextRandomInt(
                    ref GetResamplePrngStream(
                        ref spectralStream.ResampleStream,
                        spectralStream.ResampleKey,
                        resampleCount
                    ),
                    0,
                    MotelyEnum<MotelySpectralCard>.ValueCount
                );

            ++resampleCount;
        }
    }
}
