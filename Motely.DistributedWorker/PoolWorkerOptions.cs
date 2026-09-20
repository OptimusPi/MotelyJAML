namespace Motely.DistributedWorker;

/// <summary>Configuration for the in-process pool worker (when API runs the worker).</summary>
public sealed class PoolWorkerOptions
{
    public const string SectionName = "Pool";

    public string Url { get; set; } = "";
    public int Threads { get; set; } = Environment.ProcessorCount;
    public string WorkerId { get; set; } = "";

    /// <summary>
    /// Seed lake data root (Motely.DataLake).
    /// Default: Seeds in the working directory. Empty disables local saving.
    /// </summary>
    public string LocalDbPath { get; set; } = "Seeds";

    /// <summary>
    /// Optional: only claim blocks for this specific filter ID.
    /// If empty, claim from whatever active session needs help most.
    /// </summary>
    public string FilterId { get; set; } = "";
}
