using Motely.Filters;

namespace Motely.DataLake;

public sealed class ScoredResultsCsvSink : IMotelyResultSink
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly IReadOnlyList<string> _tallyLabels;
    private StreamWriter? _writer;
    private bool _disposed;

    public static string ResultsPath(string? root, string filterId)
    {
        root ??= Environment.GetEnvironmentVariable("MOTELY_DATALAKE_PATH");
        return Path.Combine(string.IsNullOrWhiteSpace(root) ? "Seeds" : root, filterId + ".csv");
    }

    public ScoredResultsCsvSink(string? root, string filterId, IReadOnlyList<string> tallyLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterId);
        ArgumentNullException.ThrowIfNull(tallyLabels);
        _path = ResultsPath(root, filterId);
        _tallyLabels = tallyLabels;
    }

    public void OnSeed(string seed)
    {
        var row = new MotelyScoredSeedResult();
        row.Reset(seed, 0);
        OnScored(in row);
    }

    public void OnScored(in MotelyScoredSeedResult tally)
    {
        string seed = tally.Seed;
        if (string.IsNullOrEmpty(seed))
            return;

        int score = tally.Score;
        var span = tally.TallyValuesSpan;

        var sb = new System.Text.StringBuilder();
        sb.Append(seed).Append(',').Append(score);
        foreach (int v in span)
            sb.Append(',').Append(v);
        string line = sb.ToString();

        lock (_gate)
        {
            if (_disposed)
                return;

            if (_writer is null)
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(_path));
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                bool writeHeader = !File.Exists(_path) || new FileInfo(_path).Length == 0;
                _writer = new StreamWriter(_path, append: true) { AutoFlush = false };
                if (writeHeader)
                    _writer.WriteLine(
                        _tallyLabels.Count == 0
                            ? "Seed,Score"
                            : $"Seed,Score,{string.Join(",", _tallyLabels)}"
                    );
            }

            _writer.WriteLine(line);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _writer?.Flush();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
