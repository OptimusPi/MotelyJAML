using Motely.DataLake;
using Motely.Filters;

namespace Motely.Tests;

public sealed class JamlSeedPersistenceTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("motely-persist-");
    private string LakeRoot => Path.Join(_temp.FullName, "Seeds");

    public void Dispose() => _temp.Delete(recursive: true);

    private static MotelyScoredSeedResult Result(string seed, int score)
    {
        var r = new MotelyScoredSeedResult();
        r.Reset(seed, score);
        return r;
    }

    [Fact]
    public void Auto_LakeAndUiAreTheSameRows()
    {
        var accepted = new List<(string Seed, int Score)>();
        using (var persistence = new JamlSeedPersistence(LakeRoot, "whimsy", MotelyScoreCutoff.Auto()))
        {
            persistence.OnScoredAccepted = t => accepted.Add((t.Seed, t.Score));

            Assert.True(persistence.OnScored(Result("AAAAAAAA", 1)));
            Assert.True(persistence.OnScored(Result("5X5", 5)));
            Assert.False(persistence.OnScored(Result("616", 3)));
            Assert.True(persistence.OnScored(Result("7H7", 5)));
            Assert.False(persistence.OnScored(Result("UNITTEST", 2)));
        }

        var seedFile = SeedLakeSink.SeedFilePath(LakeRoot, "whimsy");
        Assert.True(File.Exists(seedFile));
        var seeds = File.ReadAllLines(seedFile)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToHashSet();
        Assert.Equal(3, seeds.Count);
        Assert.Contains("AAAAAAAA", seeds);
        Assert.Contains("5X5", seeds);
        Assert.Contains("7H7", seeds);
        Assert.DoesNotContain("616", seeds);
        Assert.DoesNotContain("UNITTEST", seeds);

        Assert.Equal([("AAAAAAAA", 1), ("5X5", 5), ("7H7", 5)], accepted);
    }

    [Fact]
    public void Auto_SaveBackMatchesTheSeedFile()
    {
        string[] saved;
        using (var persistence = new JamlSeedPersistence(LakeRoot, "whimsy", MotelyScoreCutoff.Auto()))
        {
            persistence.OnScored(Result("AAAAAAAA", 1));
            persistence.OnScored(Result("5X5", 5));
            persistence.OnScored(Result("616", 3));
            saved = persistence.SeedsToSave().ToArray();
        }

        Assert.Equal(["5X5", "AAAAAAAA"], saved);

        var seedFile = SeedLakeSink.SeedFilePath(LakeRoot, "whimsy");
        var onDisk = File.ReadAllLines(seedFile)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal(2, onDisk.Length);
    }

    [Fact]
    public void FixedFloor_DropsBelowFloorEverywhere()
    {
        var accepted = new List<string>();
        using (var persistence = new JamlSeedPersistence(LakeRoot, "whimsy", MotelyScoreCutoff.Fixed(4)))
        {
            persistence.OnScoredAccepted = t => accepted.Add(t.Seed);
            Assert.False(persistence.OnScored(Result("616", 2)));
            Assert.True(persistence.OnScored(Result("5X5", 4)));
            Assert.True(persistence.OnScored(Result("AAAAAAAA", 9)));
        }

        var seedFile = SeedLakeSink.SeedFilePath(LakeRoot, "whimsy");
        var seeds = File.ReadAllLines(seedFile)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal(["5X5", "AAAAAAAA"], seeds);
        Assert.Equal(["5X5", "AAAAAAAA"], accepted);
    }
}
