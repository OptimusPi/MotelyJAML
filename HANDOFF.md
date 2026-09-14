# Handoff

## Project

MotelyJAML is a Balatro seed searcher and per-seed analyzer in C# (.NET 10, C# 14). It runs
natively (CLI, DistributedWorker) and in the browser through Bootsharp on NativeAOT-LLVM
(`Motely.Wasm`, always `-c Release`). Filters live in `JamlFilters/`.

The operator's statements about this project are facts. Do not re-litigate them.

## Working rules

- Read the relevant files before asking. Ask only when the answer changes what you do next.
- Do exactly the requested task. No extra features, abstractions, or files.
- Stay inside `D:\MotelyJAML`. No recursive scans of drives or the user profile.
- Never read, print, or pass credentials.
- PowerShell is denied. Use Bash.
- Do not guess library APIs. Cite documentation, source, or code already compiling in this repo.
- Stop immediately on "stop" or "sit".
- Report plainly: what changed, what was verified, what failed.

## State: YAML-only loader (done, uncommitted, base `68b42ff`)

The engine reads filter documents as YAML through VYaml. JSON is read by the same parser.
The one-line clause syntax (`- Blueprint in ante 1`, "JUMMY") is removed from C#; the
operator is moving it into a TypeScript language server.

### Removed

- `Motely/Filters/Jaml/JamlDocumentParser.cs` (hand-written JAML parser)
- `Motely/Filters/Jaml/JamlForeignTree.cs` (System.Text.Json path and old YAML path)
- `Motely/Filters/Jaml/JamlLine.cs` (one-line syntax)
- `Motely/Filters/Jaml/JamlConfigWriter.cs` (`ToJaml`)
- `Motely/Filters/Jaml/JamlLoadFormat.cs`
- Tests for those features: `JamlConfigWriterTests`, `JamlLineCanonicalizeTests`,
  `JamlLineClauseTests`, `JamlLineTests`, `S8P3JamlLineTests`, `S8P3WriterShapeTests`,
  `TerseLineClauseTests`, `TerseScoreDefaultTests`, `JummyEquivalenceTests`, and the fixture
  files `Motely.Tests/JamlFilters/Zerkeo_Jummy_Antes.jaml`, `Zerkeo_Jummy_Antes_Range.jaml`.

### Added

- `Motely/Filters/Jaml/JamlNode.cs`: `JNode`/`JMap`/`JSeq`/`JScalar` (moved out of the old
  parser; `JScalar.IsNull` added) and `JamlIntRange` (range grammar `1-8`, `1..8`, `1–8`).
- `Motely/Filters/Jaml/JamlYamlTree.cs`: VYaml events into the node tree. Also defines
  `JamlSyntaxException` (positioned YAML syntax error, LSP code `JAML0001`).

### Changed

- `JamlConfigLoader`: single entry `FromJaml(string)` / `TryLoad(string, out, out)`.
  Clause lists accept mappings only. The desc-driven populator is unchanged.
- `JamlSearchBuilder.DefaultTallyLabel`: `clause.Label ?? $"score{index}"`. Unlabeled should
  columns were previously named by their one-line spelling.
- `MotelyJamlFile.TryLoad(path, format, ...)` and `FormatFromPath` removed.
- `Motely.CLI`: `--jaml`, `--json`, `--yaml` still exist; all three go through the one loader.
- `Motely.Lsp/JamlLanguageService.cs`: one-line discriminator detection removed; syntax
  diagnostics use `JamlSyntaxException`.

### VYaml 1.1.1 behavior, verified by tests in this repo

- `CurrentMark` is the scanner position, not the token start. A plain scalar's mark is already
  on the next line. `CurrentMark.Line` is 1-based. Spans are therefore found by searching the
  source text forward from the previous token (`JamlYamlTree.Locator`).
- `null`, `~` and JSON `null` come back as the text "null" from `GetScalarAsString`; use
  `IsNullScalar()`.
- Bare `key:` with no value hangs the tokenizer only as the last token with no trailing newline.
  `JamlYamlTree.EnsureTrailingNewline` appends `\n` before parsing (replaced the ` ~` rewrite).
- Block scalars follow YAML: `|` and `>` keep one trailing newline; `|-` and `>-` strip it.

### Verification

