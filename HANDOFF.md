# Handoff — MotelyJAML, 2026-10-25 (MCP for first-time agents)

## TL;DR

The MCP server now works for an agent that knows nothing about Motely. A cold Haiku agent
went from ~32 calls with silent dead ends to ~12 calls with zero errors on the same task set.
908/908 tests. No engine change that reaches the browser, so **no motely-wasm release**
(31.0.0 is still current).

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
- Unmerged remote branches, left alone pending the owner's call: `feature/jaml-shorthand-parser`
  (doesn't compile against current YamlDotNet: CS0535) and `feature-yaml` (74k-line deletion).

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
