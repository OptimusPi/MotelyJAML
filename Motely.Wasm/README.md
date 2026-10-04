# motely-wasm

Motely in the browser: one JS package, one thread, the engine compiled by NativeAOT-LLVM through
[Bootsharp](https://sharp.elringus.com). The assemblies are embedded in the package, so there is
nothing to host beside the JS, and nothing in `bin/motely-wasm` is patched after the build.

```js
import bootsharp, { Search, Analyze, Jaml } from "motely-wasm";
import { CancellationToken } from "motely-wasm/bcl";

await bootsharp.boot();

Jaml.check(text);                       // null, or "JAML line 3: ..."
Search.onScored.subscribe((s) => console.log(s.seed, s.score));
const settings = Search.settings(text).withSequentialSearch().withBatchCharacterCount(4);
await settings.start(new CancellationToken());   // cancel() the token to stop
Analyze.seeds(text);                    // Jamlyzer, one result per seed in the filter's `seeds:`
```

`Search.settings(jaml)` returns the engine's fluent settings (`withSeedList`, `withStartBatchIndex`,
`withEndBatchIndex`, `withKeywordSearch`, `withRandomSearch`, `withAnalysis(eventRolls)`, `stopAfter`,
...). Finds arrive on `Search.onScored`, progress on `Search.onProgress`, and with `withAnalysis`
each find's breakdown follows on `Search.onAnalyzed`. `Analyze.seedsPaged` and `Analyze.seedsResume`
scroll one seed's shop and roll queues.

## Build

```
dotnet workload install wasm-tools
cd Motely.Wasm
dotnet publish -c Release        # bin/motely-wasm
node tests/smoke.mjs
```

Release publish switches on NativeAOT-LLVM and trimming by itself. `wasm-opt` (Binaryen) is optional;
Bootsharp warns when it is missing and the build still works.

## Folder access (optional)

`JamlFiles` reads and writes `.jaml` files in a folder the user picks. It needs Bootsharp.FileSystem,
a Bootsharp sponsor package served from the rewaffle feed, so it is off by default and the package
builds from nuget.org alone. Turn it on with `-p:MotelyFileSystem=true` or `MOTELY_FILESYSTEM=true`
(see `nuget.config` for the feed) and call `fs.init(Bootsharp.FileSystem.FileMounter)` before `boot()`.
Off, `JamlFiles` keeps the same exports: `isSupported()` is false, `list()` is empty and the file
calls reject.

## Notes

- The package decodes its embedded assemblies with `Uint8Array.fromBase64`: Node 24+, Chrome 140+,
  Safari 18.2+, Firefox 133+. Older hosts need a shim before `boot()`.
- C# exceptions reach JS as "C# exception from NativeAOT" with no message. Call `Jaml.check(text)`
  first to get the line-numbered reason a filter does not load.
- No COEP/COOP headers, no SharedArrayBuffer: the search runs on one thread.
