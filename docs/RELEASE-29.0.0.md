# Motely 29.0.0

Balatro seed search, rebuilt around plain YAML filters.

## Filters

- Filters are plain YAML. The loader reads them with VYaml's parser straight into typed objects, with no JSON step and no reflection.
- `motely check <filters>` loads every filter and builds its search plan. A directory checks every `.yaml` and `.yml` file in it.
- `motely hook` is a post-edit hook for coding agents. It exits 2 with the error when an edited filter no longer loads.
- `--yaml <name>` with a missing file now prints one error line and exits 1, not a stack trace.

## Engine

- The automatic score cutoff is on by default in the engine and the CLI.
- A seed is never dropped for a negative score.
- The old terminal UI is gone.

## Browser

- `motely-wasm`: the engine and the YAML loader in the browser or Node, through Bootsharp (NativeAOT-LLVM).

## MCP server and Aspire

- `Motely.MCP` is its own program, separate from the CLI. Run it over stdio, or with `--port N` for Streamable HTTP on loopback. Connections from any other address get 401.
- Tools: `validate_filter`, `search_seeds`, `analyze_seeds`, `list_names`, `saved_seeds`, `list_filters`.
- With `ConnectionStrings__db` set, every `search_seeds` result is saved to Postgres, one row per seed and filter. Neon's `postgresql://` URL is accepted as given. Schema changes are numbered SQL files applied once at startup.
- `Motely.AppHost` (Aspire) runs the database, a filter check, the MCP server and a manual search behind one dashboard. With no connection string it starts a local Postgres container. To use Neon instead:

```
dotnet user-secrets set ConnectionStrings:db "<neon connection string>" --project Motely.AppHost
```

## Build

- .NET 10 SDK, pinned in `global.json`.
- The CLI publishes with NativeAOT (`dotnet publish Motely.CLI -c Release -r <rid>`).
- On Windows the linker needs `vswhere.exe` on PATH. Run from a Visual Studio developer prompt, or add `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to PATH.
