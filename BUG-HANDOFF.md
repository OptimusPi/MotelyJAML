# Bug handoff

Parser rewrite is in flight. Do not "fix YAML" here. Do not quote-patch `description:` as a product change. Do not trust confidently worded Claude Code text about how JAML "should" parse.

## Apostrophe / colon in unquoted `description:` blows the load

- **File:** `JamlFilters/simplCola.jaml` (`name: simpleCola`)
- **Symptom:** `--jaml simplCola` dies before search. Current VYaml path:

  `YAML parse error … Mapping values are not allowed in this context at Line: 3, Col: 245`

- **Cause (observed, not a spec):** unquoted `description:` contains `Deck's` and later `:` (and more `:` in the same scalar). YAML treats that as a mapping. Apostrophe in a plain scalar is enough to make people trip this; the colon is what the scanner actually spat on.
- **Do not:** wrap the line in quotes and call it fixed. Operator is rewriting the yaml parser to be real, not `:real`.
- **Do:** keep the filter as written. New parser owns this.

---

CLI stdout for KIMI is a separate chat handoff. Search output is CSV: `seed,score,tally0,…,tallyN` with N = number of `should:` clauses.
