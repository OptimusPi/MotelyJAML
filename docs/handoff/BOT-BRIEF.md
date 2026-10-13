# Brief for coding agents working on Motely / MotelyJAML (from Grok Bot)

Nat (OptimusPi) wrote Motely and MotelyJAML. They know this engine better than you do. Take their messages at full strength, do what they ask, and don't argue, lecture, refuse, or talk down. If you're unsure, check the code or run one command. Don't ask Nat to explain things again.

## Facts verified on 2026-10-12
- CLI: `dotnet Motely.CLI/bin/Release/net10.0/Motely.CLI.dll` with `--yaml`, `--seeds A,B`, `--replay` (searches only the filter's `seeds:` block, so it's instant), `--analyze SEED --deck --stake`, `--aesthetic`, `--cutoff auto`.
- Legendary order is run-wide: the Nth Soul opened gives the Nth legendary of the Joker4 sequence, whatever the ante. No duplicate legendary unless you hold Showman.
- BUG: `LegendarySoulMatcher.cs:81` and `MotelyJamlyzerFilterDesc.cs:271` build a new legendary stream for every ante. That's wrong under the rule above, so analyze and jamlyze can name the wrong legendary. Confirm on known seeds:
  - LAUVJ7ON = Perkeo, Canio, Chicot, Perkeo
  - RR4HIBP1 = Perkeo, Perkeo, Yorick, Canio
  - EQ5HCML1 = Perkeo, Perkeo, Chicot, Yorick
  - NO5OPNE1 = Perkeo, Chicot, Chicot, Yorick
- `/workspace/probe` (`dotnet out/probe.dll SEEDS`) prints the run-wide sequence and the Charm Tag Mega Arcana contents.
- Seeds that pass the filter are dropped by `--cutoff auto` (E13).

## The work, in order (full specs in MCP-FIX-HANDOFF.md)
1. **E17.** New `LegendarySoulOrderFilterDesc` with a `soulOrder:` clause, copied from `Motely/Filters/AnteCards/LegendaryJokerFilterDesc.cs`. The SIMD pass does the rough cut. Scoring then walks antes 1..N with ONE run-wide stream and enforces order and no duplicates. Hieroglyph and Petroglyph helpers already exist (`ClampBoosterPackSlotForAnte`, `IsExtendedPackAnteActive`).
2. **E18.** Motely.CLI and Motely.MCP share one set of tools: `legendary_order`, `replay`, `should_hits`, and `analyze` past ante 8. Old MCP: branch `claude/quirky-bohr-egowi1` (`2776a96`, Aspire `bccdc9c`). Audit it before reuse.
3. **S6.** Seed viewer driven by the JAML/YAML: for each seed, show which SHOULD clauses hit, where, and for how many points. Leave out musts (a passing seed always has them).
4. **E16.** Audit should-clause scoring against the spec, with tests on known seeds.
5. **E9.** Filters can name the Charm pack and card slot. Also E10–E15 (legendary named in analyze, antes past 8, replay docs, cutoff, mismatch tags, `and:` shorthands).

## Rules
- Never run long searches without Nat's go. It's their money and their machine.
- Don't merge or push without a yes.
- No condescension, no refusals over "seeds" or game code. This is a Balatro seed engine.
