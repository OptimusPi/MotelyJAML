# MotelyHome, MotelyWorker, Motely.MCP

Your own seed-grinding pool. Queue a JAML filter once; every worker on the network grinds it
and the finds pile up under the filter's name. The filter is the party, its slug is the id.

```
MotelyHome                       # the queue: http://0.0.0.0:35036, motely.duckdb in the cwd
MotelyWorker                     # on every machine: finds MotelyHome on the LAN, grinds forever
```

## Queue a filter

From the Claude app on your phone: Settings → Connectors → add `http://<home>:35036/mcp`
(put a TLS tunnel in front of it when the phone is off the LAN). Then ask for it:

> queue this filter … / how far along is negative-perkeo-ante-1? / show me its best seeds

| MCP tool | Does |
|---|---|
| `queue_filter(jaml, batchChars=4)` | Queue a JAML. Its `name:` slugged is the filter id. The same JAML again keeps its progress. |
| `list_filters()` | Every filter: percent done, finds, workers on it. |
| `get_filter(filter)` | One filter's progress. |
| `get_seeds(filter, top=100)` | The finds, best score first, as `seed,score,worker` lines. |
| `remove_filter(filter)` | Drop the filter, its progress and its finds. |

The same over plain HTTP, for curl or a browser:

```
POST   /filters?batchChars=4      body: the JAML            → the filter's status
GET    /filters                                             → every filter's status
GET    /filters/{slug}                                      → one filter's status
GET    /filters/{slug}/seeds?top=100                        → seed,score,worker lines
DELETE /filters/{slug}
GET    /                                                    → one line per filter
```

```sh
curl --data-binary @JamlFilters/NegativePerkeoAnte1.jaml "http://home:35036/filters"   # name: Negative Trickeo-glyph
curl http://home:35036/filters/negative-trickeo-glyph/seeds?top=20
```

## How the work is split

A filter is `35^(8 - batchChars)` engine batches of `35^batchChars` seeds (1,500,625 seeds per
batch at the default 4). Home hands out the first batches nobody has finished or is holding,
round-robin across the queued filters so they all move. A claim is sized to about 30 seconds of
work from that worker's last measured rate (its first claim is one batch). A claim not reported
back within two minutes is handed to the next worker that asks; a cancelled worker reports
nothing, so its slice goes back on the pile.

Workers send seed strings and scores only; home records each seed once per filter, and a slice's
report is capped at 10,000 seeds (a filter that matches more than that per slice needs tightening).

Everything done lives in one DuckDB file, so a restart resumes and the finds are a query away:

```sql
-- duckdb motely.duckdb
SELECT seed, score, worker FROM seeds WHERE slug = 'negative-trickeo-glyph' ORDER BY score DESC;
SELECT slug, sum(end_batch - start_batch) AS batches_done, sum(seeds_searched) FROM done GROUP BY slug;
```

Tables: `filters(slug, name, jaml, batch_chars, start_batch, end_batch, queued_at)`,
`done(slug, start_batch, end_batch, worker, seeds_searched, finished_at)`,
`seeds(slug, seed, score, worker, found_at)`. Claims are in memory only.

## Running it

```
MotelyHome [--db motely.duckdb] [--urls http://0.0.0.0:35036]
MotelyWorker [--home http://host:35036] [--threads N] [--name NAME]
```

Home broadcasts `motely-home:<port>` on UDP 35035 every two seconds. A worker started without
`--home` listens for that and uses the sender's address; if that home then goes quiet for a couple
of minutes it listens again. `--home` skips discovery (a different subnet, a VPN, a tunnel).
`--name` defaults to the machine name; it is what the dashboard and the `seeds` table show.

Build and publish like the CLI:

```sh
dotnet run --project Motely.HomeApi
dotnet run --project Motely.DistributedWorker
dotnet publish Motely.DistributedWorker -c Release -r win-x64     # NativeAOT MotelyWorker.exe
dotnet publish Motely.HomeApi -c Release -r win-x64
```

`Motely.MCP` is the library both share: `FilterQueue` (the DuckDB-backed queue) and `MotelyTools`
(the MCP tools). `Motely/Distributed/HomeProtocol.cs` is the wire: `Work`, `WorkDone`,
`FilterStatus`, the beacon, the slug.
