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

## Fixed

- `onScored` fires once per find (26.0.2 fired it twice).
- Loader errors name the source line, column and offending text, in block and flow style.
- All 341 filters under `JamlFilters/` load.

## Publish

From a machine with the Bootsharp.FileSystem sponsor feed and `wasm-opt`:

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
