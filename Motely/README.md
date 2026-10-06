# Motely (engine)

SIMD + scalar Balatro seed search. JAML loads into typed `JamlConfig`; filters are FilterDescs.

| Concern | Where |
|---------|--------|
| Search / SIMD | `MotelySearch`, vector contexts |
| JAML grammar | `Filters/Jaml/` — FilterDesc owns wire; `JamlSchema` indexes |
| Seed providers | `SeedProviders/` (list, random, sequential, aesthetics) |
| PRNG streams | keyed streams; order within a key is law |

## Commands (from repo root)

```sh
dotnet build
dotnet test
dotnet run --project Motely.CLI -- --jaml <file>
dotnet run --project Motely.CLI -- --jaml <file> --collect 1
dotnet run --project Motely.TUI
claude mcp add motely -- dotnet run --project Motely.MCP -c Release   # party tools over stdio
cd Motely.Wasm && dotnet publish -c Release && node tests/smoke.mjs   # the npm package (Motely.Wasm/README.md)
```

## Projects

| Project | What it is |
|---------|------------|
| `Motely` | The engine and the JAML grammar. Everything else depends inward on it. |
| `Motely.CLI` | Command-line search, and `--party` for seedfinder.app Search Parties. |
| `Motely.TUI` | Terminal UI: filter library, JAML editor, search with live results. |
| `Motely.MCP` | MCP server (stdio). `party_start` splits one sweep across local Motely.CLI processes; `party_status`, `party_list`, `party_stop`, `party_save`. |
| `Motely.Wasm` | `motely-wasm` on npm: the engine in the browser via Bootsharp. |
| `Motely.Tests` | The xunit suite. |

One grammar: editors and hosts load JAML through this project, never a second table.
