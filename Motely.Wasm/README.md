# motely-wasm

The Motely engine in one JS package, compiled by NativeAOT-LLVM through
[Bootsharp](https://bootsharp.com). JS gets the engine's own API: the same classes, methods and
types the CLI uses, with no wrapper layer in between.

```js
import bootsharp, { JamlConfigLoader, JamlSearchBuilder, MotelyJamlyzer, CancellationToken } from "motely-wasm";

await bootsharp.boot();

JamlConfigLoader.check(text);                       // null, or the reason with its line
const config = JamlConfigLoader.fromJaml(text);     // the engine's JamlConfig, as a plain object

const token = new CancellationToken();
const search = JamlSearchBuilder.createSettings(config)
  .withThreadCount(1)
  .withSequentialSearch()
  .withSeedMatchCallback((seed) => console.log(seed))
  .withSeedAnalyzeProvider(MotelyJamlyzer.createRiderDesc(config, (r) => console.log(r.antes)))
  .start(token);                                    // token.cancel() stops it
await search.waitForCompletionAsync();
search.dispose();                                   // frees the search's native buffers

MotelyJamlyzer.analyze(config);                     // one result per seed in the config's seeds:
```

## How the engine crosses

- Records (`JamlConfig`, `MotelyJamlyzerSeedResult`, `MotelyProgress`, ...) cross by value as
  plain objects. Classes and interfaces (`IMotelySearchSettings`, `IMotelySearch`, clauses,
  filter descs, the Jamlyzer rider) cross by reference. `long` is `bigint`.
- Members whose signature holds a ref, a pointer or a ref struct are erased by one rule in
  `Program.cs` (`Names`): that's the SIMD plumbing, which has no JS form. Their types still cross
  as handles, so an engine-made filter desc or rider moves between engine calls as itself.
  `Specializations.cs` makes a JS-made SIMD hook throw instead of pretending to run.
- `withJimmolate` and `withSeedGenerator` are erased. The engine documents both as native-only:
  a per-seed predicate would cross once per seed, and a lazy sequence has no value to serialize
  (`withSeedList` is the crossing form). `awaitCompletion` is erased too: it blocks the thread the
  search runs on, which in the browser would hang the page.
- A search holds native buffers until `dispose()` (otherwise until the GC finalizes it). Dispose
  every search you start; disposing a running one stops it and settles `waitForCompletionAsync`.
- `withSeedList` runs the whole list inside `start()`, with no yield to the event loop. Keep
  lists small in a page, or use a sequential/keyword search, which yields between batches.
- `MotelyProgress.percentComplete` is 0-100.
- The NativeAOT runtime gives a thrown C# exception to JS as "C# exception from NativeAOT",
  without its message. After any call throws (a filter that does not load, a rejected `with*`
  value, a `start` that fails), `Errors.last()` returns its message. `JamlConfigLoader.check`
  returns the loader's reason without throwing.

## Hosts

The package decodes its embedded assemblies with `Uint8Array.fromBase64`: Node 25+, Chrome 140+,
Safari 18.2+, Firefox 133+. An older host needs that function shimmed before `boot()`, for
example `Uint8Array.fromBase64 ??= (s) => new Uint8Array(Buffer.from(s, "base64"))` on Node.
No COOP/COEP headers and no SharedArrayBuffer are needed.

## Build

```
dotnet workload install wasm-tools
dotnet publish -c Release        # bin/motely-wasm, warnings are errors, nothing suppressed
npm install                      # @rewaffle/bootsharp-file-system, for the folder tests
npm test                         # node --test "tests/*.test.mjs"
```

Install [Binaryen](https://github.com/WebAssembly/binaryen/releases) and put `wasm-opt` on
PATH first. Bootsharp runs it on every Release publish; when it's missing it logs a warning
and ships an unoptimized binary.

The default build includes `JamlFiles` (folder access through Bootsharp.FileSystem). That package
is not on nuget.org: add the rewaffle sponsor feed as a `rewaffle` source in your user-level
NuGet.Config (the repo's nuget.config already maps `Bootsharp.FileSystem` to it). JS then calls
`fs.init(IFileMounter)` (`fs` from `@rewaffle/bootsharp-file-system`, `IFileMounter` from
`motely-wasm/bootsharp/file-system`) before `boot()`.

Without the feed, build with the flag off:

```
dotnet publish -c Release -p:MotelyFileSystem=false
```

`JamlFiles` keeps the same exports there: `isSupported()` is false, `list()` is empty, and the
file calls reject.

The engine's YAML loader is YamlDotNet's static (source-generated) deserializer through the
Vecc generator (`Vecc.YamlDotNet.Analyzers.StaticGenerator`): no reflection, so NativeAOT
trims it cleanly.
