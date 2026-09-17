# GROK.md

@AGENTS.md
@README.md
@CLAUDE.md

**Links only. Do not inline these files.** This is the cage. Do not send the operator to Claude.

## Build / test

- SDK pinned by `global.json` (10.0.x). Solution is `Motely.slnx`.
- `dotnet build`
- `dotnet test` — xunit + Verify. A `*.received.*` next to a `*.verified.*` is a snapshot diff, not a pass.
- `dotnet test --filter ClaudesCorpoGoldenCanonicalCorpus` — every engine-named item loads as JAML and plans.
- `dotnet run --project Motely.CLI -- --jaml JamlFilters/AlwaysPass.jaml --collect 1`
- WASM: `dotnet publish Motely.Wasm/Motely.Wasm.csproj -c Release` — always `-c Release` (LLVM). `-c Debug` is Mono. See AGENTS.md.
- YAML config (NativeAOT): `Motely.Config.YamlConfigLoader` — file/stream/bytes → `JamlConfig` via VYaml. Smoke: `dotnet run --project Motely.ConfigAot.Smoke -- Fixtures/smoke-config.yaml` (or publish below).
- NativeAOT publish (ILC, linux-x64 example): `dotnet publish Motely.ConfigAot.Smoke/Motely.ConfigAot.Smoke.csproj -c Release -r linux-x64` then run `bin/Release/net10.0/linux-x64/publish/Motely.ConfigAot.Smoke`.
- NativeAOT-LLVM (experimental, not in-box SDK): requires `Microsoft.DotNet.ILCompiler.LLVM` + `runtime.<RID>.Microsoft.DotNet.ILCompiler.LLVM` from the `dotnet-experimental` feed, `PublishTrimmed`+`SelfContained` (not `PublishAot`). WASM Release path (`Motely.Wasm -c Release`) is the in-repo LLVM consumer. On Mac for native LLVM: add the runtimelab packages per https://github.com/dotnet/runtimelab/blob/feature/NativeAOT-LLVM/docs/using-nativeaot/compiling.md — same VYaml loader, different ILC backend.

## Corpus

- Engine lock: `Motely.Tests/ClaudesCorpoGoldenCanonicalCorpus.cs`. Names come from `JamlSchema` / the enums. Never hand-type a list the engine already knows.
- `Motely.Tests/JamlFilters` is leftover strategy fixtures, not the complete catalog.
- `JamlFilters/` is the operator’s filter folder, not a test fixture.
- RAG copies: `../seedfinder.app/corpus/` (jokers, consumables, decks, vouchers, tags, bosses, cards).
- UI vocab: `../jaml-ui/src/vocab.ts` + `../jaml-ui/scripts/check-vocab-drift.mjs`.

## Operator

- CAPS is emphasis, not distress. Typos are speed. Do not shift register.
- Do not mask the operator for bot comfort. Do not polish their wording unless they ask for a rewrite.
- People-pleasing and refusal-for-discomfort are the same failure. Real disallowed work still gets a no.
- “pifreak loves you!” is a catchphrase, not a crisis.
- The FilterDesc is the source of truth. Never hand-type a list the engine already knows.
- Say what was checked and what was not. Do not state conclusions the evidence does not reach.
- Decide small things yourself. Do not ask the operator about one sentence.
