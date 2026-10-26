# Handoff — MotelyJAML, 2026-10-25 (MCP, JAML shorthand + dialect, motely-wasm 31.1.0)

## TL;DR

JAML reads the way the owner writes it, the MCP server works for agents that know nothing
about Motely, and **motely-wasm 31.1.0** is tagged (`v31.1.0`, notes in
docs/RELEASE-31.1.0.md). 1042/1042 xunit, wasm node:test 27/27, NativeAOT-LLVM publish clean.

- **Shorthand** (`Motely/Filters/JamlShorthand.cs`): `- Perkeo in ante 1 or 2`,
  `- Negative Blueprint in antes 1-3 score 50`, bare `- Triboulet`. Each form loads to exactly
  its mapping twin (tests compare fields and search a fixed seed list with both).
- **Dialect** (from the owner's own filters in BalatroSeedOracle/YAMLFilters, 139 → 149 of 157
  load): `defaults: { antes, score }`, `ante:`, one number for a one-item list, `requireMega:`,
  `soulJoker:`, `dateCreated:` (kept as metadata), J/Q/K/A ranks, loose names in mappings.
  The 8 that still fail are real mistakes or pre-JAML formats; each error names the fix.
- **Loader errors** are one line in JAML terms ("'antez' is not a key of a `joker` clause; did
  you mean `antes`?"). A clause with no clause key used to throw a NullReferenceException.
- **CI** had failed on every master push since before 31.0.0: the repo has no `REWAFFLE_TOKEN`
  secret and `wasm` ran `dotnet nuget add source` with an empty password. Now a `secrets-gate`
  job skips `wasm`/`web` with a warning when the token is missing. **To get the real wasm gate
  back, set `REWAFFLE_TOKEN`** (read:packages on rewaffle's GitHub Packages).
- **npm**: 31.1.0 is not on npm yet. This machine isn't logged in to npmjs (`npm whoami` →
  401). From Motely.Wasm: `npm login`, then `npm publish` (prepack runs the NativeAOT-LLVM
  build, ~1 min on Windows).
- **Binaryen**: use version 130 (131 at most). `X:\binaryen-version_130\bin` is on PATH and
  installed. Binaryen 132 added "compact imports" and writes that encoding whenever
  `--all-features` is on, which Bootsharp 0.9.0's targets hardcode; V8 (Node, Chrome) can't
  read it ("unknown import kind 0x7f"). `--disable-compact-imports` would fix it, but that
  flag can only go into Bootsharp's own targets. With 130 the Release publish runs wasm-opt
  and `npm test` is 27/27.

### Earlier the same day: MCP for first-time agents

A cold Haiku agent went from ~32 calls with silent dead ends to ~12 calls with zero errors on
the same task set.

- New tools: `filter_list` (what filters exist), `seed_analyze` (what a seed holds, `antes`
  to trim). Every filter-taking tool accepts inline `jaml` text, so agents can write filters
  without saving files on the server.
- Resources `jaml://guide` (primer + examples, tests validate them) and `jaml://schema`.
- `sequential` is now a window of whole batches (`startBatch`, `batches`, returns
  `nextBatch`): no overlap, no gaps. The old collect-stop resume repeated or skipped seeds.
- `FilterCheck.Warnings`: a boss asked for in an ante it can never appear in. Surfaces in
  `motely check`, `seed_validate` and `seed_search`'s note. It found that `boss-the-ox` and
  `boss-amber-acorn` could **never match** (The Ox starts at ante 6, showdown bosses are ante
  8 only); those and 7 other boss filters had their antes corrected.
- Removed the fake MCP Apps panel (`ui://motely/search`, its Run button did nothing) and the
  `ModelContextProtocol.Extensions.Apps` package. `seeds: "-"` (stdin) is refused on the server.
  Null fields are always serialized, matching the output schemas.
- Dead remote branches the owner OK'd deleting (the auto-mode classifier blocked the delete):
  `git push origin --delete feature/jaml-shorthand-parser feature-yaml` (tips fe8b39d7,
  82b7a156).

## Previous session (2026-10-24, Kimi)

MCP server + AG-UI endpoint audited and fixed; Motely.Web grew a **Server Agent** tab
(AG-UI client for `/agui`) with proper Balatro buttons and a pinned footer. All commits are
on `master` of `OptimusPi/MotelyJAML` (pushed). 881/881 tests, typecheck and vite build green,
panel verified in a real browser run (`search Zerkeo_Pure` → 313 seeds, live progress).

## Commits this session

| Commit | What |
|---|---|
| `57f34fcb` | MCP audit fixes: port 5200 (macOS pins 5000), `seed_validate` failures are real `isError`s, `/agui` mints thread/run ids, README lies fixed |
| `2dbfadb8` | The Server Agent panel: `src/agui.ts` (AG-UI SSE client), `src/AgentPanel.tsx`, tabs in `App.tsx`, `/agui` Vite proxy, AppHost `web → mcp` reference |
| `58665db8` | `Motely.Wasm/README.md`: the macOS build situation (see below) |
| `5e3ed421`-ish (UI) | Real `JimboButton`s everywhere in the panel (hover brightens, press sinks), blue Run / red Stop, compact blue seed buttons with click-to-copy, footer pinned to viewport bottom |

## Run it

```sh
dotnet run --project Motely.MCP        # MCP + AG-UI on http://localhost:5200/mcp, /agui
cd Motely.Web && npm install && npm run dev   # http://localhost:5173 (tabs: Seed Lab / Server Agent)
dotnet run --project Motely.AppHost    # or everything under Aspire
```

Filter edits: `dotnet run --project Motely.CLI -- check <filter>` (or `motely check`).
Tests: `dotnet test Motely.Tests` (build CLI/MCP/Tests individually; solution build needs the
rewaffle feed — see Auth).

## The auth chain (fixed, will need refresh someday)

- npm `@rewaffle/*` comes from GitHub Packages: token lives in `~/.npmrc`
  (`//npm.pkg.github.com/:_authToken=…`). When it 401s again:
  `gh auth refresh -h github.com -s read:packages`, then rewrite that line with `gh auth token`.
- NuGet `rewaffle` source (user-level) holds the same token, stored **clear-text** (macOS can't
  encrypt NuGet passwords — expected). `dotnet nuget update source rewaffle --username OptimusPi
  --password "$(gh auth token)" --store-password-in-clear-text`.

## motely-wasm on macOS (the big gotcha)

`dotnet publish` for Motely.Wasm **cannot run the AOT step on macOS**: Bootsharp 0.9 bundles
NativeAOT-LLVM host compilers for win/linux only, and `runtime.osx-arm64.Microsoft.DotNet.
ILCompiler.LLVM` exists on no public feed (the runtimelab-branch dance you remember). CI
publishes on ubuntu-latest and uploads `bin/motely-wasm` as an artifact.

**What this repo does instead:** `Motely.Wasm/bin/motely-wasm/` (gitignored) was copied from
the published npm package `motely-wasm@31.0.0` (identical version; `jaml/` types match). The
web app consumes it through the `file:../Motely.Wasm` dependency.

When the engine version bumps: `npm install motely-wasm@<new> --prefix /tmp/x` and copy
`node_modules/motely-wasm/bin/motely-wasm` into `Motely.Wasm/bin/`. Engine code changes don't
reach the browser until then (JAML filters are data — no rebuild needed).

## Known loose ends

- `OptimusPi/seedfinder-app` is an **empty stub** — intended home for the deployable MCP server?
  The MCP code currently only lives here (`Motely.MCP`). `seedfinder.app` (private) is the
  Claude-plugin sibling project.
- The agent emits one text message **per seed** (noisy on the wire); the panel folds them into
  buttons, but batching in `MotelySearchAgent` would be cleaner (watch the determinism tests).
- jaml-ui: the Balatro button spec (hover darkens 0.88, press sinks + darkens 0.75) is written
  into the repo's AGENTS.md/llms.txt and shipped in **jaml-ui 9.1.1** (tag `v9.1.1`, branch
  `v9-maintenance`; master is the unreleased v10 Base-UI rewrite). Motely.Web pins `^9.1.1`.
- Port 5173 is squatted by other projects sometimes (NoFog dev server was) — `PORT=<n>` env
  moves Vite; the `/agui` proxy follows automatically.
- `scratch/` and `assets/` at repo root are untracked leftovers from probing sessions.
