# WHINING-2: read-only review, pass 2 (4:20 later)

The branch is now **`v30-is-love-all0-brains-are-beautiful`**, at `8bf2aff0`. Master was merged in. The working tree has
uncommitted MCP edits in `Motely.MCP/{Program.cs, Search/SearchRunner.cs, Agent/MotelySearchAgent.cs}` and a new
`Motely.MCP/Agui/`, all from another agent that is still working. Since pass 1, 493 files changed: the YAML→JAML rename
landed, along with `Motely.Lsp`, `vscode-jaml`, the schema, and `SchemaWriter`. I changed no source and only ran builds.

| Command | Pass 1 | Pass 2 |
|---|---|---|
| `dotnet build Motely.slnx` | ❌ NU1109 at restore | ❌ **still**: same 3 pins (AppHost and Tests) |
| `dotnet build Motely.MCP` | ❌ 17 errors | ✅ **0 errors** (working tree, after the other agent's live fixes) |
| `dotnet build Motely.CLI` | ✅ | ✅ |
| `dotnet build Motely.Lsp` | none | ✅ |
| `dotnet build Motely.Wasm` | none | ✅ |
| `dotnet test Motely.Tests` | ❌ blocked | ❌ **still blocked by B1** |

## What changed since WHINING-1

- **B2's `Jaml*` names are now correct.** The rename made `JamlConfig`, `JamlConfigLoader`, `JamlSearchPlan` and
  `Input(JamlPath:, JamlSeeds:)` real. My pass-1 "rename to YAML*" advice is **void**. Sorry, the ground moved under it.
- **B3 is fixed by the rename.** `JamlFilters/` exists now, so the fallback in `Program.cs:6` works. It still duplicates
  `FilterCheck`'s own default and could be dropped, but that's optional.
- These B2 items are **fixed** in the working tree:
  - `yield` inside `catch`, now moved into `TryLoadConfig` / `TryCreatePlan`
  - the `double` → `int` percent
  - `.AsTask()`
  - `using System.Text`
  - `ChatResponseUpdate.Contents` / `new ChatMessage(role, text)`
  - `using Microsoft.Extensions.AI`
  - the real `AddAGUI` / `MapAGUI`, now hand-written in `Agui/AguiEndpoint.cs` on top of `AGUI.Server`'s
    `ToChatRequestContext` + `AsAGUIEventStreamAsync` + SSE
- W5 dead code is **fixed** (the empty `if` and the `matched` counter are gone).

## 🔥 Still blocking

### B1. Restore (unchanged, and still the #1 problem)
`CentralPackageTransitivePinningEnabled` plus three stale pins in `Directory.Packages.props`:
- `:34` `Microsoft.Extensions.AI.Abstractions` 10.6.0. **MCP.Core 2.2.0 needs ≥ 10.8.3.**
- `:41` `Microsoft.Extensions.DependencyInjection` 10.0.5. Aspire 13.6.1's Hosting needs ≥ 10.0.12.
- `:44` `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3. Aspire.Hosting.JavaScript/Testing needs ≥ 1.17.0.

Fix: bump the three versions, or turn transitive pinning off and pin MessagePack directly where Aspire uses it. Until one of
those happens, CI's `test` job is red and none of the ~590 tests run.

### B2′. Motely.MCP: ✅ FIXED while I was writing this
At first the build showed 2 CS8625 errors (`out` params set to `null` in `SearchRunner.TryLoadConfig` / `TryCreatePlan`).
The other agent fixed them mid-review. A rebuild of the current working tree gives **`dotnet build Motely.MCP`: 0 errors**.
Those fixes are uncommitted, so commit them before anyone switches branches.

## ⚠️ New this pass

- **N1. The WASM README went stale again.** `5109db47` changed it to `YAMLConfigLoader / YAMLSearchBuilder / MotelyYAMLyzer /
  fromYAML / YAMLFiles`, and then the rename put the exports back to `JamlConfigLoader` (`Motely.Wasm/Program.cs:125`),
  `JamlConfigLoader.FromJaml` (`:132`), `JamlSearchBuilder` (`:145`) and `MotelyJamlyzer` (`:158`). The README is wrong
  in the other direction now, so it needs `git revert 5109db47` or the same edit reversed.
- **N2. Build output is committed.** `vscode-jaml/out/*.js(.map)` and `vscode-jaml/jaml-language-support-3.5.0.vsix`
  (a 2.9 MB binary) are tracked. Suggestions:
  - add `vscode-jaml/out/` and `*.vsix` to `.gitignore`
  - `git rm --cached` both
  - ship the .vsix as a release asset
- **N3. Odd file in the filter corpus:** `JamlFilters/141Z573J, 5, 3, 2-colaaaaqaaaaa.txt` (seed results, CSV-ish). It's
  harmless, because `check JamlFilters/` only reads .yaml/.yml/.jaml. But the name has commas and spaces, and it's results
  rather than a filter, so it probably belongs elsewhere. `JamlFilters` also holds 1 .csv. Your call, Nat. Flagged, not judged.
- **N4.** Filters are still `.yaml` (184 of them), which is fine: `FilterCheck` takes .yaml/.yml/.jaml, and CI/AppHost now
  say `--jaml` and `JamlFilters/`. That's consistent. ✅

## ⚠️ Carried from pass 1 (still open)

- **W1. Uncapped search.** The AG-UI agent still calls `RunAsync(filter, mode, null, …)`. "search perkeo sequential" walks
  the entire seed space, and the `matches` list and channel are both unbounded. The MCP `Search` tool still has no
  `CancellationToken` and no default `collect`.
- **W2. No auth.** `/mcp` and `/agui` still have no auth, and `Extensions.Apps` is pinned but unused. Keep it on localhost.
- **W3. Separators.** Seeds are streamed as bare text and glued together in `GetResponseAsync`.
- **W4. Matches collected twice.** That's still in `MotelyTools.Search` alongside `Done.Matches`.
- **W6. ServiceDefaults is unused.** There's still no `AddServiceDefaults()`, and still no `mcp` resource in AppHost.
- **New (minor):** `AguiEndpoint.AddAGUI` **replaces** `SerializerOptions.TypeInfoResolver` instead of prepending to
  `TypeInfoResolverChain`, which silently drops any resolver registered before it. That's harmless today, but it's a trap.

## Still waiting on my old lane

- ante-2 Charm tests. The filter uses `legendaryJoker: Any` (or blank), **never `[]`**.
- the TagPacks doc wording
- the `allistic handoff2.md` merge. `allistic handoff2.md` changed by 4 lines in the merge; I didn't re-verify the edition-prefilter line.
- WASM-SCRUTINY: drop S1 (wasm-opt is set up), fix the line numbers, and now also add N1 above.

## Fix order (updated)
1. **B1** (bump 3 pins) → `dotnet test` runs again
2. Commit the working-tree MCP fixes (they compile)
3. **N1** (README back to `Jaml*`)
4. **W1** (cap and cancel)
5. **N2** (stop committing build output)
6. **W2** (auth)
