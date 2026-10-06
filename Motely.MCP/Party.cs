using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Motely.MCP;

/// <summary>One sequential sweep split into contiguous batch ranges, one Motely.CLI process per range.</summary>
public sealed partial class Party : IDisposable
{
    private readonly List<Worker> _workers = [];
    private readonly ConcurrentQueue<Find> _finds = new();

    public string Id { get; }
    public string Jaml { get; }
    public int BatchCharCount { get; }
    public long TotalBatches { get; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;

    public IReadOnlyList<Worker> Workers => _workers;
    public IReadOnlyCollection<Find> Finds => _finds;
    public bool Done => _workers.All(w => w.Process.HasExited);

    private Party(string id, string jaml, int batchCharCount)
    {
        Id = id;
        Jaml = jaml;
        BatchCharCount = batchCharCount;
        TotalBatches = MotelyGlobals.SequentialBatchCount(batchCharCount);
    }

    public static Party Start(string id, string jaml, int workers, int threadsPerWorker, int batchCharCount, long startBatch, long endBatch, string? cutoff)
    {
        var party = new Party(id, jaml, batchCharCount);
        if (endBatch <= 0 || endBatch > party.TotalBatches)
            endBatch = party.TotalBatches;
        if (startBatch < 0 || startBatch >= endBatch)
            throw new ArgumentOutOfRangeException(nameof(startBatch), $"Need 0 <= startBatch < endBatch ({endBatch}).");
        long span = endBatch - startBatch;
        workers = (int)Math.Clamp(workers, 1, span);
        for (int i = 0; i < workers; i++)
        {
            long start = startBatch + span * i / workers;
            long end = startBatch + span * (i + 1) / workers;
            party._workers.Add(party.Launch(i, start, end, threadsPerWorker, cutoff));
        }
        return party;
    }

    private Worker Launch(int index, long startBatch, long endBatch, int threads, string? cutoff)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "cli", "Motely.CLI.dll"));
        foreach (var arg in new[]
        {
            "--jaml", Jaml,
            "--batchCharCount", BatchCharCount.ToString(),
            "--startBatch", startBatch.ToString(),
            "--endBatch", endBatch.ToString(),
            "--threads", threads.ToString(),
            "--no-save",
            "-q",
        })
            info.ArgumentList.Add(arg);
        if (cutoff is not null)
        {
            info.ArgumentList.Add("--cutoff");
            info.ArgumentList.Add(cutoff);
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException("Motely.CLI did not start.");
        var worker = new Worker(index, startBatch, endBatch, process);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line && FindLine().Match(line) is { Success: true } m)
                _finds.Enqueue(new Find(m.Groups["seed"].Value, int.Parse(m.Groups["score"].Value), index));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
                worker.LastError = line;
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return worker;
    }

    public void Stop()
    {
        foreach (var worker in _workers)
            if (!worker.Process.HasExited)
                worker.Process.Kill(entireProcessTree: true);
    }

    public void Dispose()
    {
        Stop();
        foreach (var worker in _workers)
            worker.Process.Dispose();
    }

    [GeneratedRegex(@"^(?<seed>[1-9A-Z]{1,8}),\s*(?<score>-?\d+)")]
    private static partial Regex FindLine();

    public sealed class Worker(int index, long startBatch, long endBatch, Process process)
    {
        public int Index { get; } = index;
        public long StartBatch { get; } = startBatch;
        public long EndBatch { get; } = endBatch;
        public Process Process { get; } = process;
        public string? LastError { get; set; }
    }

    public readonly record struct Find(string Seed, int Score, int Worker);
}

/// <summary>The parties this server has started.</summary>
public sealed class Parties : IDisposable
{
    private readonly ConcurrentDictionary<string, Party> _parties = new();
    private int _next;

    public Party Start(string jaml, int workers, int threadsPerWorker, int batchCharCount, long startBatch, long endBatch, string? cutoff)
    {
        var id = $"party-{Interlocked.Increment(ref _next)}";
        var party = Party.Start(id, jaml, workers, threadsPerWorker, batchCharCount, startBatch, endBatch, cutoff);
        _parties[id] = party;
        return party;
    }

    public Party Get(string id) =>
        _parties.TryGetValue(id, out var party)
            ? party
            : throw new InvalidOperationException($"No party '{id}'. Parties: {string.Join(", ", _parties.Keys)}");

    public IEnumerable<Party> All => _parties.Values;

    public void Dispose()
    {
        foreach (var party in _parties.Values)
            party.Dispose();
    }
}
