# Motely 27.0.0

The engine audited end to end, a pool you run yourself, and the JAML grammar that builds its own
filters. Major because the Search API now rejects what it used to run badly.

## Engine

- **Bad settings fail at `Start()`**, not in a worker thread: a batch character count outside 1-7,
  a start batch past the seed space, an end before the start, a negative random count, a keyword
  outside the seed alphabet. Keywords and padding are folded to upper case the way the game does.
- **A throwing worker stops the search** instead of leaving the others running forever. Disposing
  from inside a callback is deferred, so no double free; the native buffers are released once.
- **The SIMD prefilter matches the scalar pass.** Vector pack draws are deduplicated the way the
  scalar draws are; Soul/Black Hole spectrals route to the per-seed path from shop, Sixth Sense and
  Séance sources; the ante-1 pack extension reaches slots 4-7; when every voucher is redeemed the
  draw ends as Blank in both passes.
- **Jamlyzer** bounds its inputs (10,000 event rolls, 100,000 shop slots), resumes shops exactly,
  and reports one-line errors. The CLI validates `--seeds`, keywords, padding, `--startBatch`,
  `--endBatch`, `--batchCharCount` and NaN before it starts.
- `MotelyGlobals.SeedsPerSequentialBatch(n)` and `SequentialBatchCount(n)` hold the batch math
  (35^n seeds per batch, 35^(8-n) batches) that the search, the CLI and the workers each used to
  derive on their own.

## JAML

- Every clause builds its own SIMD filter (`IJamlClause.CreateFilterDesc`). The hand-kept
  clause-to-filter switch is gone: a clause without a filter does not compile.
- Range shorthand on every `int[]` key: `antes: 1-8`, `[1..3, 7]`, `1 to 8`, at most 1024 values.
- Loader errors name the source line, column and offending text, in block and flow style. Unknown
  root keys are rejected instead of ignored.
- All 341 filters under `JamlFilters/` load.

## Your own pool: MotelyHome, MotelyWorker, Motely.MCP

- `MotelyHome` is the queue. Queue a JAML filter and every `MotelyWorker` on the LAN grinds it:
  workers take no arguments and find home over a UDP beacon. The filter's `name:` slugged is its
  id; the finds pile up under it in one DuckDB file (`motely.duckdb`) that survives restarts and
  answers SQL.
- The queue is an MCP server (`/mcp`, streamable HTTP): `queue_filter`, `list_filters`,
  `get_filter`, `get_seeds`, `remove_filter`. Add it as a connector in the Claude app and queue a
  filter from your phone. The same over HTTP: `POST /filters`, `GET /filters/{slug}/seeds`.
- Slices are sized to about 30 seconds of work from each worker's measured rate, handed out
  first-gap-first and round-robin across filters; an unreported slice is re-handed after two
  minutes. See `Motely.HomeApi/README.md`.
- `Motely.CLI --party <id> [--server url]` grinds a seedfinder.app Search Party with the same
  engine, threads and output as any search.

## Browser: Motely.Wasm

Rebuilt from the Bootsharp 0.9 guide: one thread, NativeAOT-LLVM, assemblies embedded in the
package, nothing patched after the build. Four interop modules: `Search`, `Analyze`, `Jaml` and
`JamlFiles`; folder access stays opt-in through `MotelyFileSystem`.

Breaking for 26.x callers:

- `search.start()` takes no token. Call `search.cancel()` to stop; `start()` resolves. The
  `motely-wasm/bcl` CancellationToken is gone from the surface.
- A rejected `with*` or `start()` leaves its reason on `search.error`, because NativeAOT drops
  exception messages at the boundary. `Jaml.check` still explains a filter that does not load.
- The Jimmolate hook (`Search.jimmolate`, `jimmolateSettings`) is not rebuilt.
- Enums keep their C# names (`MotelyDeck`, `MotelyStake`, ...), as jaml-ui imports them.

The package decodes its embedded assemblies with `Uint8Array.fromBase64`: Node 24+, Chrome 140+,
Safari 18.2+, Firefox 133+. CI publishes it and runs a Node smoke test on every PR.

## Removed

- `Motely.Lsp` and the DuckLake-era `Motely.DistributedWorker` and `Motely.DataLake`.

## CI

`.github/workflows/ci.yml` builds `Motely.slnx` and runs the xunit suite (639 tests) in Release on
every PR and push to master. `release.yml` runs it before cutting a GitHub Release from these notes.
