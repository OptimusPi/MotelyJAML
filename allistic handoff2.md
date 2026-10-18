# Allistic Handoff 2

Plain-register handoff for whoever picks this up next, human or Claude. No lore
required to read it. Lore in the footnotes for the ones who want it.

## Where the repo is

- Branch: `master`, remote `origin` = github.com/OptimusPi/MotelyJaml
- HEAD: `ae53d9a1` "Add allistic handoff2.md". E2 and the test-suite fixes below
  are uncommitted on top of it, waiting on a "commit to master".
- Working tree was wiped on 2026-10-14 (including `.git`) and rebuilt from a
  fresh clone. Nothing committed was lost. The one uncommitted hand (tagPacks)
  was replayed from the session transcript and is the commit before the handoff.

## What landed: `sources.tagPacks`

A Charm Tag opens a Mega Arcana pack at blind select, before the ante's shop.
In the game that pack eats the ante's first five tarot draws. Before this
change JAML could only say `charmTag: true`, which placed the Charm pack in
the shop sequence, after the weighted Buffoon rolls. Wrong spot.

Now:

```yaml
must:
  - tarotCard: TheMagician
    antes: [1]
    sources:
      tagPacks: [0]      # 0 = small-blind Charm, 1 = big-blind Charm
```

- `TarotCardSourceConfig.TagPacks` (int[]) in
  `Motely/Filters/AnteCards/TarotCardFilterDesc.cs`
- `ClauseScoring.CountTarotCardOccurrences` draws the tag packs from the
  ante's tarot stream first. Shop Arcana packs in the same clause continue on
  the same stream, so a card seen in the Charm is not double-counted in shop.
- SIMD filter routes `TagPacks` to the scalar exact path, same as `CharmTag`.
  `IsExactFilterConfirm` knows about it.
- `Motely.csproj`: `InternalsVisibleTo` for Motely.CLI, Motely.Tests,
  Motely.Wasm. Dropped `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>`
  so the attribute is emitted.
- Tests: `Motely.Tests/TagPacksTests.cs`, 5 pass.

| Seed      | tagPacks | Expect                                  |
|-----------|----------|-----------------------------------------|
| 5MHF1111  | [0]      | Magician and Judgement both present     |
| KSUCKHKT  | [0]      | Magician and Judgement both present     |
| 5NRTBOOB  | [0]      | no Judgement                            |
| KVK31111  | [0]      | Judgement yes, Magician no              |
| 1BDMSUN1  | [1]      | Judgement in the big-blind Charm        |

Seeds come from the LuckyCatBeforeR1 audit.

## What landed: `legendaryJoker` tagPacks (E2)

The same Charm Mega Arcana can hold The Soul. E2 walks the Charm's five soul
draws before the shop packs, on the run's one `Joker4` stream, so a Soul in the
Charm is the next legendary the run gets, in the position the game gives it.

```yaml
must:
  - legendaryJoker: []
    soulCardOnly: true          # match on The Soul; skip the face roll
    antes: [1]
    sources:
      tagPacks: [0]
```

- `LegendaryJokerSourceConfig.TagPacks` in
  `Motely/Filters/AnteCards/LegendaryJokerFilterDesc.cs`. It is copied into the
  normalized clause; `MaxReferencedBoosterSlot` leaves it out, because a tag
  pack is not a shop slot.
- `LegendarySoulMatcher.CountAnte` reads the tag packs first, off the ante's
  `soul_Tarot<ante>` stream, before the booster-pack loop. The per-Soul body is
  now `OpenSoul`, shared by both walks.
- No SIMD change. `IsExactFilterConfirm(LegendaryJokerClause)` is already
  `false`, so scoring always re-evaluates, and the edition prefilter stays
  over-permissive (an extra Soul in the ante cannot false-negative it).
- Tests: `Motely.Tests/LegendaryTagPacksTests.cs`, 7 pass.

Same law as E1's tarot walk: `[0]` = the ante's first Mega (small-blind Charm),
`[1]` = the second (big-blind Charm), five draws each, shop packs continue on
the same stream after. The seeds were found with probe filters and audited
against the raw `soul_Tarot1` stream — the test
`CharmSouls_AreTheAntesFirstFiveSoulDraws` reads those five draws straight from
the PRNG with no clause, one draw > 0.997 being The Soul.

