# Motely.Wasm / Bootsharp audit: next steps

> **Update 2026-10-16 — the naming regression in M1/N1 was resolved the other way.** The dialect
> is JAML again: `Motely.Wasm/README.md` and the JS exports are `JamlConfigLoader.fromJaml`,
> `JamlSearchBuilder`, `MotelyJamlyzer`, `JamlFiles`, `JamlConfig`, and `JamlFiles.cs` /
> `JamlFileNames.cs` hold `IJamlFiles` / `JamlFilesModule` / `JamlFileNames`. Read M1's fix
> direction as JAML, and N1 as done. The rest of this audit stands as of its date.

Audit date 2026-10-15. Read-only. `dotnet build D:\MotelyJAML\Motely.Wasm` (Debug): 0 warnings, 0 errors.
No Release publish was run, so nothing below about LLVM output (SIMD, Binaryen) was observed on a real artifact.

Doc paths are relative to `D:\bootsharp\`. Project paths are relative to `D:\MotelyJAML\`.

## 1. Summary

- Version: Motely.Wasm pins Bootsharp / Bootsharp.Inject 0.9.0 (`Directory.Packages.props:24-25`). The checkout documents 0.9.0
  (`src/cs/Directory.Build.props:4`, `src/cs/Bootsharp/Build/Bootsharp.props`). The installed NuGet package's `build/Bootsharp.props`
  and `PackageTemplate.json` match the checkout. No version drift.
- API usage matches the current docs. It uses `[Export]` / `[Import]` and `[assembly: Export(typeof(...))]`, `[RenameModule|Node|Member]`,
  `[SpecializeImport|Export]`, `AddBootsharp()` / `RunBootsharp()`, and a `Main` entry point. There is no deprecated or obsolete API
  (`[JSInvokable]`, `[JSFunction]`, `[JSEvent]` are not in the docs or sources).
- The csproj is minimal and correct. Configuration, threading and the package.json are right.
- The real problems are stale docs (README still uses the pre-rename JAML names) and two unverified claims about the Release artifact:
  that Binaryen runs in CI, and that SIMD actually reaches the LLVM output.
- Findings: 1 Must, 2 Should, 3 Nice.

## 2. Fixes

### MUST

#### M1. `Motely.Wasm/README.md` documents JS names that no longer exist

- **What is wrong:** The README's code sample and prose use the pre-rename names. The package now exports `YAMLConfigLoader`,
  `YAMLSearchBuilder`, `MotelyYAMLyzer`, `YAMLFiles` and `fromYAML`. Anyone copying the README gets `undefined`.
- **Evidence (project):** Stale names are at `Motely.Wasm/README.md:8, 12, 13, 16, 20, 25, 30, 32, 48, 71, 83`. Specifically `JamlConfigLoader`,
  `JamlSearchBuilder`, `MotelyJamlyzer`, `fromJaml`, `JamlConfig`, `JamlFiles` and "Jamlyzer rider". Actual exports are `Program.cs:125`
  (`YAMLConfigLoader`), `:145` (`YAMLSearchBuilder`), `:158` (`MotelyYAMLyzer`), `:17` (`IYAMLFiles` renamed to `YAMLFiles` at `:228`).
  `tests/engine.test.mjs:5-17` imports the YAML names.
- **Evidence (Bootsharp):** `docs/guide/declarations.md`, "Function Declarations" and "Namespaces". The JS node is the C# type name and
  members are camelCased, so `YAMLConfigLoader.FromYAML` becomes `YAMLConfigLoader.fromYAML`. `docs/guide/renaming.md`, "Node". The
  `I` strip for `IYAMLFiles` comes from the project's own `[RenameNode]`.
- **Fix:** Replace in README.md:
  - `JamlConfigLoader` -> `YAMLConfigLoader`
  - `JamlSearchBuilder` -> `YAMLSearchBuilder`
  - `MotelyJamlyzer` -> `MotelyYAMLyzer`
  - `fromJaml` -> `fromYAML`
  - `JamlConfig` -> `YAMLConfig`
  - `JamlFiles` -> `YAMLFiles`
  - "Jamlyzer" -> "YAMLyzer"

  Optionally lift the README sample into a `node --test` case. `tests/engine.test.mjs` already covers most of it.
- **Verify:** `grep -in jaml Motely.Wasm/README.md` returns nothing. Paste the README sample into a scratch `.mjs` next to `bin/motely-wasm`
  after a Release publish and run it with node.

### SHOULD

#### S1. The CI `wasm` job has no Binaryen (`wasm-opt`) step, so the shipped artifact may be unoptimized

- **What is wrong:** On a Release publish Bootsharp runs `wasm-opt ... -O3` and, when it is missing, only logs a warning and continues with a
  non-fully-optimized WASM. The `wasm` job (which produces the artifact the `web` job and Motely.Web consume) installs the dotnet
  workload and node but never Binaryen. The README tells developers to install it, but CI does not.
- **Evidence (project):** `.github/workflows/ci.yml:56-82` (steps: checkout, setup-dotnet, setup-node, nuget add, `workload install wasm-tools`,
  `npm ci`, `dotnet publish -c Release`, `npm test`; no binaryen). `Motely.Wasm/README.md:60-62` ("Install Binaryen... Bootsharp runs it").
  `grep -i "wasm-opt\|binaryen" .github/workflows/*.yml` returns nothing.
- **Evidence (Bootsharp):** `docs/guide/llvm.md`, "Binaryen" (steps 1-3: install, put `wasm-opt` on PATH, otherwise a warning and a
  non-fully-optimized binary). `src/cs/Bootsharp/Build/Bootsharp.targets:111-119` (the `Exec wasm-opt` and `Warning` tasks, with
  `IgnoreExitCode=true`).
- **Fix (CI):** In the `wasm` job, before `dotnet publish -c Release`, add a step that puts Binaryen on PATH. Either:
  ```yaml
  - run: sudo apt-get update && sudo apt-get install -y binaryen
  ```
  or download a release tarball from https://github.com/WebAssembly/binaryen/releases (the page `llvm.md` links) and add its `bin` to
  `$GITHUB_PATH`. The apt package may lag; prefer the release tarball since the command uses `--all-features`.
- **Note:** `TreatWarningsAsErrors` (`Directory.Build.props:12`) is a compiler (csc) property. It does not obviously promote MSBuild
  `<Warning>` task output, so do not assume a missing `wasm-opt` fails CI. That is why the failure mode is silent.
- **Verify:** Open the latest `wasm` job log and search for "Failed to run Binaryen optimization". After the fix it is absent. Compare
  `bin/motely-wasm/*.wasm` size before and after, or run the job with `wasm-opt --version` as a preceding step.

#### S2. Prove that SIMD reaches the Release (LLVM) binary, because the engine depends on it

- **What is wrong:** Nothing in Motely.Wasm sets, checks, or tests SIMD. It relies on Bootsharp's default. That is probably fine for the
  Debug (Mono) path, but the Release path uses NativeAOT-LLVM, and the docs say nothing about SIMD there. Motely uses `Vector128/256/512`
  in 36 files under `Motely/`. If the LLVM output falls back to scalar code, the browser engine is much slower than expected and no test
  would notice, since correctness tests (`tests/engine.test.mjs`) pass either way.
- **Evidence (project):** `Motely.Wasm/Motely.Wasm.csproj:3-14` (no `WasmEnableSIMD`). `grep -rl "Vector256\|Vector512\|Vector128" Motely
  --include=*.cs` lists 36 files.
- **Evidence (Bootsharp):** `src/cs/Bootsharp/Build/Bootsharp.props:10` (`<WasmEnableSIMD>true</WasmEnableSIMD>` is set for every build). `docs/guide/llvm.md`,
  "Setup" (Release auto-enables NativeAOT-LLVM and speed-focused codegen; SIMD is not mentioned). The only other SIMD signal in the installed
  package is the opposite: `~/.nuget/packages/bootsharp/0.9.0/llvm/microsoft.dotnet.ilcompiler.llvm/build/DotNetJsApi.targets:68`
  (`<EmscriptenEnvVars Include="WASM_ENABLE_SIMD=0" />`). That is a flag for the generated dotnet.js glue, not necessarily codegen, but it
  shows the setting is not self-evidently honored. `samples/bench/dotnet/DotNet.csproj:9` sets `WasmEnableSIMD` only for the Mono bench.
- **Fix:** Not a csproj edit (the property is already true and redundant to re-set). Add a check:
  1. After `dotnet publish -c Release`, inspect the wasm for SIMD opcodes, for example
     `wasm-dis bin/motely-wasm/*.wasm` (Binaryen) piped to a count of `v128.` / `i32x4.` / `i8x16.` instructions, or
     `wasm-objdump -d` (wabt). If the count is zero, SIMD is not in the LLVM output.
  2. Add a node timing smoke test (a fixed `withSeedList` or small sequential slice) and record seeds/sec in CI output so a regression to
     scalar is visible.
  3. Only if the binary has no SIMD: open an issue against Bootsharp / NativeAOT-LLVM. Do not invent a property; none beyond
     `WasmEnableSIMD` exists in the sources.
- **Verify:** The opcode count in step 1 is greater than zero, and seeds/sec on the Release build is within the expected range of the
  Debug/Mono and native numbers.

### NICE

#### N1. File names still say Jaml after the rename

- **What is wrong:** `Motely.Wasm/JamlFiles.cs` holds `IYAMLFiles` / `YAMLFilesModule` and `JamlFileNames.cs` holds `YAMLFileNames`.
- **Evidence (project):** `Motely.Wasm/JamlFiles.cs:13, 57`, `Motely.Wasm/JamlFileNames.cs:14`. `Motely.Tests/Motely.Tests.csproj:35`
  links `../Motely.Wasm/JamlFileNames.cs`. `Motely.Tests/JamlFileNamesTests.cs` also still carries the name.
- **Evidence (Bootsharp):** None. This is a consistency item, not a Bootsharp rule. Listed as Nice for that reason.
- **Fix:** `git mv` the two files and update `Motely.Tests.csproj:35`. The working tree has uncommitted changes in
  `Motely.Tests/JamlFileNamesTests.cs`, so do this after committing them.
- **Verify:** `dotnet build Motely.slnx` and `dotnet test Motely.slnx` succeed.

#### N2. Redundant project properties

- **What is wrong:** `OutputType` and `AllowUnsafeBlocks` are set by the Bootsharp package for you.
- **Evidence (project):** `Motely.Wasm/Motely.Wasm.csproj:6, 8`.
- **Evidence (Bootsharp):** `src/cs/Bootsharp/Build/Bootsharp.props:4-5` (`OutputType=Exe`, `AllowUnsafeBlocks=true`). `docs/guide/getting-started.md`,
  "Configure C# Project" (only `TargetFramework`, `RuntimeIdentifier` and the package reference are required).
- **Fix:** Delete those two lines (keep `RuntimeIdentifier`; the docs require it). Harmless to leave; the explicit lines do document intent.
- **Verify:** `dotnet build Motely.Wasm` still passes with 0 warnings. Release publish unchanged.

#### N3. Consider sideloading the binaries for the web build

- **What is wrong:** By default Bootsharp embeds the runtime and assemblies in the JS module as base64. That is about 30 percent larger, and the
  README's host requirement (`Uint8Array.fromBase64`, Node 25+, Chrome 140+) exists because of that decode.
- **Evidence (project):** `Motely.Wasm/README.md` section "Hosts"; `Motely.Wasm.csproj` has no `BootsharpBinariesDirectory`.
- **Evidence (Bootsharp):** `docs/guide/sideloading.md` (lines 3, 5-19, 31) and `docs/guide/build-config.md` property table
  (`BootsharpBinariesDirectory`). `samples/trimming/cs/Trimming.csproj` sets it for minimum size.
- **Fix:** For the web target, set `<BootsharpBinariesDirectory>` (for example under Motely.Web `public/bin`) and call
  `bootsharp.boot("/bin")`. Keep embedded for Node/CLI use. This is a trade-off (extra files to serve); only do it if bundle size matters.
- **Verify:** Compare the gzip/brotli size of the published module before and after. Boot in a browser with the network tab open.
  Whether sideloading also removes the `Uint8Array.fromBase64` requirement is not stated in the docs; test it in the target browsers.

## 3. Correct and should be left alone

- **Package reference and versions:** Bootsharp, Bootsharp.Inject and Microsoft.Extensions.DependencyInjection are listed without versions
  and pinned centrally (`Directory.Packages.props:24-27`). This matches `docs/guide/extensions/dependency-injection.md`.
- **Project configuration:** `net10.0` plus `RuntimeIdentifier=browser-wasm` (`Motely.Wasm.csproj:4-5`) matches `docs/guide/getting-started.md`.
  `BootsharpName` and `BootsharpPackageDirectory` (`:9, :12`) are documented in `docs/guide/build-config.md`. Pointing the package.json copy at
  `obj/pkg` is a legitimate use of that property. The hand-written `package.json` (`exports`, `browser`, `type: module`) matches
  `src/cs/Bootsharp/Build/PackageTemplate.json` and the default output dir `bin/<BootsharpName>`.
- **Entry point and DI:** `Program.Main` builds a `ServiceCollection`, calls `AddBootsharp()`, registers the exported handler, then `RunBootsharp()`
  (`Program.cs:28-36`). This is the documented sequence (`dependency-injection.md`). `[assembly: Export(typeof(IYAMLFiles))]` (`Program.cs:17`)
  is the documented interface-module form (`interop-modules.md`).
- **Static `[Export]` forwarders for engine statics:** The engine cannot reference Bootsharp, and statics cannot be exported as modules
  ("non-static class" in `interop-modules.md`). `getting-started.md` shows static `[Export]` methods as the fallback. Keep them.
- **Optional parameters:** `int? engineCutoff = null`, `int eventRolls = 20` become optional TS parameters (`declarations.md`, "Default Arguments").
- **Renamers:** `[RenameModule]` / `[RenameNode]` / `[RenameMember]` have the documented signatures `(Type|MemberInfo, string @default)`, and returning
  null to erase a node or member is documented (`renaming.md`, "Node" and "Member"). The erasure of ref-struct and pointer members is a sound use.
- **Specializations:** `[SpecializeImport]` + `SpecializedImport(id)` and `[SpecializeExport]` + `SpecializedExport(it)` pairs follow
  `docs/guide/specialization.md`. Overriding `Unwrap` as `protected override` is right for a cross-assembly override.
- **Serialization shape:** Records cross by value and classes/interfaces by reference (`serialization.md`, `interop-instances.md`). `long` is
  `BigInt` (`serialization.md` table). `IReadOnlyList<T>` is accepted (`serialization.md`, "Collection Interfaces").
- **Threading and COOP/COEP:** The package builds single-threaded with no `WasmEnableThreads`. The only threaded Bootsharp sample sets
  `<WasmEnableThreads>true</WasmEnableThreads>` and COOP/COEP headers (`samples/react/backend/Backend.WASM/Backend.WASM.csproj:14`,
  `samples/react/vite.config.ts` server headers). Not enabling it is consistent. The engine already special-cases the browser:
  `Motely/MotelySearch.cs:1096` (`_workerCount = IsBrowser ? 1 : ...`) and `:1260` (cooperative pump), so a missing `withThreadCount(1)` does not
  spawn threads.
- **Globalization:** Not overridden, so Bootsharp's invariant default applies (`build-config.md`, "Globalization"). Correct for a YAML engine.
- **Disposal and blocking APIs:** Erasing `AwaitCompletion` (`Program.cs:244`) and exposing `waitForCompletionAsync` is consistent with a
  single-threaded host.
- **Boot order for the file system:** `fs.init(...)` before `await bootsharp.boot()` (`Motely.Web/src/main.tsx:10-11`, `tests/files.test.mjs:21-22`)
  matches `docs/guide/extensions/file-system.md` and the rule that imports must be assigned before boot (`declarations.md`).
- **Release defaults:** The project does not override trimming, AOT or LLVM settings (only `UseSystemResourceKeys=false`, which wins over
  Bootsharp's `true` as a project-level property). Leave as is. `docs/guide/getting-started.md` says Release auto-enables LLVM, aggressive trimming and Binaryen.

## 4. Unverified / opinions

- **Exception handling workaround (`Guard`, `Errors.Last`, `MotelyErrors.Thrown`, `Program.cs:39-123`).** No Bootsharp doc or source in the checkout
  discusses exception messages crossing to JS (grep for "exception" in `docs/guide` finds nothing relevant), so the claim that NativeAOT
  hands JS a message-less "C# exception from NativeAOT" is not documented there. The workaround may be necessary. If Bootsharp or the
  LLVM runtime gains a documented mechanism, prefer it. `Errors.LastMessage` (`Program.cs:105`) is an unsynchronized static; fine
  single-threaded, but it would race if threads were ever enabled.
- **NuGet / npm pairing for the file-system extension.** `Bootsharp.FileSystem` is `2026.4.30.1559` (`Directory.Packages.props:26`, and the only
  version in `~/.nuget/packages/bootsharp.filesystem`) while `@rewaffle/bootsharp-file-system` is `2026.5.201827` (`Motely.Wasm/package.json:37`,
  `Motely.Web/package.json:14`). `docs/guide/extensions/file-system.md` shows both installed with `Version="*-*"` / `latest` but states no pairing
  rule. A skew of three weeks may be harmless; confirm with the rewaffle sponsor repo before bumping either side.
- **`fs.init` call shape.** `file-system.md` shows `fs.init(Bootsharp.FileSystem.FileMounter)` imported from the root module. The project uses
  `fs.init(IFileMounter)` from `motely-wasm/bootsharp/file-system` (`Motely.Web/src/main.tsx:4, 10`) and `fs.init(IFileMounter, 0)`
  (`tests/files.test.mjs:21`). The tests pass, so the project form works; the doc example looks stale (older naming). The second
  argument is not documented in the checkout.
- **Whether NativeAOT-LLVM supports threads or SIMD** at all is not stated in the docs (`llvm.md` is silent). See S2 for how to measure SIMD.
- **`Uint8Array.fromBase64` as the only reason for the host floor** (README "Hosts") is not covered by Bootsharp docs.
- **Version duplication:** `Motely.Wasm/package.json:3` (`30.0.0`) duplicates `MotelyVersion` (`Directory.Build.props:7`). Easy to drift; consider
  generating one from the other.
- **Outside Bootsharp, noticed in passing:** `Directory.Packages.props:17-19` still lists `ModelContextProtocol.Core` and `Npgsql` under a
  "Motely.MCP" comment, and no `.csproj` references them (checked with grep). These look like leftovers from the removed MCP project.
  They are harmless under central package management but can be deleted.
- **Not exercised:** I did not run a Release publish, so I have not confirmed that it succeeds with the current rename, or what the LLVM
  artifact contains.

## 5. Docs and samples read

Docs (all under `D:\bootsharp\docs\`):
- `index.md`
- `guide/index.md`
- `guide/getting-started.md`
- `guide/build-config.md`
- `guide/declarations.md`
- `guide/serialization.md`
- `guide/interop-modules.md`
- `guide/interop-instances.md`
- `guide/renaming.md`
- `guide/specialization.md`
- `guide/sideloading.md`
- `guide/llvm.md`
- `guide/extensions/dependency-injection.md`
- `guide/extensions/file-system.md`

Not fully read: `D:\bootsharp\README.md` (feature list only). `guide/*` pages not listed above do not exist in this checkout. There is no CHANGELOG.

Samples and sources:
- `samples/minimal/cs/Minimal.csproj`
- `samples/minimal/main.mjs`
- `samples/react/backend/Backend.WASM/Backend.WASM.csproj`
- `samples/react/backend/Backend.WASM/Program.cs`
- `samples/react/src/main.tsx`
- `samples/react/vite.config.ts`
- `samples/trimming/cs/*.csproj`
- `samples/trimming/README.md`
- `samples/bench/dotnet-llvm/DotNetLLVM.csproj`
- `src/cs/Bootsharp/Build/Bootsharp.props`
- `src/cs/Bootsharp/Build/Bootsharp.targets`
- `src/cs/Directory.Build.props`
- `src/cs/.scripts/llvm.sh`

Installed package (for the version check):
- `~/.nuget/packages/bootsharp/0.9.0/build/*` and `llvm/.../DotNetJsApi.targets`

Project files read:
- `Motely.Wasm/Motely.Wasm.csproj`
- `Motely.Wasm/Program.cs`
- `Motely.Wasm/Specializations.cs`
- `Motely.Wasm/JamlFiles.cs`
- `Motely.Wasm/JamlFileNames.cs`
- `Motely.Wasm/package.json`
- `Motely.Wasm/.npmrc`
- `Motely.Wasm/README.md`
- `Motely.Wasm/tests/engine.test.mjs`
- `Motely.Wasm/tests/files.test.mjs`
- `Motely.Wasm/tests/handles.mjs` (head)

Consumers and repo config:
- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`
- `nuget.config`
- `Motely.slnx`
- `Motely/Motely.csproj`
- `Motely/MotelySearch.cs` (threading sections)
- `Motely.Tests/Motely.Tests.csproj`
- `Motely.Web/src/main.tsx`
- `Motely.Web/package.json`
- `.github/workflows/ci.yml`
