namespace Motely;

public struct MotelySingleBoosterPackStream(
    MotelySinglePrngStream prngStream,
    bool generatedFirstPack
)
{
    public MotelySinglePrngStream PrngStream = prngStream;
    public bool GeneratedFirstPack = generatedFirstPack;
}

public partial class MotelySingleSearchContext
{
    // The game hands out one unrolled Buffoon the first time it ever builds a pack, which is the
    // run's first shop in ante 1. Ante 0 only exists after Hieroglyph is bought in that shop, so
    // its packs come later and are all rolled.
    public MotelySingleBoosterPackStream CreateBoosterPackStream(int ante, bool isCached = false) =>
        CreateBoosterPackStream(ante, ante != 1, isCached);

    public MotelySingleBoosterPackStream CreateBoosterPackStream(
        int ante,
        bool generatedFirstPack,
        bool isCached = false
    )
    {
        return new(CreatePrngStream(MotelyPrngKeys.ShopPack + ante, isCached), generatedFirstPack);
    }

    public MotelyBoosterPack GetNextBoosterPack(ref MotelySingleBoosterPackStream stream)
    {
        if (!stream.GeneratedFirstPack)
        {
            stream.GeneratedFirstPack = true;
            return MotelyBoosterPack.Buffoon;
        }

        return MotelyWeightedPools.BoosterPacks.Choose(GetNextRandom(ref stream.PrngStream));
    }
}