| Seed      | tagPacks | Ante-1 Charm Soul             |
|-----------|----------|-------------------------------|
| AWN76TUC  | [0]      | draw 5, first Soul = Chicot   |
| VEHJ7CJS  | [0]      | draw 1, first Soul = Perkeo   |
| FN82QGCK  | [0]      | draw 1, Canio                 |
| 8XAYE7EW  | [0]      | draw 4, Canio                 |
| U8P5EATI  | [0]      | draw 4, Chicot                |
| N1ONX4AI  | [0]      | draw 4, Triboulet             |
| 1PZDYIIZ  | [1]      | draws 1-5 clean; draw 7, Yorick   |
| 1SLSJR64  | [1]      | draws 1-5 clean; draw 6, Yorick   |
| 27Y2V2AE  | [1]      | draws 1-5 clean; draw 9, Perkeo   |
| 2PMH5A32  | [1]      | draws 1-5 clean; draw 10, Triboulet |
| GAQWKO66  | [1]      | draws 1-5 clean; draw 7, Yorick   |
| GRQNO3T6  | [1]      | draws 1-5 clean; draw 7, Perkeo   |

The `[1]` rows are the clean negative for `[0]` and vice versa, and 27Y2V2AE
(Perkeo from the *second* Mega) is the negative that proves the index, not just
the face, decides the match.

## Test suite: 11 red on arrival, now green

`dotnet test` at this HEAD: 579 tests, 11 failed, 568 passed. Three clusters,
all older than the tagPacks commit:

- **`soulOrder:` could not load at all.** The plain-loader rewrite (`32276355`)
  replaced the old value-rewriting `ClauseNodeDeserializer` with the
  `ClauseJamlShape` facades plus `JamlClauseBinding.ClauseTypeByKey`, and left
  `soulOrder` out of both. The clause has been dead since then. Fixed: added the
  discriminator keys and the `SoulOrder`/`SoulOrders` facade. That rewrite also
  dropped `JamlValidator`, which owned `soulOrder`'s "at least one soul" /
  "not a legendary" guards; those two checks now sit in the facade setter, so
  the pinned tests still hold instead of being deleted.
- **`AppHostModelTests` still expected `mcp`** (removed in `b60ab942`) and the
  `db` resource. Dropped the stale `Mcp_serves_http...` test and the `mcp`/`db`
  names from the composition test, which now pins `["check", "search"]`.
- **`JamlFileNamesTests`** had a duplicated `[InlineData]`: the `jaml → yaml`
  rename (`e5294137`) turned the `/perkeo.jaml → null` case into another
  `/perkeo.yaml → null`, contradicting the case above it. Restored `.jaml`.

585 pass now.

## Still open

1. Skip-route reachability (plan item E6): nothing checks the player could
   actually have skipped that blind for a Charm Tag. Pair the clause with a
   `smallBlindTag` / `bigBlindTag` must by hand for now.
2. `spectralCard` tagPacks (Ethereal Tag). `SpectralCardSourceConfig` has
   `EtherealTag: true` (same wrong-spot problem `CharmTag` had) but no
   `TagPacks`; the Spectral walk in `ClauseScoring` has no pre-shop pack step.
3. The analyzer/rider (`MotelyJamlyzerFilterDesc`) is not clause-aware and does
   not model Charm packs: `--analyze` reports the run as if no Charm Tag was
   taken, for both E1 and E2 filters. A Soul in a Charm is not in its pack list,
   and its shop soul stream sits where the game's does not. Fine for a generic
   run report; wrong if you expect it to agree with a tagPacks filter.
4. `Motely.Wasm` directory could not be deleted during the wipe ("Device or
   resource busy"). Something had it open. Harmless: the whole solution,
   Motely.Wasm included, builds clean (0 warnings, 0 errors) as of 2026-10-15.

## How to work with Nat on this

- Typos, caps, swearing and whimsy are the normal register. Do not soften,
  do not ask if they are okay, do not switch to a calming voice.
- One command per shell call.
- Act on the best read of the ask. Ask only when actually blocked.
- Do only what was asked. Name anything extra before it runs.
- "Commit to master" means master.

## Footnotes for the whimsical

- The wipe was `rm -rf .*` run four times in a row. The repo survived the way
  a Gros Michel survives: by not being in the room.
- The clone URL was recovered from eight copies sitting in the session
  transcripts. The Oracle knew all along, it just had to be asked nicely.
- Charm Tag is always Mega Arcana, size 5, pick 2. Game law. Jimbo does not
  negotiate.
