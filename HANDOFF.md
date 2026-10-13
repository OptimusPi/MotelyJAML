# MotelyJAML — Handoff

Date: 2026-10-12. Branch: `release/engine`.

## Project

- Balatro seed-search engine. C#, .NET 10 (SDK pinned in `global.json`). Solution `Motely.slnx`:
  `Motely` (engine), `Motely.CLI`, `Motely.Wasm`, `Motely.Tests`, `Motely.AppHost` (Aspire),
  `Motely.ServiceDefaults`.
- Engine PRNG must be bit-identical to Balatro's Lua (double math, LuaJIT `%.13f` rounding).
- CLI: NativeAOT, `IlcInstructionSet=x86-64-v3`. stdout = CSV rows only; everything else stderr.
- Wasm: `browser-wasm`, NativeAOT-LLVM via Bootsharp 0.9.0, single-threaded. Package
  `motely-wasm` → `Motely.Wasm/bin/motely-wasm`. Build `dotnet publish -c Release` in
  `Motely.Wasm`; test `node Motely.Wasm/tests/smoke.mjs` (Node 25+, `Uint8Array.fromBase64`).
- CLI smoke: `dotnet run -c Release --project Motely.CLI -- --yaml longCola --startSeed ZXXXXXXZ --batchCharCount 2`.
- MCP server: currently `motely mcp [--port N]` inside Motely.CLI (`MotelyMcp.cs`, `McpHttp.cs`);
  Aspire runs it as the `mcp` resource (`AppHost.cs:22-30`). Planned move: see below.

## Working conventions

- The maintainer runs git. Sessions do not.
- Run from `D:\MotelyJAML` with absolute paths; no `cd`.
- One command per shell call; no pipes, no `&&`/`;` chains.
- Read before editing. Edit only on an explicit go.
- Do not delete unreferenced files, package pins or ignore rules (see *Unreferenced pins*).

## Database decision

Requirements: runs on Linux; hosted alongside a Vercel deployment; reachable from .NET
(including the NativeAOT CLI/MCP) and from the seedfinder.app side.

**Use Neon (serverless Postgres) via the Vercel Marketplace integration.**

Why:
- Vercel does not run a database of its own; "Vercel Postgres" became the Neon Marketplace
  integration. Marketplace storage is the supported path: Neon (Postgres), Upstash (Redis),
  Supabase, Turso, PlanetScale, Mongo Atlas, plus Vercel Blob / Edge Config for files / small KV.
- seedfinder.app already stores filters and seeds in Neon. One vendor, one schema, one
  connection-string shape across the public MCP and Motely.MCP.
- Linux-native; scale-to-zero on the free/launch tiers; a Neon branch per git branch gives
  preview deployments their own data.
- .NET access: **Npgsql** (`NpgsqlDataSourceBuilder`), trimming/NativeAOT clean. Mapping by
  hand or **Dapper.AOT**. No EF Core on the NativeAOT CLI/MCP (EF 10 NativeAOT is still
  experimental and needs precompiled queries).
- Connection strings: Neon's pooled endpoint (`…-pooler…`, PgBouncer) from Vercel functions;
  the direct endpoint from the long-running Motely.MCP / Aspire process.
- Local dev: Aspire `builder.AddPostgres("db")` container, `WithReference` into Motely.MCP,
  same SQL as prod. Migrations as numbered `.sql` files applied at startup by a small Npgsql
  runner (DbUp also works and is AOT-safe); no EF migrations.

Not chosen:
- **DuckDB / DuckLake**: embedded analytics; nothing hosts it on Vercel; `DuckDB.NET.Data.Full`
  puts a native library into every AOT publish. Local analysis tool at most. (`ducklake.sqlite`
  sits at the repo root; `DuckDB.NET.Data.Full` is pinned in `Directory.Packages.props:22` and
  referenced by no project.)
- **SQLite / Turso (libSQL)**: on the Marketplace, but the .NET client is immature and SQLite
  semantics do not suit a multi-writer search party.
- **Supabase**: also Postgres; would be a second Postgres vendor next to Neon.
- **Upstash Redis**: not for data of record. Possible later for search-party coordination
  (block claims, heartbeats) if Postgres row locks are not enough.

