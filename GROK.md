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
