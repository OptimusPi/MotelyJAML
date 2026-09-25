namespace Motely.DistributedWorker;

public sealed class PoolWorkerOptions
{
    public const string SectionName = "Pool";

    public string Url { get; set; } = "";
    public int Threads { get; set; } = Environment.ProcessorCount;
    public string WorkerId { get; set; } = "";

    public string LocalDbPath { get; set; } = "Seeds";

    public string FilterId { get; set; } = "";
}
