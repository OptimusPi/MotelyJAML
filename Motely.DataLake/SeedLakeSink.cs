using Motely.Filters;

namespace Motely.DataLake;

public sealed class SeedLakeSink : IMotelyResultSink
{
    private readonly string _seedFilePath;
    private readonly object _gate = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _buffer = [];
    private StreamWriter? _writer;
    private bool _disposed;

    public static string LakeRoot(string? root)
    {
        root ??= Environment.GetEnvironmentVariable("MOTELY_DATALAKE_PATH");
        return string.IsNullOrWhiteSpace(root) ? "Seeds" : root;
    }

    public static string LakePath(string? root, string filterId) =>
        Path.Combine(LakeRoot(root), filterId + ".duckdb");

    public static string SeedFilePath(string? root, string filterId) =>
        Path.Combine(LakeRoot(root), filterId + ".txt");

    public SeedLakeSink(string? root, string filterId, IReadOnlyList<string>? tallyLabels = null, string? catalogPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterId);
        _seedFilePath = SeedFilePath(root, filterId);
        if (File.Exists(_seedFilePath))
        {
            foreach (var line in File.ReadLines(_seedFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    _seen.Add(trimmed);
            }
        }
    }

    public void OnSeed(string seed)
    {
        var row = new MotelyScoredSeedResult();
        row.Reset(seed, 0);
        OnScored(in row);
    }

    public void OnScored(in MotelyScoredSeedResult tally) => Write(tally.Seed, tally.Score, tally.TallyValuesSpan);

    public void Write(string seed, int? score, ReadOnlySpan<int> tallies = default)
    {
        if (string.IsNullOrEmpty(seed))
            return;

        lock (_gate)
        {
            if (_disposed)
                return;
            if (!_seen.Add(seed))
                return;
            _buffer.Add(seed);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            FlushLocked();
        }
    }

    private void FlushLocked()
    {
        if (_buffer.Count == 0)
            return;

        if (_writer is null)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_seedFilePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            _writer = new StreamWriter(_seedFilePath, append: true) { AutoFlush = false };
        }

        foreach (var seed in _buffer)
            _writer.WriteLine(seed);
        _writer.Flush();
        _buffer.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            try { FlushLocked(); }
            catch { }
            _writer?.Dispose();
            _writer = null;
        }
    }
}
