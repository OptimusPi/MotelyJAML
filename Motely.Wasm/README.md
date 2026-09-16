# Motely.Wasm

Two hosts. Motely.dll has no Bootsharp.

`import { Search, Analyze } from "motely-wasm"`

**Jimmolate** is `MotelyIndividualSeedSearcher`: JS defines `(ctx) => score` and **binds it before `boot()`**. `ctx` is the live `MotelySingleSearchContext` (specialization). Not a seed string.

```js
Search.jimmolate = (ctx) =>
  ctx.getAnteFirstVoucher(1) === /* MagicTrick */ 1 ? 1 : 0;
await bootsharp.boot();
await Search.jimmolateList(["ALEEB", "PIROCKS"]);
```

`Search.scoreList(jaml, seeds)` is JAML list search. `Analyze.seeds(jaml)` is Jamlyzer. Both take JAML text — `JamlConfig` is a class and does not cross.

**The seed analyzes itself.** `Search.settings(jaml).withAnalysis(eventRolls)` makes the engine run the Jamlyzer on every find in the same pass: each `Search.onScored` seed is followed by its full `MotelyJamlyzerSeedResult` on `Search.onAnalyzed`, same `score` and `tally`, every ante's boss/voucher/tags/shop/packs. `eventRolls: 0` is the per-ante summary only (no roll queues); `20` is what `Analyze.seeds` uses. Don't call `Analyze.seeds` for a seed the search just handed you — that is a second crossing for data the engine already had in hand.

```js
Search.onAnalyzed = (r) => (breakdown[r.seed] = r);
await Search.settings(jaml).withAnalysis(0).withSeedList(seeds).start(token);
```

`[RenameModule] => index`. `[RenameNode]` erases `Boot`, `Names`, specialization Import/Export proxies. `Motely*` enums emit without the prefix in TS (`Joker`, `TarotCard`, `SpectralCard`, …). Engine enum names stay.

```sh
dotnet publish Motely.Wasm/Motely.Wasm.csproj -c Release
```

**Always `-c Release`.** Release is NativeAOT-LLVM. `-c Debug` is Mono: fat, slow, not the module.