Initial schema (sketch):
- `filters(slug pk, name, jaml text, author, created_at)`
- `seeds(seed, filter_slug fk, score int, tallies jsonb, found_by, found_at, pk(seed, filter_slug))`
- `search_parties(id pk, filter_slug fk, batch_char_count, total_batches bigint, created_at, cancelled_at)`
- `search_party_claims(party_id fk, batch_start bigint, batch_end bigint, claimed_by, heartbeat_at, done_at, pk(party_id, batch_start))`
  Claim with `SELECT … FOR UPDATE SKIP LOCKED`; expire claims whose heartbeat is stale.

## Planned: move the MCP server out of Motely.CLI into Motely.MCP (Aspire resource)

- Move `Motely.CLI/MotelyMcp.cs` and `Motely.CLI/McpHttp.cs` to a new `Motely.MCP` project.
- They use CLI types `FilterCheck`, `CliSearchMode`, `SearchTelemetry`; move those to `Motely`
  or a shared project.
- `Motely.CLI/Program.cs`: remove `mcpCommand` / `--port`. `Motely.CLI.csproj`: remove
  `ModelContextProtocol.Core`.
- `Motely.AppHost/AppHost.cs:22-30`: `AddProject<Projects.Motely_MCP>("mcp")` + `.WithMcpServer("/mcp")`.
- `Motely.slnx`: add `Motely.MCP/Motely.MCP.csproj`.
- Add to `McpHttp.HandleAsync`, before the Origin check:
  `if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address)) response.StatusCode = 401;`
  (the Origin check alone only stops browsers; http.sys routes a `localhost` prefix by Host header).
- Motely.MCP owns the Neon connection (`ConnectionStrings__db` from Aspire locally, from the
  Vercel/Neon env in prod).

## Open: CSV tally column names

- Should-clause columns print as `score0..N` (`Motely/Filters/MotelySearchBuilder.cs`,
  `DefaultTallyLabel = clause.Label ?? $"score{index}"`). Consumers: `Program.cs` (header),
  `MotelyMcp.cs` (two places). `Motely.Tests/DefaultFallbackTests.cs` pins the fallback.
- Wanted: columns named by the clause's item in Motely's display format.
- Available: `Motely/FormatUtils.cs` formatters; the loader (`YamlConfigLoader.cs`,
  `ClauseNodeDeserializer.Deserialize`) has the written key/value in `wire`/`wireValue`.
- Declined approaches: type-switch in `MotelySearchBuilder`; set `Label` in the loader from the
  written scalar; `WriteHeader()` on `ConsoleResultSink`. Get the exact header text wanted for
  `longCola`'s four should clauses first.

## Review findings

Line numbers are from the 2026-10-12 working tree; verify on `release/engine` before acting.

Engine
1. `MotelyVectorSearchContext.cs:504-507`, `MotelySingleSearchContext.cs:353-356`: resample
   streams ≥ 8 reset to `[]` every time index 8 is reached; Balatro keeps them. `??= []`.
2. `MotelySingleSearchContext.cs:238-260` vs `MotelyVectorSearchContext.cs:342-361`: scalar
   `Round13` differs from the vector FMA rounding. Make scalar the FMA twin; drop `Round13`
   and the unused statics at `MotelyVectorSearchContext.cs:336-338`.
3. `MotelyVectorUtils.cs:47-69, 287-297`: `PackedSimd` branches apply lane 0's shift to all
   lanes / shift mask bytes. Remove; scalar fallbacks are correct.
4. `ClauseScoring.cs:438-442` + `JamlShouldScoreDesc.cs:148-168`: `or: {mode: max, min: 2}`
   re-applies `Min` as an occurrence floor in must/mustNot. Mirror the LogicClause case at
   `JamlShouldScoreDesc.cs:184`.
5. `Filters/Events/*FilterDesc.cs`: empty `rolls` loads, `Debug.Assert` only, `sorted[^1]`
   throws in Release.
6. No load-time validation (`ClauseScoring.cs:247-253` cites a non-existent `ValidateBounds`):
   `min: 0` matches all; `antes: [9]` throws mid-search; > 256 should clauses overruns
   `MotelySeedScoreTally.cs:37-41`; a comma in `label:` breaks the CSV header.
