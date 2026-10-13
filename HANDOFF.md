# Handoff

Branch `release/engine`, 6 commits ahead of `master`. Last two commits: `92b5046c` (74-file catch-all) and `7c233eca` (deleted docs/RELEASE-29.0.0.md). Both pushed.

Nothing below has been fixed. Build and tests were not run this session.

## Broken

- **Motely.Web does not build.** `Motely.Web/src/App.tsx` imports `./FilterFolder.js`. That file does not exist. It is the component that lists filters from the picked folder via `JamlFiles` and hands one to `SeedLab`.
- **Motely.Web is not wired in.** Its package.json says "served by Aspire", but `Motely.AppHost/Program.cs` has no resource for it and `Motely.slnx` does not list it. Needs an `AddNpmApp`/`AddJavaScriptApp` resource with `PORT` passed (vite.config.ts already reads it).
- **Motely.Web has no node_modules.** `npm install` has never been run there. Whether jaml-ui 8.1.0 actually exports `JimboApp`, `JimboBackground`, `JimboBalatroFooter`, `JimboWordmark`, `SeedLab`, `STARTER_JAML` is unverified. Check `node_modules/jaml-ui/dist/index.d.ts` after install.
- **Motely.Web/.npmrc is gitignored.** Root `.gitignore` ignores `.npmrc` and only un-ignores `Motely.Wasm/.npmrc`. A fresh clone of Motely.Web can't resolve `@rewaffle/bootsharp-file-system` from GitHub Packages. Either un-ignore it or move the `@rewaffle:registry` line to a root `.npmrc` that is tracked.

## Stale

- `Motely.Wasm/README.md` still says `YamlConfigLoader` and `node tests/smoke.mjs`. The code is `JamlConfigLoader`, smoke.mjs is deleted, tests run with `npm test` (`node --test tests/`).
- `.gitignore` references things that no longer exist: `packages/jaml-mcp`, `vscode-jaml/`, `jaml-lang/dist`, `Motely.Wasm/analyzer.mjs`, `*.duckdb`. Its comment about Motely.Wasm/.npmrc being "token-via-${RNV}" is wrong; that file holds no token.
- Root `package.json` is `{}`. Delete it or give it a purpose.
- `docs/` is now empty after the release notes were deleted.

## Risky

- `@rewaffle/bootsharp-file-system` is pinned to `latest` in both Motely.Wasm and Motely.Web. Pin a version. `.npmrc` comment claims CI writes a token; no CI config exists in the repo.
- `YamlFilters/standard-*.yaml` and `Motely.Tests/YamlFilters/standard-*.yaml` are duplicates that were both edited in `92b5046c`. Pick one source of truth or have the test project reference the root folder.
- `Motely/Filters/Events/` was collapsed from 12 per-event filter descs into `EventFilterDesc<TRoll>` + `EventClauses.cs`. Scalar/vector parity for each event (Bloodstone, Business, Cavendish, Glass, GrosMichel, LuckyMoney, LuckyMult, Misprint, Parking, SpaceLevelup, WheelOfFortune, WheelStaysFlipped) needs a test run to confirm nothing regressed. `LuckyEventLuckTests.cs` exists; the others may not be covered.
- `JamlValidator.Validate` throws one `InvalidOperationException` with newline-joined messages. The WASM `Errors.last()` path and `motely check` both depend on that message surviving; confirm it does.

## First things to run

```bash
dotnet build Motely.slnx
```

```bash
dotnet test Motely.Tests
```

```bash
cd Motely.Wasm && dotnet publish -c Release && npm test
```

```bash
cd Motely.Web && npm install && npm run typecheck
```
