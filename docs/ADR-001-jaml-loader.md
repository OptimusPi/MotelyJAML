# ADR-001: Motely YAML — one loader, text in, config out, files at the edge

**Status:** Accepted
**Date:** 2026-09-22
**Deciders:** Nat (pifreak)

## Context

- Filters are plain YAML. `.jaml`, `.yaml`, `.yml` and `.json` must all load. JSON is valid YAML, so it needs no separate path.
- Motely has to run in three places: native CLI and workers (JIT or NativeAOT), and the browser (`Motely.Wasm`, NativeAOT-LLVM via Bootsharp). Anything that needs runtime code generation dies in the browser.
- Browser files come through **Bootsharp.FileSystem** (the File System Access API). The core library must not know it exists: Motely.dll has no Bootsharp.
- Status on 2026-09-22, measured on 50 real filters from `JamlFilters/`: 2/50 loaded under JIT and 0/50 under NativeAOT. There were two causes:
  1. VYaml's generated deserializer writes `null` into every list the file leaves out (`mustNot`, `seeds`…). The engine then crashes on `config.MustNot.Where(...)`.
  2. VYaml's built-in resolver creates `EnumAsStringFormatter<MotelyDeck>` / `ListFormatter<T>` with `MakeGenericType` at runtime, which NativeAOT and WASM can't do ("missing native code").

## Decision

Three layers. Each one knows only the layer below it.

```
 Bootsharp.FileSystem (browser)      File.ReadAllText (native)
            │ bytes → UTF-8 text              │ text
            ▼                                 ▼
  Motely.Wasm/JamlFiles.cs           CLI / workers / tests
  list/load/save/delete/rename        JamlConfigLoader.FromFile(path)
  .jaml .yaml .yml .json
            │ text                            │ text
            └──────────────┬──────────────────┘
                           ▼
        Motely/Filters/Jaml/JamlConfigLoader.FromJaml(text)   ← the only parser
        VYaml document + JamlClauseFormatter for clauses
        normalizes missing lists to []
                           ▼
                      JamlConfig  → JamlSearchBuilder → engine
```

1. **One parser:** `JamlConfigLoader.FromJaml(string)` (plus `FromFile(path)` and `TryLoad`). No second parser exists: no hand-rolled string parsing and no separate JSON path.
2. **AOT-safe by construction:**
   - Every generic VYaml formatter the root document needs (`EnumAsStringFormatter<MotelyDeck>`, `<MotelyStake>`, `ListFormatter<IJamlClause>`, `ListFormatter<string>`) is named in the resolver, so the compiler emits it.
   - `JamlClauseFormatter` reflects over clause types. `Motely.dll` embeds `ILLink.Descriptors.xml` (`preserve="all"`), so the trimmer, ILC and NativeAOT-LLVM keep every type and property it touches. Array creation uses `Array.CreateInstanceFromArrayType`, which needs no dynamic code.