- `dotnet build --no-incremental`: `Motely.slnx`, `Motely.DataLake`, `Motely.JsonRender`,
  `Motely.DistributedWorker` all 0 errors, 0 warnings. `Motely.Wasm -c Release` build: 0/0.
  `Motely.Wasm` publish was not run.
- `dotnet test Motely.Tests`: 1750 passed, 0 failed.
- Corpus `JamlFilters/` (159 documents): loaded and planned with both `68b42ff` and this
  change, every config dumped field by field and diffed.
  - 157 load, 2 fail, identical before and after: `ColaOopsLite.jaml` (`score: 1|`) and
    `M.yml` (`Showman` is not a `MotelyJokerCommon`).
  - Behavior change: flow-style `sources: { ... }` was silently ignored by the old parser (filter
    fell back to default sources). It is now honored. Affects `NegTag_FourOops.jaml` (10
    clauses) and `_flowsyntax_test.jaml`. These two filters now match different seeds.
  - 48 folded (`>`) descriptions now end with `\n`. Text only.
  - Everything else is identical.

### Operator filters rewritten (one-line syntax to mappings, same clauses)

- `JamlFilters/Pickle.jaml`, `JamlFilters/DietCola_Ghost_Ankh.jaml`,
  `JamlFilters/tardy_grade.jaml`, `JamlFilters/Zerkeo.jaml`
- `Motely.Tests/JamlFilters/Pickle.jaml`, `Motely.Tests/JamlFilters/Zerkeo_Pure.jaml`

The operator may want these back as one-liners once the TypeScript language server exists.

### Review findings (2026-09-13, not fixed)

Two reviews ran against this change (an adversarial Sonnet agent and `/code-review`). Each
finding below was reproduced by the reviewer. None is fixed.

1. FIXED: rewrite removed, pinned by `JamlBlockScalarTests.BlockText_LineEndingInColon_IsKeptAsTyped`.
   `JamlYamlTree.FillEmptyMappingValues` appended ` ~` to any line
   ending in `:` whose next line is not indented deeper, including lines inside `|`/`>` block
   scalars. `description: |` + `Options:` + `more text` loads as `"Options: ~\nmore text\n"`.
   Silent. No corpus file is affected today.
2. `YamlPairCap = 65_536` (`JamlYamlTree.cs`) now applies to `.jaml` files. A `seeds:` list over
   65,536 entries fails to load, so `MotelyJamlFile.TrySaveSeeds` eventually fails for filters
   that accumulate seeds.
3. `JamlYamlTree.Locator.SpanOf` counts newlines from the start of the text for every scalar:
   quadratic. 20,000 seeds took ~5.2 s to load. The LSP runs the loader on every edit.
4. Unquoted seed `NULL` (or `null`, `Null`, `~`) is a YAML null and loads as `""`
   (`JScalar.IsNull` + `GetStringArray`). The old `.jaml` parser kept the text.

Not yet reviewed: span placement when a token's text appears earlier in the document, and
null/blank handling across all clause keys.

### VYaml version

- DONE: `VYaml` and `VYaml.Annotations` bumped to 1.4.0. Build 0/0, `Motely.Tests` 1751 passed
  (run with `DOTNET_GCHeapHardLimit=0x100000000` and `--blame-hang-timeout`). Corpus comparison
  not rerun. Wasm build not rerun. Hang not re-tested.
- Measured on 1.1.1, in a scratch console, 3 s timeout per document: bare `key:` followed by a
  newline parses fine and reports a null scalar. `must:\n  - joker:` with NO trailing newline
  hangs and allocates without bound. The ` ~` rewrite exists only for this case.
- Operator instruction: do not run hang tests again. They consume all RAM.

### Next (operator approval required for each step)

1. DONE: VYaml 1.4.0. Still to do: rerun the corpus comparison.
2. DONE: ` ~` rewrite replaced with trailing-newline fix.
3. Remove `YamlPairCap`; compute line numbers incrementally in `Locator`; keep raw text for seeds.
4. Add tests: block scalar with a `:`-terminated line, bare discriminator in each position,
   100k seeds load time, seed `NULL`.

### Open

- The one-line equivalence property ("every spelling of the antes finds the same seeds") had its
  test in `JummyEquivalenceTests`. It now has no owner. It belongs to the TypeScript server.
- `Motely.Generators` still generates `JamlSchema` from `[JamlDiscriminator]`. Not touched.
- Nothing is committed.
