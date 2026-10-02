# motely-wasm 27.0.0

Major release: the TypeScript enum names changed. Everything else is additive or a fix.

## Breaking

- **Enum rename.** All 17 TS enums drop the `Motely` prefix (`MotelyDeck` is now `Deck`, `MotelyItemEdition` is now `ItemEdition`, and so on). Members and values are identical. Rename your imports; `import { Deck as MotelyDeck }` keeps old call sites working.
- **`SearchSettings.results` removed.** Read matches from `Search.onScored`.
- **`JamlAesthetic` renumbered.** `Runs` was inserted before `Step`, so Step through Nsfw each move up one ordinal. Compare by name, not number.

## Added

- `Jaml.check(text)` returns the loader's line-numbered message, or null when the document loads.
- Range shorthand on every `int[]` key: `antes: 1-8`, `[1..3, 7]`, `1 to 8`. A key expands to at most 1024 values.
- `CancellationToken` is importable from JS.
- `JamlFiles.isSupported()`: false in a build without Bootsharp.FileSystem, so a UI can hide its folder picker.

## Your own pool: MotelyHome + MotelyWorker + Motely.MCP

- `MotelyHome` is the queue: queue a JAML filter and every `MotelyWorker` on the LAN grinds it,
  no arguments, found over a UDP beacon. The filter's `name:` slugged is its id; the finds pile up
  under it in one DuckDB file (`motely.duckdb`) that survives restarts and answers SQL.
- The queue is an MCP server (`/mcp`, streamable HTTP, stateless): `queue_filter`, `list_filters`,
  `get_filter`, `get_seeds`, `remove_filter`. Add it as a connector in the Claude app and queue a
  filter from your phone. The same over HTTP: `POST /filters`, `GET /filters/{slug}/seeds`.
- Slices are sized to about 30 seconds of work from each worker's measured rate, handed out
  first-gap-first and round-robin across filters; an unreported slice is re-handed after two minutes.
- `Motely.CLI --party <id>` grinds a seedfinder.app Search Party with the same engine.
- Every JAML clause builds its own SIMD filter (`IJamlClause.CreateFilterDesc`); the hand-kept
  clause→filter switch is gone.

## Fixed

- `onScored` fires once per find (26.0.2 fired it twice).
- Loader errors name the source line, column and offending text, in block and flow style.
- All 341 filters under `JamlFilters/` load.
- C# exceptions reach JS with their message. NativeAOT turned every one into `Error("C# exception from NativeAOT")`, so `Search.settings(badJaml)` lost its `JAML line n: ...` text; `scripts/patch-dist-exception-message.mjs` restores it in the build.
- `JamlFiles` docs name the real JS method, `$delete(name)` (Bootsharp prefixes the reserved word).

## Build

- Bootsharp.FileSystem is opt-in (`MotelyFileSystem=true`, or `MOTELY_FILESYSTEM=true`), so a clone without the sponsor feed restores and builds. `npm run build` turns it on; `npm run build:no-fs` is the same build without it. Both builds export the same API.
- CI: `.github/workflows/ci.yml` runs the xunit suite, `build:no-fs`, the headless-Chrome smoke test and the API/README gates on every PR and push to master. `release.yml` runs it before cutting a GitHub Release; it no longer builds `Motely.Lsp`, which was removed from the repo.

## Publish

From a machine with the Bootsharp.FileSystem sponsor feed and `wasm-opt` (`npm run build` needs the feed; it fails at restore without it rather than publishing a package without folder access):

```
cd Motely.Wasm
npm run build
npm run api:check        # must say: major bump allows it
npm run readme:gen       # regenerate the API section from the real typings
npm run readme:check
npm publish
```

`prepublishOnly` re-runs the script tests, build, version, API and README checks, so a failing gate stops the publish.

Then publish jaml-lang from this same commit, and bump consumers: jaml-ui 7.0.0 (import rename, ready on a local branch), then seedfinder.app.
