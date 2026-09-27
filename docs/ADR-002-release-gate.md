# ADR-002: motely-wasm release gate — one version, one API baseline, one README source

**Status:** Accepted
**Date:** 2026-09-27
**Deciders:** Nat (pifreak)

## Context

- `motely-wasm` is published from `Motely.Wasm/` with `npm publish`; `prepublishOnly` ran `dotnet publish -c Release` plus the base64 polyfill patch and nothing else.
- The version was written by hand in two places, `<MotelyVersion>` in `Directory.Build.props` and `"version"` in `Motely.Wasm/package.json`. Git history shows them bumped separately (`2356c6b` to 26.0.3, `e1ed28e`, `c6c1d50` to 26.1.0); a sync script that once existed (`7ed6e8c`, 23.3.0) was lost.
- The published API had already been rewritten under a minor bump once: 25.0.3 → 25.1.0 removed six namespaces (`MotelyJaml.fromJaml/validate/validateLine/canonicalizeLine`, `MotelySearch.searchList/collect/findOne/searchSequential`, `MotelyUtilities.*`, `MotelyLsp.*`), 55 names in all, and replaced them with `Analyze`/`Search`. Downstream code (seedfinder.app) was written against a README that still described the old surface.
- Master at `09a0f37` carries the next one: commit `28478ca` (`Names.Node`) strips the `Motely` prefix from every enum in TS, so the next build renames all 17 published enums (`MotelyDeck` → `Deck`, …) relative to npm latest 26.0.2, while `MotelyVersion` says 26.1.0 — a minor bump. `Motely.Wasm/README.md` still documented `Search.scoreList`, removed in 26.0.0.
- `jaml-lang` (the editor grammar) is a separate repo that must publish from the same engine version, and nothing said which commit that was.

## Decision

Four pieces, wired into `prepublishOnly = build && version:check && api:check && readme:check`. All plain Node ESM under `Motely.Wasm/scripts/`, no new dependencies.

1. **One version.** `<MotelyVersion>` in `Directory.Build.props` is the only place a version is typed. `scripts/version-sync.mjs` writes it into `Motely.Wasm/package.json` (and `tests/package.json` if that ever carries a version); `--check` fails the publish when they disagree. Hand-editing `package.json`'s version is now pointless.
2. **API baseline diff.** After the build, `scripts/api-check.mjs` parses `bin/motely-wasm/generated/modules/index.g.d.mts` (shared parser `scripts/api-surface.mjs`: namespaces and their functions/consts/events, interfaces, classes, enums, exported types) and diffs it against the last *published* typings: `npm view motely-wasm dist-tags.latest` → `npm pack`, cached in `Motely.Wasm/.api-baseline/` (gitignored), with the checked-in `Motely.Wasm/api-baseline.json` for offline runs (`--offline`, or automatic fallback when npm is unreachable). A removed or signature-changed name without a major bump is `exit 1` naming every symbol; an added name under a patch-only bump is a warning; a version not above the published one is `exit 1`. It prints a status table. `npm run api:baseline` refreshes `api-baseline.json` from npm after a publish.
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
6. [ ] After the next publish: `npm run api:baseline` and commit `api-baseline.json`.
