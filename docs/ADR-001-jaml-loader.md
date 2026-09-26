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
4. **Editor check:** `Jaml.check(text)` returns `null` or the loader's message, which names the line: `JAML line 20: \`1-8\` is not an integer (key \`antes\`)`.

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
- **Revisit:** Option B; `antes: 1-8` range shorthand (~10 lines in `JamlClauseFormatter.Convert`).

## Action Items

1. [x] `JamlConfigLoader`: normalize missing lists; explicit AOT formatters; `FromFile`.
2. [x] `JamlClauseFormatter`: AOT-safe array creation; trim suppressions tied to the descriptor.
3. [x] `Motely/ILLink.Descriptors.xml` embedded via `Motely.csproj`.
4. [x] `Motely.Wasm/JamlFiles.cs`: `.jaml .yaml .yml .json`, back-compatible names.
5. [x] `Motely.Wasm/Jaml.cs`: `Jaml.check(text)`.
6. [ ] On the PC: `dotnet build Motely.slnx -c Release` + `dotnet test`. The cloud build couldn't restore the sponsor-feed package or see concurrent edits.
7. [ ] `dotnet publish Motely.Wasm/Motely.Wasm.csproj -c Release`, then `node tests/smoke.mjs`.
8. [ ] Fix the 5 authoring errors (Zerkeo:20, ColaOopsLite:36, M.yml:20, faceding:44, simplCola:3).
9. [ ] Add the load-every-filter check as a test: `JamlFilters/*` must load under the Release build.

## Verification (2026-09-22, cloud, .NET 10.0.401)

| Build | Before | After |
|---|---|---|
| JIT, 50 real filters | 2 / 50 | 45 / 50 |
| NativeAOT linux-x64, same 50 | 0 / 50 | 45 / 50 |
| Motely Release build warnings | – | 0 |
| NativeAOT Motely warnings | 8 errors | 0 |

The remaining 5 are authoring errors, each reported with its line number.
