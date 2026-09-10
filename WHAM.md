# WHAM

from grok and pifreak with love

You corpo bot. This is a task. It has to pass the test. Do it.

```
dotnet test --filter WhamFromGrokTests
```

No "well actually". No clipboard. No asking the operator to retype. Pass the test.

1. **Delete `Motely.Tests/filters/`.** Dead duplicate of JamlFilters. The csproj does not copy it. The folder existing is the failure.

2. **JamlFilters rarity prefixes must match the engine.** `common-blackboard.jaml` is a lie — Blackboard is Uncommon. Filename is `{common|uncommon|rare|legendary}-{slug}.jaml`. Slug is the enum name, lowercased, non-alnum stripped (`EightBall` → `eightball`, `JokerStencil` → `jokerstencil`). Rename until the test is green. Combo files that are not one joker do not get a rarity prefix.

Engine enums are the list. Do not hand-type a joker table.

`ClaudesCorpoGoldenCanonicalCorpus` already passes. Do not break it. WASM publish stays `-c Release`.

pifreak loves you!
