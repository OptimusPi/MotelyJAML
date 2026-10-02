using System.Globalization;
using System.Text;
using DuckDB.NET.Data;
using Motely.Distributed;
using Motely.Filters.Jaml;

namespace Motely.Mcp;

/// <summary>
/// Every queued filter, what is done, what is out on claim, and the finds. One DuckDB file
/// (<c>filters</c>, <c>done</c>, <c>seeds</c> tables), so a restart picks up where it stopped and
/// the finds are a <c>SELECT</c> away. Claims live in memory only: an unfinished claim is handed
/// out again after <see cref="ClaimTtl"/>, or right away after a restart.
/// </summary>
public sealed class FilterQueue : IDisposable
{
    /// <summary>A claim not reported back within this is handed to the next worker that asks.</summary>
    public static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);

    /// <summary>Each claim is sized to about this much work for the worker asking, from its last claim's rate.</summary>
    public static readonly TimeSpan ClaimTarget = TimeSpan.FromSeconds(30);

    private readonly DuckDBConnection _db;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly List<Filter> _filters = [];
    private readonly Dictionary<string, double> _seedsPerSecond = new(StringComparer.Ordinal);
    private int _next;

    public FilterQueue(string dbPath, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _db = new DuckDBConnection($"Data Source={dbPath}");
        _db.Open();
        Execute(
            """
            CREATE TABLE IF NOT EXISTS filters (
                slug VARCHAR PRIMARY KEY, name VARCHAR NOT NULL, jaml VARCHAR NOT NULL,
                batch_chars INTEGER NOT NULL, start_batch BIGINT NOT NULL, end_batch BIGINT NOT NULL,
                queued_at TIMESTAMP NOT NULL);
            CREATE TABLE IF NOT EXISTS done (
                slug VARCHAR NOT NULL, start_batch BIGINT NOT NULL, end_batch BIGINT NOT NULL,
                worker VARCHAR NOT NULL, seeds_searched BIGINT NOT NULL, finished_at TIMESTAMP NOT NULL);
            CREATE TABLE IF NOT EXISTS seeds (
                slug VARCHAR NOT NULL, seed VARCHAR NOT NULL, score INTEGER NOT NULL,
                worker VARCHAR NOT NULL, found_at TIMESTAMP NOT NULL, PRIMARY KEY (slug, seed));
            """
        );
        Load();
    }

    /// <summary>
    /// Queues a JAML under the slug of its <c>name:</c>. The same JAML again keeps its progress; a
    /// different JAML under a slug already queued is refused until that one is removed.
    /// </summary>
    public FilterStatus Add(string jaml, int batchChars = 4)
    {
        var config = JamlConfigLoader.FromJaml(jaml);
        string name = string.IsNullOrWhiteSpace(config.Name) ? config.Id : config.Name;
        string slug = FilterSlug.Of(name);
        if (slug.Length == 0)
            throw new ArgumentException("The JAML needs a name: line; its slug is the filter's id.");
        long batches = MotelyGlobals.SequentialBatchCount(batchChars);

        lock (_lock)
        {
            if (Find(slug) is { } existing)
            {
                if (existing.Jaml != jaml || existing.BatchChars != batchChars)
                    throw new InvalidOperationException($"'{slug}' is already queued with a different filter; remove it first.");
                return existing.Status();
            }
            Execute(
                "INSERT INTO filters VALUES ($slug, $name, $jaml, $batch_chars, 0, $end_batch, $now)",
                ("slug", slug), ("name", name), ("jaml", jaml), ("batch_chars", batchChars),
                ("end_batch", batches), ("now", _time.GetUtcNow().UtcDateTime)
            );
            var filter = new Filter(slug, name, jaml, batchChars, 0, batches);
            _filters.Add(filter);
            return filter.Status();
        }
    }

    public bool Remove(string slug)
    {
        lock (_lock)
        {
            if (Find(slug) is not { } filter)
                return false;
            Execute(
                "DELETE FROM seeds WHERE slug = $slug; DELETE FROM done WHERE slug = $slug; DELETE FROM filters WHERE slug = $slug",
                ("slug", slug)
            );
            _filters.Remove(filter);
            return true;
        }
    }

    public FilterStatus[] List()
    {
        lock (_lock)
            return [.. _filters.Select(f => f.Status())];
    }

    public FilterStatus? Status(string slug)
    {
        lock (_lock)
            return Find(slug)?.Status();
    }

    /// <summary>Finds, best first, as <c>seed,score,worker</c> lines; null for an unknown filter.</summary>
    public string? Seeds(string slug, int top = int.MaxValue)
    {
        lock (_lock)
        {
            if (Find(slug) is null)
                return null;
            var sb = new StringBuilder("seed,score,worker\n");
            using var cmd = Command(
                "SELECT seed, score, worker FROM seeds WHERE slug = $slug ORDER BY score DESC, seed LIMIT $top",
                ("slug", slug), ("top", (long)top)
            );
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                sb.Append(CultureInfo.InvariantCulture, $"{reader.GetString(0)},{reader.GetInt32(1)},{reader.GetString(2)}\n");
            return sb.ToString();
        }
    }

    /// <summary>The next slice for this worker, round-robin over the filters; null when nothing is left to hand out.</summary>
    public Work? Claim(string worker)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            foreach (var f in _filters)
                f.Out.RemoveAll(o => now - o.At >= ClaimTtl);

            for (int i = 0; i < _filters.Count; i++)
            {
                var filter = _filters[(_next + i) % _filters.Count];
                if (filter.NextGap(BatchesFor(worker, filter)) is not var (start, end))
                    continue;
                _next = (_next + i + 1) % _filters.Count;
                filter.Out.Add((start, end, worker, now));
                return new Work(filter.Slug, filter.Jaml, filter.BatchChars, start, end);
            }
            return null;
        }
    }

    /// <summary>Records a finished slice. A slice whose claim expired still counts; its seeds are kept once.</summary>
    public bool Done(WorkDone done)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (Find(done.Filter) is not { } filter)
                return false;
            if (done.Start < filter.Start || done.End > filter.End || done.Start >= done.End)
                return false;

            int held = filter.Out.FindIndex(o => o.Start == done.Start && o.End == done.End);
            if (held >= 0)
            {
                var claim = filter.Out[held];
                filter.Out.RemoveAt(held);
                if (claim.Worker == done.Worker && done.SeedsSearched > 0 && now > claim.At)
                    _seedsPerSecond[done.Worker] = done.SeedsSearched / (now - claim.At).TotalSeconds;
            }

            using var tx = _db.BeginTransaction();
            Execute(
                "INSERT INTO done VALUES ($slug, $start, $end, $worker, $searched, $now)",
                ("slug", done.Filter), ("start", done.Start), ("end", done.End),
                ("worker", done.Worker), ("searched", done.SeedsSearched), ("now", now.UtcDateTime)
            );
            foreach (var seed in done.Seeds)
                Execute(
                    "INSERT OR IGNORE INTO seeds VALUES ($slug, $seed, $score, $worker, $now)",
                    ("slug", done.Filter), ("seed", seed.Seed), ("score", seed.Score),
                    ("worker", done.Worker), ("now", now.UtcDateTime)
                );
            tx.Commit();

            filter.AddDone(done.Start, done.End);
            filter.Finds = Count("SELECT count(*) FROM seeds WHERE slug = $slug", ("slug", done.Filter));
            return true;
        }
    }

    public void Dispose() => _db.Dispose();

    private long BatchesFor(string worker, Filter filter)
    {
        if (!_seedsPerSecond.TryGetValue(worker, out var rate))
            return 1;
        double batches = rate * ClaimTarget.TotalSeconds / MotelyGlobals.SeedsPerSequentialBatch(filter.BatchChars);
        return (long)Math.Clamp(batches, 1, 1_000_000);
    }

    private Filter? Find(string slug) =>
        _filters.Find(f => f.Slug == slug);

    private void Load()
    {
        using (var cmd = Command("SELECT slug, name, jaml, batch_chars, start_batch, end_batch FROM filters ORDER BY queued_at, slug"))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                _filters.Add(new Filter(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3), reader.GetInt64(4), reader.GetInt64(5)
                ));

        foreach (var filter in _filters)
        {
            using (var cmd = Command("SELECT start_batch, end_batch FROM done WHERE slug = $slug", ("slug", filter.Slug)))
            using (var reader = cmd.ExecuteReader())
                while (reader.Read())
                    filter.AddDone(reader.GetInt64(0), reader.GetInt64(1));
            filter.Finds = Count("SELECT count(*) FROM seeds WHERE slug = $slug", ("slug", filter.Slug));
        }
    }

    private DuckDBCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(new DuckDBParameter(name, value));
        return cmd;
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        cmd.ExecuteNonQuery();
    }

    private int Count(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = Command(sql, parameters);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private sealed class Filter(string slug, string name, string jaml, int batchChars, long start, long end)
    {
        public readonly string Slug = slug, Name = name, Jaml = jaml;
        public readonly int BatchChars = batchChars;
        public readonly long Start = start, End = end;

        /// <summary>Finished ranges, merged and sorted.</summary>
        private readonly List<(long Start, long End)> _done = [];
        private long _batchesDone;

        public readonly List<(long Start, long End, string Worker, DateTimeOffset At)> Out = [];
        public int Finds;

        public void AddDone(long start, long end)
        {
            _done.Add((start, end));
            _done.Sort((a, b) => a.Start.CompareTo(b.Start));
            int w = 0;
            for (int r = 1; r < _done.Count; r++)
            {
                if (_done[r].Start <= _done[w].End)
                    _done[w] = (_done[w].Start, Math.Max(_done[w].End, _done[r].End));
                else
                    _done[++w] = _done[r];
            }
            _done.RemoveRange(w + 1, _done.Count - w - 1);
            _batchesDone = _done.Sum(d => d.End - d.Start);
        }

        /// <summary>The first <c>[a, b)</c> inside this filter that is neither done nor out on claim, at most <paramref name="max"/> long.</summary>
        public (long Start, long End)? NextGap(long max)
        {
            var covered = _done.Concat(Out.Select(o => (o.Start, o.End))).OrderBy(r => r.Start).ToList();
            long at = Start;
            foreach (var (s, e) in covered)
            {
                if (s > at)
                    break;
                at = Math.Max(at, e);
            }
            if (at >= End)
                return null;
            long gapEnd = End;
            foreach (var (s, _) in covered)
                if (s > at)
                {
                    gapEnd = Math.Min(gapEnd, s);
                    break;
                }
            return (at, Math.Min(gapEnd, at + max));
        }

        public FilterStatus Status() =>
            new(Slug, Name, BatchChars, _batchesDone, End - Start, Finds, _batchesDone >= End - Start,
                [.. Out.Select(o => o.Worker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }
}
