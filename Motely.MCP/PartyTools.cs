using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Motely.MCP;

[McpServerToolType]
public sealed class PartyTools(Parties parties)
{
    [McpServerTool(Name = "party_start")]
    [Description("Split a sequential sweep of a JAML filter across local Motely.CLI worker processes. Returns the party id.")]
    public string Start(
        [Description("Path to the .jaml filter.")] string jaml,
        [Description("Worker processes. Default: 1 per 4 cores.")] int workers = 0,
        [Description("Threads per worker. Default: cores / workers.")] int threads = 0,
        [Description("Sequential batch character count, 1-7.")] int batchCharCount = 4,
        [Description("First batch of the sweep.")] long startBatch = 0,
        [Description("End batch, exclusive. 0 sweeps to the end.")] long endBatch = 0,
        [Description("Score cutoff passed to each worker: a number, 'auto' or 'off'. Default: the CLI's.")] string? cutoff = null
    )
    {
        var path = Path.GetFullPath(jaml);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No JAML at {path}.");
        int cores = Environment.ProcessorCount;
        if (workers <= 0)
            workers = Math.Max(1, cores / 4);
        if (threads <= 0)
            threads = Math.Max(1, cores / workers);
        var party = parties.Start(path, workers, threads, batchCharCount, startBatch, endBatch, cutoff);
        return $"{party.Id}: {party.Workers.Count} worker(s) x {threads} thread(s) over {party.TotalBatches:N0} batches of {path}";
    }

    [McpServerTool(Name = "party_status")]
    [Description("Workers, ranges, exit codes and the best finds of a party.")]
    public string Status([Description("Party id from party_start.")] string id, [Description("How many top finds to list.")] int top = 20)
    {
        var party = parties.Get(id);
        var text = new StringBuilder();
        text.AppendLine($"{party.Id} {(party.Done ? "done" : "running")} | {party.Jaml} | {party.Finds.Count:N0} finds | {DateTime.UtcNow - party.StartedUtc:hh\\:mm\\:ss}");
        foreach (var w in party.Workers)
        {
            string state = w.Process.HasExited ? $"exit {w.Process.ExitCode}" : "running";
            text.AppendLine($"  worker {w.Index}: batches {w.StartBatch:N0}-{w.EndBatch:N0} {state}{(w.LastError is null ? "" : $" | {w.LastError}")}");
        }
        foreach (var find in party.Finds.OrderByDescending(f => f.Score).Take(top))
            text.AppendLine($"  {find.Seed} {find.Score}");
        return text.ToString();
    }

    [McpServerTool(Name = "party_list")]
    [Description("Every party this server has started.")]
    public string List() =>
        string.Join('\n', parties.All.Select(p => $"{p.Id} {(p.Done ? "done" : "running")} {p.Finds.Count:N0} finds {p.Jaml}"));

    [McpServerTool(Name = "party_stop")]
    [Description("Kill a party's workers. Finds so far are kept.")]
    public string Stop([Description("Party id from party_start.")] string id)
    {
        var party = parties.Get(id);
        party.Stop();
        return $"{party.Id} stopped with {party.Finds.Count:N0} finds.";
    }

    [McpServerTool(Name = "party_save")]
    [Description("Merge a party's finds into the top-level seeds: block of its JAML file.")]
    public string Save([Description("Party id from party_start.")] string id)
    {
        var party = parties.Get(id);
        var seeds = party.Finds.OrderByDescending(f => f.Score).Select(f => f.Seed).Distinct().ToList();
        if (seeds.Count == 0)
            return "No finds to save.";
        if (!MotelyTopSeedSink.TryRewriteAndValidate(File.ReadAllText(party.Jaml), seeds, out var updated, out var error))
            throw new InvalidOperationException(error);
        File.WriteAllText(party.Jaml, updated);
        return $"Saved {seeds.Count:N0} seed(s) into {party.Jaml}.";
    }
}
