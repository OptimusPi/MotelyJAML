# ADR-002: motely-wasm release gate — one version, one API baseline, one README source

> **Superseded.** Motely.Wasm was rebuilt from the Bootsharp guide for 27.0.0 and none of the
> scripts below came back: no `version-sync`, `api-check`, `readme-gen` or dist patches. Today the
> version is typed twice, `<MotelyVersion>` in `Directory.Build.props` and `"version"` in
> `Motely.Wasm/package.json`, and the gate is the `wasm` job in `.github/workflows/ci.yml`, which
> publishes the package and runs `Motely.Wasm/tests/smoke.mjs`. The rest of this record is kept as
> history.

**Status:** Superseded (2026-10-04)
**Date:** 2026-09-27
**Deciders:** Nat (pifreak)

## Context

- `motely-wasm` is published from `Motely.Wasm/` with `npm publish`; `prepublishOnly` ran `npm run build` (`dotnet publish -c Release` plus the base64-polyfill and CancellationToken-export dist patches) and nothing else.
- The version was written by hand in two places, `<MotelyVersion>` in `Directory.Build.props` and `"version"` in `Motely.Wasm/package.json`. Git history shows them bumped separately (`2356c6b` to 26.0.3, `e1ed28e`, `c6c1d50` to 26.1.0); a sync script that once existed (`7ed6e8c`, 23.3.0) was lost.
- The published API had already been rewritten under a minor bump once: 25.0.3 → 25.1.0 removed six namespaces (`MotelyJaml.fromJaml/validate/validateLine/canonicalizeLine`, `MotelySearch.searchList/collect/findOne/searchSequential`, `MotelyUtilities.*`, `MotelyLsp.*`), 55 names in all, and replaced them with `Analyze`/`Search`. Downstream code (seedfinder.app) was written against a README that still described the old surface.
- Master at `09a0f37` carries the next one: commit `28478ca` (`Names.Node`) strips the `Motely` prefix from every enum in TS, so the next build renames all 17 published enums (`MotelyDeck` → `Deck`, …) relative to npm latest 26.0.2, while `MotelyVersion` says 26.1.0 — a minor bump. `Motely.Wasm/README.md` still documented `Search.scoreList`, removed in 26.0.0.
- `jaml-lang` (the editor grammar) is a separate repo that must publish from the same engine version, and nothing said which commit that was.

## Decision

