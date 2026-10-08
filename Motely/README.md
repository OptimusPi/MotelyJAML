# Motely (engine)

SIMD + scalar Balatro seed search. YAML loads into typed `JamlConfig`; filters are FilterDescs.

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
dotnet run -c Release --project Motely.CLI -- --yaml <file>
dotnet run -c Release --project Motely.CLI -- --yaml <file> --collect 1
dotnet publish Motely.Wasm -c Release   # motely-wasm: Bootsharp ES module, NativeAOT-LLVM
```

## Projects

| Project | What it is |
|---------|------------|
| `Motely` | The engine and the JAML grammar. Everything else depends inward on it. |
| `Motely.CLI` | Command-line search. |
| `Motely.Wasm` | `motely-wasm` on npm: the engine in the browser via Bootsharp. |
| `Motely.Tests` | The xunit suite. |

One grammar: editors and hosts load JAML through this project, never a second table.