7. `any`/empty list = whole category for jokers/cards/packs; matches nothing for tag/boss/voucher.
8. `MotelySearch.cs:1497-1524` + `MotelySearch.Browser.cs:16-44`: browser `Dispose()` from a
   callback frees buffers the in-flight batch still reads.
9. `MotelySearch.cs:2155-2175`: early `return` after `++SeedCount`, dead `continue`,
   `Console.WriteLine` in the per-seed loop.
10. `MotelyVectorUtils.cs:18-29`: no AVX path for double→int32; x86-64-v3 falls to scalar
    under every `RandInt`.

CLI / MCP / Wasm
11. `McpHttp.cs`: loopback enforced only by Origin (fix text above).
12. `Program.cs:358-388`: Ctrl-C reads the search before workers stop; a faulting worker skips
    the summary and the resume hint.
13. `MotelySearch.cs:1316, 2170-2172`: engine writes to stdout; corrupts piped CSV.
14. `Program.cs:202-203`: `--yaml <missing>` throws with a stack trace.
15. `YamlConfigLoader.cs:90-91,108`: `standardCard:`/`startingDraw:` inline value maps to `label`.
16. `Motely.Wasm/JamlFiles.cs:113-122,161-162`: bypasses `Guard`; `Errors.last()` null for FS failures.

Build / repo
17. `nuget.config`, `Motely.Wasm.csproj:18`, `ci.yml`: private `rewaffle` feed required by
    default; fork PRs and clean clones fail restore. `-p:MotelyFileSystem=false` when no token.
18. No `dotnet publish Motely.CLI` in CI; `release.yml` attaches no files, references a missing `docs/`.
19. `MotelySearch.cs:2644`: `MaxBatch` from `Math.Pow`; use `MotelyGlobals.SequentialBatchCount`.
20. `DuckDB.NET.Data.Full` pinned, unreferenced; `ducklake.sqlite` in root. See *Database decision*.

Additional: `IsExactFilterConfirm` mega-pack inconsistency (`ClauseScoring.cs:85-93`);
phantom `requireMega` alias in six `SourceKeys` arrays; no tests reference `Motely.CLI`, four
tautological assertions; `--save` message on the progress line; `GenerateAssemblyInfo=false`
with no replacement; `vscode-jaml` manifest only; `DietColaTricks*.txt` / `YamlFilters/*.txt`
dumps; `FormatUtils` per-call dictionary and wrong exception arg; `TopSeedSink` de-dup order;
`TagFilterDesc` unused pack stream; twelve identical event descs.

## Unreferenced pins (do not prune)

`Directory.Packages.props` pins these with no `PackageReference` in any project. Leave them;
what they are for is not recorded in the repo: `Bootsharp.Common`,
`Microsoft.Extensions.Http.Resilience`, `Microsoft.Extensions.ServiceDiscovery`,
`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore` / `.Http` /
`.Runtime`, `SixLabors.ImageSharp`, `Terminal.Gui`, `Microsoft.Build.Tasks.Git`,
`Microsoft.AspNetCore.Components.WebAssembly` / `.DevServer`, `Microsoft.CodeAnalysis.CSharp` /
`.Analyzers` (the props comment cites `Motely.Generators`), `Microsoft.AspNetCore.OpenApi`,
`Microsoft.OpenApi`, `Microsoft.Extensions.Hosting.Abstractions` / `Logging.Abstractions` /
`Options`, `Swashbuckle.AspNetCore`, `Microsoft.Extensions.Hosting`,
`Microsoft.Extensions.Logging.Console`, `ModelContextProtocol`, `ModelContextProtocol.AspNetCore`,
`DuckDB.NET.Data.Full`. `AssemblyInfo.cs` still has `InternalsVisibleTo` for `MotelyWorker` and
`Motely.Lsp`; `.gitignore` still names `packages/jaml-mcp`, `jaml-lang/dist`, `vscode-jaml/out`,
`/server`, `Motely.Wasm/analyzer.mjs`.

## Other

- `Motely.AppHost/appsettings.json` hardcodes `"filter": "legendary-perkeo"` for the `search` resource.
- Motely.MCP (local control) and seedfinder.app (public MCP) are separate systems.
- Planned front end: react-three-fiber (pmndrs) consuming `motely-wasm`, replacing `D:\jaml-ui`. Not started.
