using Motely.Filters;

namespace Motely.DataLake;

/// <summary>
/// One filter's seed writer: bare seeds land in a plain text file under the data root, one per
/// line, deduped in memory. The scored CSV (<see cref="ScoredResultsCsvSink"/>) carries scores and
/// tallies; this file is the bare-seed archive that <c>--drown</c> pours. Thread-safe; result
/// callbacks fire on every engine thread.
/// </summary>
public sealed class SeedLakeSink : IMotelyResultSink
{
    private readonly string _seedFilePath;
    private readonly object _gate = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _buffer = [];
    private StreamWriter? _writer;
    private bool _disposed;

    /// <summary>The data root, absolute: <paramref name="root"/>, else <c>MOTELY_DATALAKE_PATH</c>, else <c>Seeds</c>.</summary>
    public static string LakeRoot(string? root)
    {
        root ??= Environment.GetEnvironmentVariable("MOTELY_DATALAKE_PATH");
        return string.IsNullOrWhiteSpace(root) ? "Seeds" : root;
    }

    /// <summary>Legacy per-filter DuckDB path — kept for backward-compatible reading of old data.</summary>
    public static string LakePath(string? root, string filterId) =>
        Path.Combine(LakeRoot(root), filterId + ".duckdb");

    /// <summary>The plain-text seed file this sink writes to.</summary>
    public static string SeedFilePath(string? root, string filterId) =>
        Path.Combine(LakeRoot(root), filterId + ".txt");

    /// <param name="root">Data root; see <see cref="LakeRoot"/>.</param>
    /// <param name="filterId">The JAML filter id; names the seed file.</param>
    /// <param name="tallyLabels">Ignored (kept for API compat; tallies live in the CSV sink).</param>
    /// <param name="catalogPath">Ignored (kept for API compat; DuckLake is gone).</param>
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

    /// <summary>Push buffered seeds to the text file. Search batch boundary.</summary>
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
            catch { /* best-effort */ }
            _writer?.Dispose();
            _writer = null;
        }
    }
}
