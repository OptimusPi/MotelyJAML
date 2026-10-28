using Motely.SeedProviders;

namespace Motely.Lake;

public enum LakeCategory
{
    Ranks,
    Suits,
    CuratedRanks,
    CuratedSuits,
    NegativeUncommon,
    NegativeRare,
    TripleNegative,
}

public sealed record LakeMetadata(
    string Name,
    string Path,
    LakeCategory Category,
    string Partition,
    long EstimatedSeedCount,
    bool IsParquet
);

public interface ISeedLakeService
{
    IReadOnlyList<LakeMetadata> GetAvailableLakes();
    IEnumerable<string> ReadSeeds(LakeCategory category, string partition, int? limit = null);
    IMotelySeedProvider CreateSeedProvider(LakeCategory category, string partition, int? limit = null);
}
