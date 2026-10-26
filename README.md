# MotelyJAML

Balatro seed search. You write a filter in JAML (YAML-shaped), and a SIMD engine sweeps seeds against it.

| Project | What it is |
|---|---|
| `Motely` | The engine and the JAML filter loader |
| `Motely.CLI` | `motely`, the command-line search |
| `Motely.MCP` | MCP server (and AG-UI endpoint) for agents |
| `Motely.Lsp`, `vscode-jaml` | Editor support for JAML |
| `Motely.Wasm`, `Motely.Web` | Browser build and SeedLab UI |
| `Motely.AppHost` | Runs it all under Aspire |
| `JamlFilters/` | Filters. A bare name like `boss-the-eye` means `JamlFilters/boss-the-eye.yaml` |

## Filters

A filter lists `must`, `should` and `mustNot` clauses. A clause is a mapping, or one line of shorthand that loads to the same clause:

```yaml
must:
  - legendaryJoker: Perkeo         # mapping form
    antes: [1, 2]
  - The Eye in ante 3              # shorthand: boss: TheEye, antes: [3]
should:
  - Negative Blueprint in antes 1-3
  - Black Hole in ante 2, 3 and 4
  - Charm Tag in ante 1
```

Shorthand is `[Edition] <Name> [in ante|antes <antes>] [score <N>]`. The name is any joker, boss, voucher, tag, tarot, planet or spectral card, in any case, spaces optional, a leading "The" optional. A legendary joker becomes `legendaryJoker`, any other joker `joker`. Antes are numbers and inclusive ranges joined by commas, `or` and `and`. A line without antes (`- Triboulet`) is the mapping clause with no `antes` key, so it looks in antes 1-8, or in the filter's `defaults:`. A trailing `score N` is `score: N`. An edition (Foil, Holographic, Polychrome, Negative) fits jokers only. Every other key keeps its default, so use the mapping form for `sources` and the like. Write shorthand as `- ` list items, because inside `[ ]` a comma splits it. A bad line fails to load with the closest names or the accepted forms. The parser is `Motely/Filters/JamlShorthand.cs`.

A top-level `defaults:` block gives the keys a clause leaves out:

```yaml
defaults:
  antes: [1, 2, 3]   # every clause that looks in antes and names none (not luckyMoney and the other events, nor and/or)
  score: 5           # every clause that names no score
should:
  - Perkeo           # antes [1, 2, 3], score 5
  - joker: Blueprint
    antes: [4]       # its own antes win
```

It reaches every clause, mapping or shorthand, inside `and:`/`or:` too, and may sit anywhere in the file.

Some keys and names have a second spelling that loads the same clause:

| Written | Means |
|---|---|
| `ante: 2` | `antes: [2]` (any list of numbers may be one number) |
| `requireMega: true` | `requireMegaPack: true` |
| `soulJoker: Perkeo` | `legendaryJoker: Perkeo` |
| `voucher: Reroll Surplus`, `boss: The Hook`, `tarotCard: Fool` | `RerollSurplus`, `TheHook`, `TheFool`: any case, spaces optional, "The" optional, when exactly one name fits |
| `rank: K` | `rank: King` (J, Q, K, A) |
| `dateCreated: 2025-12-25` | metadata, kept and ignored by the search |

A wrong key or name fails to load with one line, on its own line of the file, naming the fix: `luckMoney:` asks whether you meant `luckyMoney`, `commonJoker: Showman` says Showman is an uncommon joker and to write `uncommonJoker: Showman` or `joker: Showman`, and `shopItems:` written beside `joker:` says it goes under `sources:`.

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
dotnet run --project Motely.MCP        # http://localhost:5200/mcp
```

(5200, not 5000: macOS's AirPlay Receiver pins 5000, so the server would fail to bind out of the box.)

- **Claude Code:** `claude mcp add --transport http motely http://localhost:5200/mcp`
- **VS Code:** `.vscode/mcp.json` already points at this server.
- **Aspire:** `dotnet run --project Motely.AppHost` (or `aspire run`) starts it as the `mcp` resource, next to the CLI and web resources.

**Tools.** Every tool that takes a filter accepts either `filter` (a name from `filter_list`, or an absolute path) or `jaml` (the filter's JAML text). With `jaml`, an agent can write and run its own filter without saving anything on the server.
- `filter_list(contains?)`: the server's filters, with name, deck, stake, clause counts and saved-seed count. A file that fails to load is listed with its error.
- `seed_validate(filter | jaml)`: the same check as `motely check`. Failures come back as `isError` results.
- `seed_search(filter | jaml, mode?, ...)`: the structured result carries `matches`, `matched`, `searched`, `durationMs`, `seedsPerSecond`, `note` and `nextBatch`. `mode` is one of:
  - `replay` (default): the filter's `seeds:` block
  - `seeds` + `seeds`: `A1B2C3D4,...` or an absolute path to a seed file (the server runs from its own project directory)
  - `keyword` + `keyword`
  - `random` + `randomCount`
  - `sequential` (+ optional `startBatch`, `batches`): each call sweeps a window of whole batches (16 by default, at most 256; one batch is 42,875 seeds). Pass the returned `nextBatch` as `startBatch` to sweep the next window. Windows never overlap or leave gaps.

  `collect` defaults to 100 matches, or to the whole list for `replay` and `seeds`. The maximum is 10,000. In `sequential` it only caps how many matches are listed, because every window runs to its end.

  Both `seed_validate` and `seed_search` warn about clauses that load but can never hold, such as a boss asked for before its first ante (The Eye first appears in ante 3, The Ox in ante 6). `motely check` prints the same warnings.
- `seed_analyze(seeds, deck?, stake?, antes?)`: the `motely --analyze` report for up to 20 seeds, so an agent can say what a seed actually contains. `antes` trims it to the antes that matter, since the full report is mostly shop queues.

**Resources:** `jaml://guide` (a one-page primer with working example filters) and `jaml://schema` (the full filter JSON schema).

**Other endpoints:** `/agui` (AG-UI agent) and `POST /dev/token` (mints a local JWT in Development).

**Agent panel:** Motely.Web's "Server Agent" tab is a small AG-UI client for `/agui` — it runs
the deterministic `MotelySearchAgent` (real engine, no model) and renders live progress and the
matched seeds. The Vite dev server proxies `/agui` to the `mcp` resource (service discovery under
Aspire, `http://localhost:5200` standalone, `MOTELY_MCP_URL` to override).

**Configuration** (`appsettings*.json`, or environment variables like `Mcp__Authority`):

| Key | Default | Effect |
|---|---|---|
| `MOTELY_FILTERS_DIR` (env) | `JamlFilters` (launch profile: `../JamlFilters`) | Where bare filter names resolve |
| `Mcp:Authority` | unset | OIDC issuer. When set, tokens are validated against it |
| `Mcp:Audience` | `motely-mcp` | Token audience |
| `Mcp:DevSigningKey` | built-in dev key | Local JWT key, used when there is no authority |
| `Mcp:AllowDevTokenIssuer` | on in Development | Enables `POST /dev/token` |

**Auth:** in Development with no authority, the endpoint is open. In Production, or whenever an authority is set, a bearer token is required.
The advertised `ResourceDocumentation` URL points at this section of the README.

## Build and test

```sh
dotnet build Motely.CLI && dotnet build Motely.MCP
dotnet test Motely.Tests
```

`Motely.Wasm` needs the Bootsharp packages from a local feed, so `dotnet build Motely.slnx` fails to restore without them.
