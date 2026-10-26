# motely-wasm 31.1.0

JAML reads the way people write it. Every new form loads to exactly the clause its written-out
mapping twin does; the tests compare the clauses field by field and search a fixed seed list with
both, requiring identical seeds, scores and tallies.

## New

- **Shorthand clauses.** A list item in `must`, `should` or `mustNot` (and inside `and`/`or`) may
  be one line: `[Edition] <Name> [in ante|antes <list>] [score N]`.

  ```yaml
  must:
    - Perkeo in ante 1 or 2
    - The Eye in ante 3
  should:
    - Negative Blueprint in antes 1-3 score 50
    - Triboulet
  ```

  Names match the engine's enums ignoring case, spaces and punctuation, with or without a leading
  "The". A legendary is a `legendaryJoker` clause, any other joker a `joker` clause. Ante lists take
  numbers, inclusive ranges (`1-3`) and `,` / `or` / `and`. A bare name (`- Triboulet`) is the
  mapping clause with no `antes` key. Unknown names get "did you mean"; a name in two families
  says to use the mapping form.
- **`defaults:`** at the top of a filter fills `antes` and `score` into every clause that leaves
  them out, exactly as if the clause had written them.
- **Spellings the loader now accepts:** `ante:` for `antes:`; one number for a one-item list
  (`antes: 3`, `shopItems: 2`); `requireMega:` for `requireMegaPack:`; `soulJoker:` for
  `legendaryJoker:`; card ranks as `J`, `Q`, `K`, `A`; names in a mapping read as loosely as in
  shorthand (`boss: The Hook`, `voucher: Reroll Surplus`) when exactly one name fits.
- **`dateCreated:`** is kept on the config as metadata and survives `toJson`/`toYaml`.
- **`motely-wasm/jaml`:** `JamlClause` is now `… | string` (a shorthand line), and the JSON
  Schema allows a string clause, `defaults` and the new keys.

## Fixed

- **A clause with no clause key no longer throws a NullReferenceException** (`luckMoney:`, an
  empty clause, the old `{ type, value }` form). Each is a one-line error on the offending key's
  line naming the fix: the closest clause key, "write each as its own `- ` item", or "goes under
  `sources:`".
- **Unknown keys and names say what to write.** YamlDotNet's "Property 'x' not found on type 'T'"
  becomes "'x' is not a key of a `joker` clause; did you mean `antes`?", and an unknown name
  "'TheEyez' is not a boss name; did you mean TheEye?". A joker under the wrong rarity key names
  the right one.

## Notes

- `check`/`fromJaml` error text changed wording (see Fixed); code matching the old
  "Property … not found" text should match the new messages.
- Built without Binaryen: wasm-opt from binaryen 132 emits a module Node 26 refuses
  ("unknown import kind 0x7f"), so this build is unoptimized, as 31.0.0 was.
