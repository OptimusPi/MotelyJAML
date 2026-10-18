# Motely (engine)

SIMD + scalar Balatro seed search. JAML loads into typed `JamlConfig`; filters are FilterDescs.

| Concern | Where |
|---------|--------|
| Search / SIMD | `MotelySearch`, vector contexts |
| JAML grammar | `Filters/`: `JamlConfigLoader` (load), `JamlValidator` (bounds), `MotelySearchBuilder` (config to search); each clause's FilterDesc owns its wire |
| Seed providers | `SeedProviders/` (list, random, sequential, aesthetics) |
| PRNG streams | keyed streams; order within a key is law |

## Commands (from repo root)

```sh
dotnet run -c Release --project Motely.CLI -- --jaml <file> --collect 1
dotnet run -c Release --project Motely.CLI -- check JamlFilters/
dotnet test Motely.Tests -c Release
cd Motely.Wasm && dotnet publish -c Release && npm test   # the npm package (Motely.Wasm/README.md)
cd Motely.Web && npm run dev                              # the browser app, on that package
```

## Projects

| Project | What it is |
|---------|------------|
| `Motely` | The engine and the JAML grammar. Everything else depends inward on it. |
| `Motely.CLI` | Command-line search (NativeAOT). |
| `Motely.MCP` | The engine as an MCP server for this machine; saves finds to Postgres (Neon). |
| `Motely.AppHost` | Aspire: the database, `check`, `mcp`, `search` and `web` behind one dashboard. |
| `Motely.ServiceDefaults` | OTLP traces and metrics for the CLI and MCP; AOT-safe, no ASP.NET. |
| `Motely.Wasm` | `motely-wasm` on npm: the engine in the browser or node via Bootsharp. |
| `Motely.Web` | Vite + React app: jaml-ui's SeedLab and a filters folder, on `motely-wasm`. |
| `Motely.Tests` | The xunit suite. |
