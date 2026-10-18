# WHINING-1 — read-only review, pass 1

Reviewed at HEAD `ecb731af` with a clean tree. Scope: everything since `ae53d9a1` (23 files, +1512/−104).
That covers `1e89fa6d` (Charm order and tagPacks), `d0955b0a` (pi/spin/fibonacci), `5109db47` (WASM README),
`6d73fcdd` (package pins), and `ecb731af` (Motely.MCP wip). No source was changed. I only ran builds.

| Command | Result |
|---|---|
| `dotnet build Motely.slnx` | **FAILS at restore**: 23 NU1109 errors in AppHost and Tests |
| `dotnet build Motely.MCP` | **FAILS**: 17 compile errors |
| `dotnet build Motely.CLI` | OK, 0 errors |
| `dotnet test Motely.Tests` | **can't run** (restore fails) |

---

## 🔥 Blockers

### B1. Turning on transitive pinning breaks restore for the whole solution
`ecb731af` added `<CentralPackageTransitivePinningEnabled>true` to `Directory.Packages.props`. Every central
pin now also caps transitive versions, and three of the pins are older than what other packages need:

| Pin | Needed by | Wants |
|---|---|---|
| `Microsoft.Extensions.AI.Abstractions` 10.6.0 | `ModelContextProtocol.Core` 2.2.0 | ≥ 10.8.3 |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3 | Aspire.Hosting.JavaScript / .Testing 13.6.1 | ≥ 1.17.0 |
| `Microsoft.Extensions.DependencyInjection` 10.0.5 | Microsoft.Extensions.Hosting 10.0.12 (Aspire) | ≥ 10.0.12 |

Because of `TreatWarningsAsErrors`, NU1109 is fatal, so the test suite can't run at all. Fix: raise those three pins.
MEAI goes to ≥ 10.8.3, and the comment "AGUI.Server 1.0.1 depends on MEAI.Abstractions 10.6.0" is then a floor,
not a reason to stay on 10.6.0. The other option is to drop transitive pinning and pin MessagePack by referencing
it directly in AppHost. Note that the MessagePack pin itself (GHSA fix) is fine.

### B2. Motely.MCP doesn't compile (17 errors)
**Old `Jaml*` names.** The repo is `YAML*`:
- `SearchRunner.cs:57` `JamlConfig` → `YAMLConfig`
- `:60` `JamlConfigLoader` → `YamlConfigLoader`
- `:68` `JamlSearchPlan` → `YAMLSearchPlan`
- `:89-91` `Input(JamlPath:, JamlSeeds:)` → `YAMLPath:`, `YAMLSeeds:`

