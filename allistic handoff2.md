# Allistic Handoff 2

Plain-register handoff for whoever picks this up next, human or Claude. No lore
required to read it. Lore in the footnotes for the ones who want it.

## Where the repo is

- Branch: `master`, remote `origin` = github.com/OptimusPi/MotelyJAML
- HEAD: `943c2580` "Add tagPacks tarot source for Charm Tag Mega Arcana"
- Working tree was wiped on 2026-10-14 (including `.git`) and rebuilt from a
  fresh clone. Nothing committed was lost. The one uncommitted hand (tagPacks)
  was replayed from the session transcript and is now the commit above.

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

## Not done

1. Full test suite has not been run against this commit. Only the filtered
   TagPacks run. Do `dotnet test` before trusting it for anything else.
2. `legendaryJoker` tagPacks (plan item E2). Not started.
3. Skip-route reachability (plan item E6): nothing checks that the player
   could actually have skipped that blind for a Charm Tag. Pair the clause
   with a `smallBlindTag` / `bigBlindTag` must by hand for now.
4. Motely.Wasm directory could not be deleted during the wipe ("Device or
   resource busy"). Something had it open. Harmless, but if a stale build
   shows up there, that is why.

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