Four pieces, wired into `prepublishOnly = test:scripts && build && version:check && api:check && readme:check` (`test:scripts` is `node --test` over `scripts/test/`, the gate's own tests). All plain Node ESM under `Motely.Wasm/scripts/`, no new dependencies.

1. **One version.** `<MotelyVersion>` in `Directory.Build.props` is the only place a version is typed. `scripts/version-sync.mjs` writes it into `Motely.Wasm/package.json` (and `tests/package.json` if that ever carries a version); `--check` fails the publish when they disagree. Hand-editing `package.json`'s version is now pointless.
2. **API baseline diff.** After the build, `scripts/api-check.mjs` parses `bin/motely-wasm/generated/modules/index.g.d.mts` (shared parser `scripts/api-surface.mjs`: namespaces and their functions/consts/events, interfaces, classes, enums, exported types) and diffs it against the last *published* typings: `npm view motely-wasm dist-tags.latest` → `npm pack`, cached in `Motely.Wasm/.api-baseline/` (gitignored), with the checked-in `Motely.Wasm/api-baseline.json` for offline runs (`--offline`, or automatic fallback when npm is unreachable). A removed or signature-changed name without a major bump is `exit 1` naming every symbol; an added name under a patch-only bump is a warning; a container that only *gained* members (an enum member appended at the end, an optional field on a type, a new interface/class member, a new namespace function) counts as added, not changed. A new *required* type field, an enum member inserted mid-list (Bootsharp enums carry no initializers, so every later member renumbers), or a `{…}` ↔ `Readonly<{…}>` wrapper change stays a change; a version not above the published one is `exit 1`. It prints a status table. `npm run api:baseline` refreshes `api-baseline.json` from npm after a publish.
3. **README from typings.** `scripts/readme-gen.mjs` renders the API section between `<!-- api:start -->` / `<!-- api:end -->` in `Motely.Wasm/README.md` from the same typings; `--check` fails the publish when the block is stale. Prose outside the markers is never touched.
4. **jaml-lang reminder.** `api:check` ends with `jaml-lang: publish jaml-lang from motelyjaml <MotelyVersion> @ <git rev-parse HEAD>`. The gate cannot publish that repo; it can say exactly what to publish from.

## Options Considered

### A. Post-build typings diff against npm latest, semver-enforced (chosen)
| Dimension | Assessment |
|---|---|
| Complexity | Low: ~600 lines of dependency-free Node across four scripts |
| Cost | One `npm pack` per publish (cached) |
| Coverage | Every exported TS name Bootsharp emits, which is exactly what consumers import |
| Verified | Against real tarballs: 25.0.3→25.1.0 fails (55 removed), 25.1.0→26.0.2 passes (major), simulated master→26.1.0 fails (17 enums) |

**Pros:** catches the failure that actually happened, twice; no hand-maintained list of "public API"; works offline.
**Cons:** the parser knows Bootsharp's emitter shape (line-oriented, brace-balanced); a very different emitter would need a parser update. `[RenameNode]` renames are reported as remove+add, which is the honest reading for a consumer.

### B. C#-side `[Export]` attribute audit (PublicAPI.Shipped.txt style)
| Dimension | Assessment |
|---|---|
| Complexity | Medium: Roslyn analyzer or reflection over Motely.Wasm.dll |
| Coverage | Misses what happens between C# and TS: `Names.Node` renames, Bootsharp camelCase, optional-parameter shapes |

**Cons:** the enum-prefix break lives entirely in `Names.Node`, which this would not see. Rejected.

### C. Semantic-release / conventional commits computing the version
Rejected. The version is an operator decision here, and commit messages in this repo are not a reliable signal (`ok`, `:|`).

### D. TypeScript compiler API to diff declaration files
Rejected for now: adds a devDependency to a package that has none, for a file whose shape the 160-line parser reads fully. Revisit if Bootsharp's output changes shape.

## Consequences

- **Easier:** a publish that would break a consumer stops in `prepublishOnly` with the symbol names on screen. The README can no longer describe an API that does not exist. Bumping the version is one edit.
- **Harder:** the first Release build on the PC after this ADR will fail both `api:check` (17 enum renames vs 26.0.2 under 26.1.0 — bump to 27.0.0 or revert `Names.Node`) and `readme:check` (block generated from 26.0.2 typings; run `npm run readme:gen`). That is the gate working, not a bug in it.
- **Harder:** after every publish, run `npm run api:baseline` so the offline baseline matches npm; without it, offline runs compare against an older release (still correct, just noisier).
- **Revisit:** whether `[RenameNode]`-style renames deserve a dedicated "renamed" row instead of remove+add; publishing `jaml-lang` from this gate if the two repos ever merge.

## Action Items

1. [x] `scripts/version-sync.mjs` + `version:sync` / `version:check`.
2. [x] `scripts/api-surface.mjs`, `scripts/api-check.mjs` + `api:check` / `api:baseline`; `api-baseline.json` from 26.0.2; `.api-baseline/` gitignored.
3. [x] `scripts/readme-gen.mjs` + `readme:gen` / `readme:check`; markers in `Motely.Wasm/README.md`.
4. [x] `prepublishOnly` wired; jaml-lang reminder line.
5. [ ] On the PC: `npm run build` in `Motely.Wasm`, then `npm run api:check` — decide 27.0.0 vs reverting the enum rename — then `npm run readme:gen`.
   - 2026-09-27, cloud, after the member-level classifier: the same scratch typings still fail 17 removed + 7 changed, and `JamlAesthetic` stays breaking for a real reason this time: `Runs` sits at index 4, so `Step`..`Nsfw` each move up one value. Appending `Runs` after `Nsfw` in the C# enum would make it a plain addition. `SearchSettings` also loses `results`.
   - 2026-09-27, cloud: a scratch `dotnet publish` of Motely.Wasm at 22d0a10 (throwaway copy without `JamlFiles.cs` / Bootsharp.FileSystem, which only the sponsor feed serves) fed to `api-check.mjs --offline --typings …`: exit 1, `17 removed + 7 changed exported name(s) need a MAJOR bump (26.x -> 27.0.0)` — the 17 `Motely*` enums, `JamlAesthetic` (+`Runs`), `SearchSettings`, `MotelySingleSearchContext`, `MotelyItem`, `MotelyJamlyzerAnteResult/Pack/Pulls`. The same typings with `--version 27.0.0`: exit 0. `readme:check` on them: exit 1 (stale block, as predicted). Against the 26.0.2 typings all three checks exit 0.
6. [ ] After the next publish: `npm run api:baseline` and commit `api-baseline.json`.
7. [x] Gate without the sponsor feed. Bootsharp.FileSystem is opt-in (`MotelyFileSystem=true` / `MOTELY_FILESYSTEM=true`); `build` passes it, `build:no-fs` does not, and `JamlFiles` exports the same names either way (`isSupported()` tells them apart), so `api:check` and `readme:check` read the same surface from both. `.github/workflows/ci.yml` runs `test:scripts`, `version:check`, `build:no-fs`, the smoke test, `api-check --offline` and `readme:check` on every PR; `release.yml` calls it.
   - 2026-10-01, cloud: `api:check` against npm 26.0.2 on the `build:no-fs` typings: exit 0, `17 removed, 7 changed, 30 added; major bump allows it`.
8. [x] `scripts/patch-dist-exception-message.mjs` joins the dist patches: the NativeAOT-LLVM `dotnet.runtime.js` replaced every C# exception with `Error("C# exception from NativeAOT")` although `JSMarshalerArgument.ToJS(Exception)` writes the message into the slot (verified by decompiling the shipped System.Runtime.InteropServices.JavaScript.dll: `ToJS(ex.Message)`, then `Type = Exception`, `GCHandle` @4 clear of `IntPtrValue` @0 / `Length` @8). The patch reads it through the runtime's own string marshaller, which also frees it. Found by shape, so a runtime that changes it fails the build. `scripts/test/patch-dist-exception-message.test.mjs`; the smoke page asserts `Search.settings` throws `JAML line 3: ...`.
