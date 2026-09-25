using System.Collections.Generic;
using Motely.Filters;

namespace Motely.DataLake;

public sealed class JamlSeedPersistence : System.IDisposable
{
    private readonly MotelyScoreCutoff _cutoff;
    private readonly SeedLakeSink _lake;
    private readonly MotelyTopSeedSink.Collector _scoredCollector;

    public System.Action<MotelyScoredSeedResult>? OnScoredAccepted { get; set; }

    public JamlSeedPersistence(
        string? lakeRoot,
        string filterId,
        MotelyScoreCutoff? cutoff = null,
        int saveLimit = int.MaxValue,
        IReadOnlyList<string>? tallyLabels = null
    )
    {
        _cutoff = cutoff ?? MotelyScoreCutoff.Off();
        _lake = new SeedLakeSink(lakeRoot, filterId, tallyLabels);
        _scoredCollector = new MotelyTopSeedSink.Collector(saveLimit);
    }

    public MotelyScoreCutoff Cutoff => _cutoff;

    public bool OnScored(in MotelyScoredSeedResult tally)
    {
        if (!_cutoff.ShouldEmit(tally.Score))
            return false;

        _lake.OnScored(in tally);
        _scoredCollector.Consider(tally.Seed, tally.Score);
        OnScoredAccepted?.Invoke(tally);
        return true;
    }

    public IReadOnlyList<string> SeedsToSave() => _scoredCollector.GetSeeds();

    public bool SaveBack(string jamlPath, out string? error) =>
        MotelyJamlFile.TrySaveSeeds(jamlPath, SeedsToSave(), out error);

    public void Flush() => _lake.Flush();

    public void Dispose() => _lake.Dispose();
}
