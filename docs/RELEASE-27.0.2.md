# motely-wasm 27.0.2

## Joker rarity is a typed enum in JS

`MotelyJokerRarity` now ships in the package. A joker's `MotelyItemType` value is
`category | rarity | index`, and the rarity bits are `MotelyJokerRarity`'s values, so rarity
needs no name lists:

```js
import { MotelyItemType, MotelyJokerRarity, MotelyJokers } from "motely-wasm";

MotelyJokers.rarity(MotelyItemType.Perkeo);              // MotelyJokerRarity.Legendary
MotelyItemType.Blueprint & MotelyJokerRarity.Legendary;  // same test, no call, no boot needed
```

`Legendary`'s bits are the whole rarity mask, so `item & MotelyJokerRarity.Legendary` gives the
rarity of any joker.

## JamlFiles is back, on by default

`JamlFiles` (a picked folder of filters, through Bootsharp.FileSystem) is in the default build
again. `-p:MotelyFileSystem=false` builds without it; the exports stay and `isSupported()` is false.

## If you are coming from 27.0.0

27.0.1 replaced the wrapper modules with the engine's own API. Nothing from 27.0.0's
`Search`, `Analyze` or `Jaml` exists any more. `JamlFiles` is back in 27.0.2:

| 27.0.0 | 27.0.1 and later |
|---|---|
| `Jaml.check(text)` | `JamlConfigLoader.check(text)` |
| `Search.settings(jaml)` | `JamlSearchBuilder.createSettings(JamlConfigLoader.fromJaml(jaml)).withThreadCount(1).withQuietMode(true)` |
| `await search.start()` | `const s = settings.start(token); await s.waitForCompletionAsync()` |
| `search.cancel()` | `const token = new CancellationToken()`, pass it to `start(token)`, then `token.cancel()` |
| `Search.onScored` | `.withScoredResultCallback(r => …)` when `settings.seedScoreDesc` is set, otherwise `.withSeedMatchCallback(seed => …)`. Attach one, not both. |
| `Search.onProgress` | `.withProgressCallback(p => …)` |
| `withAnalysis(n)` + `Search.onAnalyzed` | `.withSeedAnalyzeProvider(MotelyJamlyzer.createRiderDesc(config, r => …, n))` |
| `search.error` | `Errors.last()`, read right after the call that threw |
| `totalSeedsSearched`, `matchingSeeds`, `elapsedMs`, … | same names, on the object `start()` returns |
| `Analyze.seeds(jaml)` / `seedsPaged(jaml, rolls, slots)` | `MotelyJamlyzer.analyze(JamlConfigLoader.fromJaml(jaml), rolls?, slots?)` |
| `Analyze.seedsResume(jaml, states, rolls, slots?)` | `MotelyJamlyzer.analyzeWithResumeFrom(config, states, rolls, slots?)` |
| `JamlFiles.*` | unchanged; call `fs.init(Bootsharp.FileSystem.FileMounter)` before `boot()` |

Callbacks are plain JS functions; `system.Action<T>` is only the TypeScript name for `(x: T) => void`.
