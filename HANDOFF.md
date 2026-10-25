# Handoff — MotelyJAML, 2026-10-24 (Kimi session)

## TL;DR

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

- `docs.motely.dev/mcp` is advertised in the MCP auth metadata but doesn't exist (README admits it).
- `OptimusPi/seedfinder-app` is an **empty stub** — intended home for the deployable MCP server?
  The MCP code currently only lives here (`Motely.MCP`). `seedfinder.app` (private) is the
  Claude-plugin sibling project.
- The agent emits one text message **per seed** (noisy on the wire); the panel folds them into
  buttons, but batching in `MotelySearchAgent` would be cleaner (watch the determinism tests).
- Port 5173 is squatted by other projects sometimes (NoFog dev server was) — `PORT=<n>` env
  moves Vite; the `/agui` proxy follows automatically.
- `scratch/` and `assets/` at repo root are untracked leftovers from probing sessions.
