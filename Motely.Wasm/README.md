# motely-wasm

The Motely engine in one JS package, compiled by NativeAOT-LLVM through
[Bootsharp](https://bootsharp.com). JS gets the engine's own API: the same classes, methods and
types the CLI uses, with no wrapper layer in between.

```js
import bootsharp, { JamlConfigLoader, JamlSearchBuilder, MotelyJamlyzer, CancellationToken } from "motely-wasm";

await bootsharp.boot();

JamlConfigLoader.check(text);                       // null, or "JAML line 3: ..."
const config = JamlConfigLoader.fromJaml(text);     // the engine's JamlConfig, as a plain object

const token = new CancellationToken();
const search = JamlSearchBuilder.createSettings(config)
  .withThreadCount(1)
  .withSequentialSearch()
  .withSeedMatchCallback((seed) => console.log(seed))
  .withSeedAnalyzeProvider(MotelyJamlyzer.createRiderDesc(config, (r) => console.log(r.antes)))
  .start(token);                                    // token.cancel() stops it
await search.waitForCompletionAsync();

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
  (`withSeedList` is the crossing form).
- The NativeAOT runtime gives a thrown C# exception to JS as "C# exception from NativeAOT",
  without its message. After any call throws (a filter that does not load, a rejected `with*`
  value, a `start` that fails), `Errors.last()` returns its message. `JamlConfigLoader.check`
  returns the loader's reason without throwing.

## Hosts

The package decodes its embedded assemblies with `Uint8Array.fromBase64`: Node 24+, Chrome 140+,
Safari 18.2+, Firefox 133+. An older host needs that function shimmed before `boot()`, for
example `Uint8Array.fromBase64 ??= (s) => new Uint8Array(Buffer.from(s, "base64"))` on Node.
No COOP/COEP headers and no SharedArrayBuffer are needed.

## Build

```
dotnet workload install wasm-tools
dotnet publish -c Release        # bin/motely-wasm, warnings are errors, nothing suppressed
node tests/smoke.mjs
```

The engine's JAML loader reads with VYaml's parser only. VYaml's serializer layer finds
formatters by reflection, which NativeAOT cannot compile (the old IL2104 / IL3053).