**C# rules:**
- `SearchRunner.cs:64,75` use `yield return` inside `catch` (CS1631). Fix: catch into a local error and yield after the try.
- `SearchRunner.cs:119` `p.PercentComplete` is a `double`, but `Progress.Percent` is `int`.
- `SearchRunner.cs:151` `WaitForCompletionAsync` returns a `Task`, not a `ValueTask`, so `.AsTask()` doesn't exist.
- `MotelySearchAgent.cs:95` `StringBuilder` needs `using System.Text;` (ImplicitUsings doesn't include it).

**MEAI API is invented:**
- `ChatResponseUpdate` has no `TextContent` property (`:98,106`). Use `new ChatResponseUpdate(ChatRole.Assistant, text)` and read `update.Text`.
- `new ChatMessage(ChatRole.Assistant) { Text = … }` (`:101`): there's no 1-arg ctor, and `Text` is read-only. Use `new ChatMessage(ChatRole.Assistant, text)`.
- `Program.cs:24`: `IChatClient` isn't in scope because `using Microsoft.Extensions.AI;` is missing.

**AG-UI API is invented:**
- `Program.cs:23,41`: `AddAGUI()` and `MapAGUI()` don't exist in AGUI.Server 1.0.1 under these usings.
- Someone needs to read the real AGUI.Server surface (the docs at docs.ag-ui.com/sdk/dotnet) before writing more against it.
- `StateSnapshotEvent { Snapshot = … }` (`MotelySearchAgent.cs:58`) also needs checking against the real type.

### B3. The default filters dir doesn't exist
`Program.cs:6` falls back to `"JamlFilters"`, but the repo has `YamlFilters/`. `FilterCheck.cs:22` already
defaults to `YamlFilters`, so overwriting `MOTELY_FILTERS_DIR` with `JamlFilters` breaks every filter lookup.
Fix: drop the fallback block and let `FilterCheck` own the default.

---

## ⚠️ Will bite once it compiles

- **W1. A search with no cap runs forever.** With MCP `search(filter, mode:"sequential")` and no `collect`, or with the
  AG-UI agent, which always passes `collect: null`, the whole seed space gets walked: about 2.3 trillion seeds.
  The request hangs, and `matches` (an unbounded `List` plus an unbounded channel) grows without limit.
  - Fix: a default `collect` (e.g. 50).
  - Fix: a hard max for remote callers.
  - Fix: add a `CancellationToken` parameter to `MotelyTools.Search`, which the MCP SDK binds automatically. Right now
    nothing can cancel a tool call.
- **W2. "Securely" and EXT-Auth aren't there yet.** `/mcp` and `/agui` have no auth (`RequireAuthorization` is never called).
  `ModelContextProtocol.Extensions.Apps` and `AGUI.Abstractions` are pinned but never referenced. CORS doesn't
  protect against non-browser clients. Today it's only safe because Kestrel's default binds to localhost. Don't
  expose it on the LAN until auth exists.
- **W3. Matches lose their separators.** The agent streams each seed as bare text (`Text(m.Seed)`), so
  `GetResponseAsync` glues them into `ABC123XYZ789…`. Add a newline or `", "`.
- **W4. Matches are collected twice.** `SearchRunner` keeps its own `matches` list and also streams `Match` events, and
  `MotelyTools.Search` builds a second list. Pick one, using `Done.Matches` or the stream.
- **W5. Dead code.** `MotelySearchAgent.cs:84-87` is an empty `if`, and `parseError` is always null (`ParseMessage` never sets it).
- **W6. ServiceDefaults is referenced but unused.** There's no `AddServiceDefaults()` and no `MapDefaultEndpoints()`, so the
  Aspire health/OTel wiring that the reference implies isn't there. The AppHost doesn't have an `mcp` resource yet either.
- **W7.** `ModelContextProtocol.Core` is pinned alongside `ModelContextProtocol`. That's fine, but the Tests project
  references `.Core` directly, which is how B1 shows up in Tests too.

---

## ✅ Checked and fine

- `5109db47` WASM README: every `Jaml*` → `YAML*` name matches the real exports
  (`YAMLConfigLoader.FromYAML` is at `Motely.Wasm/Program.cs:132`, and Bootsharp camel-cases it to `fromYAML`). M1 from
  WASM-SCRUTINY is **fixed**.
- `6d73fcdd`'s duplicated `Npgsql` line was cleaned up again in `ecb731af`.
- The MessagePack 2.5.303 pin plus its comment is the right shape.
- The Motely core and CLI build clean.

## 🤓 Notes for the humans and agents

- In filters, **don't write `legendaryJoker: []`**. Use `legendaryJoker: Any`, or leave it blank (Nat's rule). The ante-2 Charm
  filter in my earlier handoff should read:
  ```yaml
  must:
    - legendaryJoker: Any
      soulCardOnly: true
      antes: [2]
      sources: { tagPacks: [0] }
  ```
- WASM-SCRUTINY-NEXT-STEP.md is still unchanged. S1 (Binaryen) is wrong, because wasm-opt is set up, and the five
  line numbers listed in my handoff are still off.
- The ante-2 Charm tests, the TagPacks doc wording, and the `allistic handoff2.md` merge are still open.

## Fix order
B1 (restore) → B3 (one line) → B2 (names first, then the MEAI and AG-UI APIs against the real packages) → W1 (cap and cancel) → W2 (auth).
After B1, rerun `dotnet test Motely.Tests`. The last green run was 591/591 at `d0955b0a`'s content.
