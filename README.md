# MotelyJAML

Balatro seed search. You write a filter in JAML (YAML), and a SIMD engine sweeps seeds against it.

| Project | What it is |
|---|---|
| `Motely` | The engine and the JAML filter loader |
| `Motely.CLI` | `motely`, the command-line search |
| `Motely.MCP` | MCP server (and AG-UI endpoint) for agents |
| `Motely.Lsp`, `vscode-jaml` | Editor support for JAML |
| `Motely.Wasm`, `Motely.Web` | Browser build and SeedLab UI |
| `Motely.AppHost` | Runs it all under Aspire |
| `JamlFilters/` | Filters. A bare name like `perkeo` means `JamlFilters/perkeo.yaml` |

## CLI

```sh
dotnet run --project Motely.CLI -- --jaml <filter> [mode] [options]
```

stdout carries only results (CSV: `seed,score,<tallies>`). Progress, warnings and the summary go to stderr, so `> out.csv` saves a clean file.
Exit codes: `0` finished, `1` bad input, `130` interrupted (the resume point is printed).

**Choosing seeds** (pick one mode; sequential is the default):

| Flag | Searches |
|---|---|
| *(none)* | Sequential sweep of every seed. Resume with `--startBatch`/`--startSeed` |
| `--seeds A1B2C3D4,E5F6G7H8` | Exactly these seeds |
| `--seeds file.csv` | Every seed in a file: one per line, first column. A motely CSV, a plain list, or a spreadsheet export all work. Repeats run once, and bad lines are warned about and skipped |
| `--seeds -` | Seeds read from stdin |
| `--replay` | The filter file's own `seeds:` block |
| `--keyword WORD` / `--keywords A,B` | Seeds that contain the word(s) |
| `--random N` | N random seeds |
| `--aesthetic NAME\|all` | Pattern families such as palindromes |

**Other options:** `--deck`, `--stake` (override the filter's values), `--threads`, `--collect N` (stop after N matches), `--cutoff N|auto|off` (minimum score to print), `--save` (merge the printed seeds into the filter's `seeds:` block), `-q`.

**Commands:**
- `motely --analyze SEEDS|file|-` prints each seed ante by ante: boss, voucher, tags, shop and packs.
- `motely check <filters...>` checks that each filter loads.
- `motely hook` is a post-edit hook for coding agents (exit 2 when an edited filter breaks).

### Researching a pile of seeds

There is no database. A seed list is just a file, and replaying one takes milliseconds, so keep the CSVs and re-run filters over them:

```sh
motely --jaml longColaDanker --cutoff off > pile.csv       # collect (Ctrl+C whenever)
motely --jaml otherFilter --seeds pile.csv --cutoff off    # re-score the pile with another filter
motely --jaml a --seeds pile.csv -q | motely --jaml b --seeds - -q   # chain filters
motely --jaml longColaDanker --seeds pile.csv --save       # keep the winners in the filter
motely --analyze pile.csv                                  # read them all
```

## MCP server

```sh
dotnet run --project Motely.MCP        # http://localhost:5000/mcp
```

- **Claude Code:** `claude mcp add --transport http motely http://localhost:5000/mcp`
- **VS Code:** `.vscode/mcp.json` already points at this server.
- **Aspire:** `dotnet run --project Motely.AppHost` (or `aspire run`) starts it as the `mcp` resource, next to the CLI and web resources.

**Tools**
- `validate_filter(filter)`: the same check as `motely check`.
- `search(filter, mode?, collect?)`. `mode` is one of:
  - `replay` (default)
  - `seeds <A1B2C3D4,...>` or `seeds </absolute/path/to/seeds.csv>`. Use an absolute path, because the server runs from its own project directory.
  - `keyword <kw>`
  - `random <n>`
  - `sequential`

  `collect` defaults to 100 matches, or to the whole list for `replay` and `seeds`. The maximum is 10,000.

**Other endpoints:** `/agui` (AG-UI agent) and `POST /dev/token` (mints a local JWT in Development).

**Configuration** (`appsettings*.json`, or environment variables like `Mcp__Authority`):

| Key | Default | Effect |
|---|---|---|
| `MOTELY_FILTERS_DIR` (env) | `JamlFilters` (launch profile: `../JamlFilters`) | Where bare filter names resolve |
| `Mcp:Authority` | unset | OIDC issuer. When set, tokens are validated against it |
| `Mcp:Audience` | `motely-mcp` | Token audience |
| `Mcp:DevSigningKey` | built-in dev key | Local JWT key, used when there is no authority |
| `Mcp:AllowDevTokenIssuer` | on in Development | Enables `POST /dev/token` |

**Auth:** in Development with no authority, the endpoint is open. In Production, or whenever an authority is set, a bearer token is required.
(The advertised `ResourceDocumentation` URL, docs.motely.dev/mcp, doesn't exist. This README is the documentation.)

## Build and test

```sh
dotnet build Motely.CLI && dotnet build Motely.MCP
dotnet test Motely.Tests
```

`Motely.Wasm` needs the Bootsharp packages from a local feed, so `dotnet build Motely.slnx` fails to restore without them.
