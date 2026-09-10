# MotelyJAML

A Balatro seed searcher, and a language for asking it questions.

You describe the run you want in **JAML** — Jimbo's Ante Markup Language — and the engine
sweeps the seed space with SIMD and hands back the seeds that match.

## How big is the space?

A Balatro seed is 1 to 8 characters from `123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ` — 35 symbols,
no zero. So the space is every length at once:

```
35¹                 35
35²              1,225
35³             42,875
35⁴          1,500,625
35⁵         52,521,875
35⁶      1,838,265,625
35⁷     64,339,296,875
35⁸  2,251,875,390,625
     ─────────────────
     2,318,107,019,760
```

Closed form: `(35⁹ − 35) / 34`. **2.318 trillion.**

Ninety-seven percent of that is the bottom row. Every short seed you have ever typed —
`ALEEB`, `PIROCKS` — rides in the three percent above it.

## The shortest filter that works

```yaml
must:
  - Perkeo
```

That is a complete filter: the seed must contain Perkeo somewhere in antes 1–8. Run it:

```sh
dotnet run --project Motely.CLI -- --jaml myfilter.jaml --collect 1
```

Anything writable as a line is also writable as a block, and the two mix freely:

```yaml
name: Double Negative
must:
  - tag: NegativeTag
    antes: [10]
    min: 2
```

Ante 10 offers exactly two tags — small blind and big blind — so `min: 2` asks for both of them
to be Negative. About 1 in 576. Seed `16661` is one.

## The space is a budget

Every `must` multiplies rarity. One clause at 0.3% still leaves seven billion seeds; stack six
rare ones and you are past 1e-12 and **nothing exists** — 2.3 trillion is a hard ceiling on what
is askable. The engine prints its estimate before it spends anything:

```
Cost:  ~0.1 crunches/seed
Rare:  ~1 in 576  (model: 1/1 clauses)
```

Knowing whether the answer exists is most of the craft. Searching is the easy part.

## Build and run

```sh
dotnet build
dotnet test
dotnet run --project Motely.CLI -- --jaml JamlFilters/Chicot.jaml
dotnet run --project Motely.CLI -- --help
```

The .NET SDK is pinned in `global.json`. The solution is `Motely.slnx` — XML format, there is
no `.sln`.

## What's in the box

| | |
|---|---|
| `Motely` | The engine. SIMD + scalar search, JAML grammar, filters. |
| `Motely.CLI` | The command line. |
| `Motely.Generators` | Roslyn generator that turns `[JamlDiscriminator]` attributes into the schema. |
| `Motely.Lsp` | JAML language server — diagnostics, hover, completion, computed off the engine's own grammar. |
| `Motely.Wasm` | Browser build. NativeAOT-LLVM, no Mono. |
| `Motely.DataLake` | DuckDB seed lake. Nothing found is ever lost. |
| `Motely.DistributedWorker` | Pool/party client for searching across machines. |

One grammar, authored once, in C#. The editor, the CLI, and the browser all read the same
schema — there is no second table to keep in sync.

## License

MIT.
