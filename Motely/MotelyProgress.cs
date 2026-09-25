namespace Motely;

public sealed record MotelyProgress
{
    public long SeedsSearched { get; set; }
    public long MatchingSeeds { get; set; }
    public double SeedsPerMillisecond { get; set; }
    public double PercentComplete { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public long? EstimatedTimeRemainingMilliseconds { get; set; }
}
