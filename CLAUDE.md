# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build, test, run

.NET SDK is pinned to **10.0.301** in `global.json` (`rollForward: latestPatch`, no prerelease).
The solution file is `Motely.slnx` (XML solution format — there is no `.sln`).

```sh
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~JamlLineTests"      # one test class
dotnet test --filter "DisplayName~terse"                      # one test by name
dotnet run --project Motely.CLI -- --jaml JamlFilters/Chicot.jaml
dotnet run --project Motely.CLI -- --jaml <file> --collect 1
dotnet run --project Motely.CLI -- --help
```

`TreatWarningsAsErrors` is **true repo-wide** (`Directory.Packages.props`) — a new warning
fails the build, including `NU1603`.

The repo version is `MotelyVersion` in `Directory.Build.props`, *not* `Directory.Packages.props`.
Build.props is MSBuild's first import, so the value exists by the time `Version` is evaluated;
defining it in Packages.props evaluates too late and every assembly silently stamps `1.0.0`.

### WASM

`Motely.Wasm` is **excluded from the solution** and published separately:

```sh
dotnet publish Motely.Wasm/Motely.Wasm.csproj -c Release
```

**Always `-c Release`.** Release is NativeAOT-LLVM. `-c Debug` is Mono: fat, slow, and not the
module you want. Output lands at `Motely.Wasm/bin/motely-wasm`, package name `motely-wasm`
(`BootsharpName`). `BootsharpPackageDirectory` is parked in `obj` because Bootsharp's
`PackageTemplate.json` has no version and would overwrite the project's `package.json`.

### Language server

```sh
dotnet publish Motely.Lsp -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out
MOTELY_LSP_SERVER=out/Motely.Lsp.exe node Motely.Lsp/smoke-lsp.mjs
```

Releases are cut by tag: `git tag v26.0.0 && git push --tags`. `.github/workflows/release.yml`
gates on `dotnet test`, builds the LSP for win-x64/osx-arm64/linux-x64, drives each published
binary over real stdio with `smoke-lsp.mjs`, and attaches the tarballs to a GitHub Release that
the vscode extension's bootstrap downloads from.

## Architecture

Everything depends **inward** on `Motely`. One grammar — no second authoring table in editors.

| Concern | Where |
|---|---|
| Search / SIMD | `MotelySearch`, `MotelyVectorSearchContext.*` |
| Scalar search | `MotelySingleSearchContext.*` (Boss, Jokers, Packs, Shop, Tags, Tarot, Vouchers, …) |
| JAML grammar | `Motely/Filters/Jaml/` — FilterDesc owns the wire format; `JamlSchema` indexes |
| Native C# filters | `Motely/Filters/Native/` — run by name via `--native` |
| Seed providers | `Motely/SeedProviders/` (list, random, sequential, aesthetics) |
| PRNG streams | keyed streams; **order within a key is law** |

### JAML is source-generated

A clause type is a `FilterDesc` in `Motely/Filters/Jaml/` carrying `[JamlDiscriminator("voucher",
"vouchers", …)]`. `Motely.Generators/JamlGrammarGenerator.cs` — referenced as an `Analyzer`, not as
an assembly — reads those attributes and emits `JamlSchema.g.cs`. Adding a clause means adding the
FilterDesc plus its attribute; CLI parsing, LSP completion, and editor diagnostics follow from the
generated schema. Do not hand-maintain a parallel clause list.

Loading uses **VYaml** (source-generated, reflection-free) rather than YamlDotNet, specifically so
it survives the WASM trimmer.

`--jaml`, `--json`, and `--yaml` load the same filter bag; only JAML has terse one-liners.

### Scalar / vector parity

`MotelySingleSearchContext.*` and `MotelyVectorSearchContext.*` are parallel partial-class families
covering the same game surface. They must agree — `VectorScalarParityTests`,
`MotelyItemVectorParityTests`, `RawStreamParityTests`, and `VectorLuaRandomParityTests` exist to
catch drift. Change one side, change the other.

### Seed lake

Results persist to DuckDB lakes plus CSV/TXT under the results root (default `Seeds`, override with
`--results-path` or `MOTELY_DATALAKE_PATH`). `--drown` re-searches every seed ever saved across all
filters' lakes plus the JAML's own `seeds:` block, deduped. `--replay` (alias `--verify-seeds`)
verifies only that JAML's `seeds:` block.

### WASM boundary

`import { Search, Analyze } from "motely-wasm"`. Both take **JAML text** — `JamlConfig` is a class
and does not cross the boundary.

*Jimmolate* is `MotelyIndividualSeedSearcher`: JS defines `(ctx) => score` and **binds it before
`boot()`**. `ctx` is the live `MotelySingleSearchContext` specialization, not a seed string.

```js
Search.jimmolate = (ctx) => (ctx.getAnteFirstVoucher(1) === 1 ? 1 : 0);
await bootsharp.boot();
await Search.jimmolateList(["ALEEB", "PIROCKS"]);
```

`Search.settings(jaml).withAnalysis(eventRolls)` runs the Jamlyzer on every find in the same pass:
each `Search.onScored` seed is followed by its `MotelyJamlyzerSeedResult` on `Search.onAnalyzed`.
`eventRolls: 0` is the per-ante summary; `20` is what `Analyze.seeds` uses. Do **not** call
`Analyze.seeds` for a seed the search just handed you — that is a second boundary crossing for data
the engine already had.

## Projects

| Project | What it is |
|---|---|
| `Motely` | The engine. SIMD + scalar seed search, JAML grammar, filters. |
| `Motely.Generators` | Roslyn source generator producing `JamlSchema.g.cs`. Pinned to Roslyn 4.14 (the floor), not the SDK's, because it loads into whatever Roslyn the IDE host carries. |
| `Motely.CLI` | `dotnet run --project Motely.CLI --`. McMaster.CommandLineUtils. |
| `Motely.Wasm` | Bootsharp browser build. Two hosts: `Search`, `Analyze`. `Motely.dll` has no Bootsharp dependency. |
| `Motely.Lsp.Core` | `JamlLanguageService` — the analysis, host-independent. |
| `Motely.Lsp` | stdio JSON-RPC LSP server wrapping Lsp.Core. |
| `Motely.MCP` | MCP server exposing engine tools. |
| `Motely.DataLake` | DuckDB seed lake, CSV sinks, seed sources. |
| `Motely.DistributedWorker` | Pool/party client for distributed searching. |
| `Motely.JsonRender` | HTML/JSON report rendering. |
| `Motely.Schema` | Schema emitter. |
| `vscode-jaml` | VS Code extension `jaml-language-support` (publisher `pifreak`), bundles the released LSP binary and a `@jimbo` chat participant. |

`Motely.TUI/` contains `SearchWindow.cs` but **no `.csproj`** — it is not in the solution and does
not build.

## Repo notes

- `origin` is `OptimusPi/MotelyJAML`; `upstream` is `Tacodiva/Motely`, the project this forked from.
- `JamlFilters/*.jaml` are real filter definitions used as inputs and examples. Seed strings in
  them are base-32 and can spell things by accident.