3. **Files live at the edge only:**
   - `JamlFiles` (Motely.Wasm) reads bytes through Bootsharp.FileSystem and hands text to the loader.
   - `JamlConfig` never crosses to JS (it's a class; Bootsharp would pass it as a live instance). JS always passes text: `Search.settings(await JamlFiles.load(name))`.
   - Name rule: `.jaml` names drop the extension (back-compat: `sub/filter`), while `.yaml` / `.yml` / `.json` keep theirs (`sub/filter.json`). A bare name means `.jaml`.
4. **Editor check:** `Jaml.check(text)` returns `null` or the loader's message, which names the line: `JAML line 20: \`8-1\` is a descending range; write \`1-8\` (key \`antes\`)`. (`antes: 1-8` itself loads: range shorthand landed with the revisit below.)

## Options Considered

### A. VYaml + explicit formatters + embedded trim descriptor (chosen)
| Dimension | Assessment |
|---|---|
| Complexity | Low: ~40 changed lines |
| Cost | ~0 (VYaml already referenced) |
| AOT/WASM | Verified: NativeAOT linux-x64 45/50, same as JIT |
| Familiarity | Existing code, existing clause formatter |

**Pros:** works today; one parser; zero Motely build or AOT warnings.
**Cons:** keeping all of Motely makes the trimmer keep ~570 KB of IL it might otherwise drop; the clause formatter still uses reflection (kept alive by the descriptor rather than the compiler).

### B. Fully source-generated clause reading (VYaml `[YamlObject]` per clause)
| Dimension | Assessment |
|---|---|
| Complexity | Medium–High: every clause/source type annotated, every list re-normalized |
| AOT/WASM | Ideal in principle |
| Measured | The rewrite that appeared on disk 2026-09-22: 21/50 JIT, 2/50 AOT (same null-list bug inside each clause; `luck: 5` stopped parsing) |

**Pros:** no reflection, smaller trimmed output.
**Cons:** broke more than it fixed as written. Saved as `Claude outputs/JamlClauseFormatter.sourcegen-attempt.cs.txt` to revisit.

### C. Hand-rolled parser
Rejected. This is what `MotelyYAML` was, and it was deleted for exactly that reason.

### D. YamlDotNet
Rejected. It's reflection-heavy and not trim-safe, which is the reason VYaml was chosen (see `Directory.Packages.props`).

## Trade-off Analysis

A ships now and is proven under NativeAOT against real filters. B is the cleaner end state, but only once every clause type normalizes its own nulls. Moving A → B later doesn't change the public surface (`FromJaml(text)`), so nothing downstream moves.

## Consequences

- **Easier:** any host (CLI, worker, browser, tests) loads any filter format through one call. The browser gets `.yaml`/`.yml`/`.json` from a mounted folder with no JS changes.
- **Harder:** adding a new root-level enum or list type to `JamlConfig` means adding its formatter to the resolver list in `JamlConfigLoader`, or WASM breaks. The AOT harness below catches it.
- **Revisit:** Option B. ~~`antes: 1-8` range shorthand~~ — done 2026-09-27: `JamlClauseFormatter.ExpandRanges` expands `1-8`, `1..8`, `1 to 8` (case-insensitive) for every `int[]` key, alone or inside a list (`[1-3, 7]`); a descending range is `JAML line n: \`8-1\` is a descending range; write \`1-8\` (key \`antes\`)`; ranges expand to at most 1024 values per key (the widest real key, antes, is 0-39), so `1-999999999` and a list of many `0-1023` items are errors, not allocations. Tests: `Motely.Tests/JamlAnteRangeTests.cs`. Release gating for the package that ships it: ADR-002.

## Action Items

1. [x] `JamlConfigLoader`: normalize missing lists; explicit AOT formatters; `FromFile`.
2. [x] `JamlClauseFormatter`: AOT-safe array creation; trim suppressions tied to the descriptor.
3. [x] `Motely/ILLink.Descriptors.xml` embedded via `Motely.csproj`.
4. [x] `Motely.Wasm/JamlFiles.cs`: `.jaml .yaml .yml .json`, back-compatible names.
5. [x] `Motely.Wasm/Jaml.cs`: `Jaml.check(text)`.
6. [ ] On the PC: `dotnet build Motely.slnx -c Release` + `dotnet test`. The cloud build couldn't restore the sponsor-feed package or see concurrent edits.
   - 2026-09-27, cloud, SDK 10.0.301: `Motely.slnx` restores from nuget.org alone (Bootsharp.FileSystem is referenced only by Motely.Wasm, which is not in the solution). Build: 0 warnings, 0 errors. Test: 495/495 after the fixes below (master was 491/492: `JamlWildcardTests.AliasSyntaxIsRejected`). Left open for the PC run.
   - 2026-09-27, cloud, later: 510/510 (Release) with `JamlLoaderLineNumberTests` (15) added; 523/523 (Release) at a7ab20c. Later commits add tests, so rerun `dotnet test Motely.slnx -c Release` for the current count rather than trusting a number here.
7. [ ] `dotnet publish Motely.Wasm/Motely.Wasm.csproj -c Release`, then `node tests/smoke.mjs`.
8. [x] Fix the 5 authoring errors (Zerkeo:19, ColaOopsLite:35, M.yml:19, faceding:43, simplCola:3). Fixed in the filter text. The loader first cited 20/36/20/44 for the first four: `JamlClauseFormatter` read VYaml's `CurrentMark` at a value scalar, after the tokenizer had already looked ahead to the next key. It now files a value under its key's line and a `- ` item under the mark from before it; `Motely.Tests/JamlLoaderLineNumberTests.cs` pins each of these shapes to its own line. Loading all 341 files turned up 4 more, also fixed: OopsPile_PerkeoCat, KittyDicetrick (`[0-7]` in a flow list), NegativePerkeoAnte3FirstArcana (nested map under `legendaryJoker:`), loki (`rank: K`).
9. [x] Add the load-every-filter check as a test: `JamlFilters/*` must load under the Release build. `Motely.Tests/JamlFilterCorpusLoadTests.cs` runs every .jaml/.yaml/.yml/.json in `JamlFilters/` and `Motely.Tests/JamlFilters/` through `FromFile`, enumerated at run time (160 and 182 files on 2026-09-27, after `bench-negative-perkeo.jaml` was added).
10. [x] `antes: 1-8` / `1..8` / `1 to 8` range shorthand on every `int[]` key (`JamlClauseFormatter.ExpandRanges`, `JamlAnteRangeTests`). Written 2026-09-27 without a dotnet SDK at hand, then compiled in the cloud (SDK 10.0.301) on `claude/trusting-galileo-ex3005` next to the line-number work in the same formatter: it compiled clean; two `JamlLoaderLineNumberTests` used `1-8` as their bad value and now use `8-1` / `1-8-9` for the same lines; a range is capped at 1024 values so `1-999999999` is an error, not an allocation. `dotnet test Motely.slnx -c Release`: 544/544 at 22d0a10. Commit d97b725's body still says NOT COMPILED; that was true when it was written and is superseded by this item (history is not rewritten).
    - 2026-09-27, release-gate review: the cap moved from per range to per key (`[0-1023, 0-1023, …]` passed the per-range check and still allocated 1024 nodes per item), and `2147483647-2147483647` no longer hangs (the `v <= to; v++` counter wrapped at int.MaxValue; the loop now counts the span). Both reproduced as failing tests first. 549/549 at bbca991 + the gate-script commits.

## Verification (2026-09-22, cloud, .NET 10.0.401)

| Build | Before | After |
|---|---|---|
| JIT, 50 real filters | 2 / 50 | 45 / 50 |
| NativeAOT linux-x64, same 50 | 0 / 50 | 45 / 50 |
| Motely Release build warnings | – | 0 |
| NativeAOT Motely warnings | 8 errors | 0 |

The remaining 5 are authoring errors, each reported with its line number.
