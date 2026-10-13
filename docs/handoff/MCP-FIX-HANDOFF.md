# MCP fix handoff: MotelyJAML engine + seedfinder.app MCP

For: coder-planner. Written 2026-10-11 (CT) by reading code and Balatro Lua. **No source in either repo was edited.**
Repos: `OptimusPi/MotelyJAML` @ master `bc0d40a` (v29.0.0); `OptimusPi/seedfinder.app` @ `a170d23` (pins `motely-wasm` 28.0.0).
Lua is cited from the user's Balatro.app (`Contents/Resources/game`). Probe used for the evidence: `/workspace/probe/Program.cs` (a separate console app that calls the engine's own stream APIs, not part of either repo). Its output is in `/workspace/whimsy/lcperkeo_probe.txt`.

Ground rules: (1) A seed the engine returns passed the filter as written. The bugs below are about **what a clause means** vs. what the game does. (2) Don't trust existing comments. Several are wrong (cited below).

## Lua facts everything below depends on
| Fact | Lua |
|---|---|
| Charm Tag always opens a **Mega Arcana** at blind select, right when the blind is skipped. It doesn't depend on shop pack rolls. | `tag.lua:209-216` |
| Arcana pack contents come from `create_card('Tarot',…,'ar1')`, so the key is `Tarotar1<ante>`, and draws happen **when a pack is opened**, in open order | `card.lua:1732-1735`, `common_events.lua:2010-2011,2091` |
| Soul roll: `pseudorandom('soul_Tarot'..ante) > 0.997`, skipped while a `c_soul` card exists | `common_events.lua:2130-2135`, `card.lua:352` |
| Legendary pool key is `Joker4` with **no ante suffix**, so it is one sequence for the whole run | `common_events.lua:2010,2091` |
| Owned jokers (and the Soul card while it exists) become `UNAVAILABLE` and get resampled with `<key>_resample<n>` (n starts at 2) | `common_events.lua:2026,2073,2155-2160`; `card.lua:352` set, `card.lua:4829-4835` clear |
| `enhancement_gate`: Lucky Cat (`j_lucky_cat`, `enhancement_gate='m_lucky'`, `unlocked=true`) is `UNAVAILABLE` unless a Lucky card is in `G.playing_cards` | `game.lua:467`, `common_events.lua:2051-2056` |
| Judgement/Soul: `create_card('Joker',…, legendary=(Soul), …, 'jud'/'sou')` | `card.lua:1416-1419` |
| `pseudorandom_element` indexes the full pool, `UNAVAILABLE` placeholders included, so a raw draw that lands on an available item is the real result | `misc_functions.lua:256-270` |
| The first shop's Buffoon pack is forced **without** consuming the `shop_pack` stream | `common_events.lua:1983-1987` |
| The ante goes up at end of the boss round, **before** that shop. The post-boss shop uses ante N+1's streams. | `state_events.lua:269` |
| Edition tags (Negative etc.) apply on `store_joker_modify` to the first shop card that is a Joker **with no edition**. One tag per card, in `G.GAME.tags` order. Runs on every generated shop card, rerolls included. | `tag.lua:393-409`, `UI_definitions.lua:800-833`, `game.lua:3467` |

Consequence of the last two rows: with the small blind skipped, ante 1 has **one** shop (after the big blind) showing engine pack slots 0 (Buffoon) and 1. Slots 2 and 3 are only reachable if the small blind is played. With both blinds skipped, ante 1 has no shop at all.

---

## ENGINE (MotelyJAML C#)

### E1. Charm Tag pack model is wrong (bug)
- **Problem:** `charmTag: true` models the Charm Mega only when the first two non-Buffoon shop packs are not Arcana, and puts it at the second one's slot index. Otherwise it scores natural Arcana packs from a fresh `ar1` stream, as if no Charm pack had been opened. In the game the Charm Mega is always opened first and consumes `ar1` draws 1-5 (draws 6-10 for a second Charm on the big blind). Shop Arcana packs come after it. A second Charm tag isn't modeled at all.
- **Evidence:** `Motely/Filters/ClauseScoring.cs:564-622` (`CountTarotCardOccurrences`, the `charmWant && !hadNaturalArcanaPack && weightedShopDrawNumber == 2` branch). Wrong doc comment at `Motely/Filters/AnteCards/TarotCardFilterDesc.cs:394-397` ("second weighted slot, no natural Arcana"). The SIMD path defers to the same code (`TarotCardFilterDesc.cs:106-116`). The spectral `etherealTag` path copies the pattern (`ClauseScoring.cs:683-…`), so check it against `tag.lua` too.
- **Change:** model tag packs as a per-ante **pre-shop pack sequence**: small-blind tag pack, then big-blind tag pack, then shop packs. All of them draw from the same `ar<ante>`/`soul_Tarot<ante>` streams in that order. Give it its own source key, e.g. `sources: { tagPacks: [0] }` (0 = small-blind tag pack, 1 = big-blind tag pack), for `tarotCard`, `spectralCard` (Ethereal) and `legendaryJoker`. Skipping the small blind should also make shop slots 2-3 unreachable for that ante (see E6).
- **Acceptance** (filter `/workspace/whimsy/LuckyCatBeforeR1.yaml` rewritten to use `tagPacks: [0]` for Magician and Judgement):
  - must match **5MHF1111** (real Charm: Star, Emperor, **Magician**, Tower, **Judgement**) and **KSUCKHKT** (World, Fool, Star, **Judgement**, **Magician**).
  - must **not** match **5NRTBOOB** (Charm: Star, Fool, High Priestess, Chariot, Magician, so no Judgement) or **KVK31111** (Charm: Death, Empress, Judgement, Fool, Tower, so no Magician). Both pass today.

### E2. `legendaryJoker` has no Charm/tag-pack source (gap)
- **Problem:** `LegendaryJokerSourceConfig.SourceKeys` = `boosterPacks, arcanaPacks, spectralPacks, requireMega(Pack)`, with no `charmTag`. `LegendarySoulMatcher.CountAnte` walks shop packs only, with a fresh soul stream. When it does find a Charm Soul, it reports it under the slot of the first shop Arcana pack. The `RequireMegaPack` comment "(e.g. Charm Tag Mega arcana)" is false: it only checks shop pack size.
- **Evidence:** `Motely/Filters/AnteCards/LegendaryJokerFilterDesc.cs:141-162`; `Motely/Filters/AnteCards/LegendarySoulMatcher.cs:39,63` (`CreateBoosterPackStream`/`CreateArcanaPackTarotStream` from slot 0, no tag pack).
- **Change:** add `tagPacks` (E1) to legendary sources and walk the tag packs first.
- **Acceptance:** `legendaryJoker: Perkeo, antes:[1], sources:{tagPacks:[0]}` matches **QSEXYM36, 1BDMSUN1, DONGCZB1** (Soul is in the small-blind Charm) and does **not** match **KSUCKHKT** or **NUDEM1LM**. With `tagPacks:[1]`, it matches **NUDEM1LM** (its second Charm holds the Soul).

### E3. Legendary order: per-ante clause restarts the run-global `Joker4` stream (bug + new clause)
- **Problem:** `FixedRarityJoker(Legendary,…)` returns `"Joker4"` with no ante, which is correct. But `CountAnte` calls `CreateLegendaryJokerStream(ante)` fresh in every ante, so every ante's first Soul reads `Joker4` draw #1. `legendaryJoker` over several antes therefore counts the first legendary again and again. The legendary pool also ignores ownership: a second Perkeo while you still own the first is `UNAVAILABLE` and resamples to `Joker4_resample2`, unless you have Showman.
- **Evidence:** `Motely/MotelyPrngKeys.cs:91`; `LegendarySoulMatcher.cs:81,123`; `Motely/Analysis/MotelyJamlyzerFilterDesc.cs:271` (jamlyze `pulls.legendaryJokers` shows the same global list under every ante). Lua `common_events.lua:2010,2026,2091`.
- **Change:** (1) Create one legendary stream per seed evaluation and thread it across antes in pack/open order, in `LegendarySoulMatcher` and in the jamlyzer. (2) New clause `legendaryOrder: [Perkeo, Perkeo]` (optional `souls: [0,1]`) meaning "the Nth Soul the player opens becomes X", independent of ante. Add an `owned:` option, default game-accurate: earlier legendaries count as owned unless Showman is active, so a duplicate resamples.
- **Acceptance:** raw `Joker4` sequences from the probe: KSUCKHKT Perkeo>Perkeo>Chicot, DONGCZB1 Perkeo>Perkeo>Perkeo, 32YORGYN Perkeo>Perkeo>Perkeo, 8WANKGMP Perkeo>Canio, **43GS2F56 Perkeo>Yorick>Yorick>Yorick**, TFWYAE83 Perkeo>Canio. With `owned: none`, `legendaryOrder:[Perkeo,Perkeo]` matches KSUCKHKT, DONGCZB1 and 32YORGYN, but not 8WANKGMP or 43GS2F56. With the default (owned), the second slot must come from `Joker4_resample2`. Regression: `/workspace/whimsy/legend_probe.csv` currently claims 43GS2F56 gets "four Perkeos, antes 2/3/6/8". After the fix it must report Perkeo then Yorick.

### E4. Enhancement gate and ownership aren't modeled for Judgement, Soul or shop (gap; doc fix at minimum)
- **Problem:** the engine returns raw draws and never applies `enhancement_gate`, owned-joker `UNAVAILABLE`, or resample. There are no hits for `enhancement_gate`, `UNAVAILABLE` or `used_jokers` anywhere under `Motely/`.
- **What that means today:** `joker: LuckyCat, sources:{judgement:[0]}` means "the first Judgement used **while the ante is 1** (key `rarity1jud`/`Joker2jud1`, which resets each ante: `MotelySingleSearchContext.Jokers.cs:117-130`, `ClauseScoring.cs:1745-1752`) raw-rolls Lucky Cat". It is **not** "first Judgement in the run". That hit is real only if a Lucky card is in the deck at that moment and you don't already own Lucky Cat (`pseudorandom_element` keeps placeholders, so the index is the same). Without a Lucky card the game rerolls with `Joker2jud1_resample2`. The same applies to shop and Buffoon Lucky Cat and to other gated jokers (Glass Joker / `m_glass`, Steel Joker / `m_steel`, Stone Joker / `m_stone`, Golden Ticket / `m_gold`).
- **Change:** minimum: document "raw roll, gate assumed satisfied" on the `judgement`/`shopItems` sources and in `learn_jaml`. Better: a `gate:` option (`assumeMet` default | `resample`). With `resample`, a gated miss rolls the `_resample<n>` key the way the game does. Same for owned jokers once E3's ownership tracking exists.
- **Acceptance:** `joker: LuckyCat, antes:[1], sources:{judgement:[0]}, gate: resample` with no Lucky-card source in the filter must **not** match 5MHF1111. With `gate: assumeMet` it does (raw rolls: LuckyCat > SmileyFace > SpaceJoker).

### E5. Negative Tag → joker linkage (new clause)
- **Problem:** no clause ties an edition tag to the card it lands on. Cola Oops Lite checks the tag and Oops in slots 2-8 separately. Nothing models (a) the first **edition-less Joker** in shop order getting the tag, (b) skipped Foil/Holo/Poly jokers, (c) held tags carried over, or (d) a double skip. On a double skip the first shop seen is the **next ante's** queue from slot 0 (`state_events.lua:269`). That settles the open question in chat: after skipping both ante 4 blinds, the tag lands in the ante 5 queue.
- **Evidence:** the only engine Negative Tag references are tag-generation resampling (`MotelySingleSearchContext.Tags.cs:17`, `MotelyVectorSearchContext.Tags.cs:48`). Lua `tag.lua:393-409`, `UI_definitions.lua:800-833`.
- **Change:** new clause e.g. `editionTagTarget: { tag: NegativeTag, joker: OopsAll6s, tagAnte: 4, skips: [small,big], maxRerolls: N }`. Walk the shop queue of the first shop actually visited. Skip non-jokers and jokers that already rolled an edition. Account for `store_joker_create` tags (Rare/Uncommon Tag) creating the first joker. Report the queue position. Multiple held edition tags apply to successive edition-less jokers.
- **Acceptance:** compute by hand from the analyzer for TFWYAE83 (Negative Tag ante 4). With `skips:[small,big]`, the walk must start at **ante 5** slot 0 (Flower Pot, Hiker, Justice, Oops per earlier analysis), and the clause must return the first edition-less joker (Flower Pot unless it rolled an edition), not Oops. For M7JBOO91 ante 4, the Foil Blue Joker at slot 3 must be skipped.

### E6. Ante-1 pack reachability ignores skips (bug in filter semantics)
- **Problem:** `boosterPacks: [0..3]` in ante 1 always counts slots 2-3 (`ClampBoosterPackSlotForAnte`, `ClauseScoring.cs:2261-2275`; `SimdPackSupport.cs` SlotReachableMask). If the filter also requires skipping the ante 1 small blind (any `smallBlindTag` route), slots 2-3 can't be seen in ante 1. Every Charm filter in `/workspace/whimsy` counts them anyway.
- **Change:** add a run-route notion (`skips: [small]` at filter or clause level) that limits reachable shop slots per ante (skip small → ante 1 slots 0-1; skip both → none), and also order tag packs before shop packs (E1).
- **Acceptance:** `LuckyCatPerkeoR1.yaml` + `skips:[small]`: KSUCKHKT's Soul (slot 2) must stop counting.

### E7. Analyzer / jamlyzer don't show tag packs (bug)
- `--analyze` and `MotelyJamlyzerFilterDesc.GetPackContents` (`:543-560`) open shop packs only. For a Charm seed, the first shop Arcana pack displays the Charm's cards (e.g. QSEXYM36 shows "Jumbo Arcana – Emperor, Soul, Magician, Judgement, Hierophant", which is the Charm Mega; the real shop pack is Sun, Fool, Star, Empress, Justice). Add a `tagPacks` section, and list shop pack contents as they'd appear after tag packs are opened.
- **Acceptance:** `--analyze QSEXYM36 --deck Ghost` shows a "Charm Tag – Mega Arcana" entry with those 5 cards, and the ante 1 Jumbo Arcana as Sun, Fool, Star, Empress, Justice.

### E8. Keyword-less `--collect` (observation, from dogfooder log)
- `Motely.CLI --collect 1` with no keyword runs `sequential` from the first seed, not across aesthetic families. Either document this or add `--aesthetic all` as the no-keyword default when `--collect` is set. Decide with Nat.

---

## seedfinder.app MCP (TS)

### S1. `learn_jaml` Lucky Cat corpus entry teaches a filter that finds unbuyable cats
- **Evidence:** `corpus/jokers/luckycat.jaml:4-15`. Its must clause is `joker: LuckyCat, antes:[1], shopItems:[0..3], boosterPacks:[0,1]`, and its own description admits the gate blocks real shops. It ships via `lib/corpus.json` (slug `luckycat`, `:1767`) and `lib/corpus.ts` `searchCorpusScored`.
- **Change:** replace it with the Charm route, and state the gate and timing:
  ```yaml
  must:
    - smallBlindTag: CharmTag
      antes: [1]
    - tarotCard: TheMagician          # use first: 2 Lucky cards satisfy enhancement_gate
      antes: [1]
      sources: { tagPacks: [0] }      # until E1 lands: { boosterPacks: [0,1,2,3], charmTag: true } + caveat below
    - tarotCard: Judgement
      antes: [1]
      sources: { tagPacks: [0] }
    - joker: LuckyCat
      antes: [1]
      sources: { judgement: [0] }     # raw roll; valid only after Magician (E4)
  ```
  Until E1/E6 ship, the entry must say the engine's `charmTag` modeling is approximate and that hits should be checked (5NRTBOOB and KVK31111 are known false routes). Also fix `corpus/tags/charm.jaml:4-5` ("First-shop skip reward" is fine; add "opens immediately, before the shop, and consumes the ante's first Arcana draws"). Then regenerate `lib/corpus.json` / the catalog (`scripts/build-jaml-catalog.mjs`).
- **Acceptance:** `learn_jaml query:"lucky cat"` returns the Charm route first. Running it returns 5MHF1111 and KSUCKHKT and (after E1) not 5NRTBOOB.

### S2. Backward-planning tool (new)
- **Problem:** `plan_seed_search` (`lib/mcp/tools/plan-search.ts:13-40`) only returns vocab and corpus matches. It doesn't map "item X by time T" to a source chain. Nat had to lead the reasoning by hand.
- **Change:** new tool `plan_route { goal: <item>, by: "beforeRound1" | "ante:N" | "anteN:shop" }`. It needs a static source graph per item type: joker ← shop / Buffoon pack / Judgement / Wraith / Rare-Uncommon Tag / Riff-raff; Judgement ← Arcana pack / shop / Emperor / purple seal; Arcana pack ← shop slot / Charm Tag. It also needs a timing table: before round 1 = tag packs only (skip small/big); ante-1 shop after skip = slots 0-1. Add gates (`enhancement_gate`, `min_ante` for tags, e.g. Negative Tag `min_ante=2`, `game.lua:230`) and output ranked chains plus JAML. Data should come from the engine vocab, not hand-typed strings.
- **Acceptance:** `plan_route {goal: LuckyCat, by: beforeRound1}` returns Charm Tag → Mega Arcana → Magician then Judgement → `judgement:[0]`, with the gate note. It must not suggest a shop clause.

### S3. Expose soul stream / legendary order clearly in `jamlyze_seeds`
- **Evidence:** `lib/mcp/jamlyzer.ts:197-208,256-271` formats `pulls.legendaryJokers` per ante. The engine fills it from a per-ante restart of global `Joker4` (E3), so every ante shows the same list, and that looks like a per-ante legendary.
- **Change:** move it to a top-level `legendaryOrder: [...]` per seed ("Soul #1 → X, #2 → Y"), drop it from per-ante pulls, and add an `include: ["route"]` view that lists tag packs (E7) then shop packs, in open order. Update the tool description at `lib/mcp/tools/analyze.ts:116-122`.
- **Acceptance:** `jamlyze_seeds` on 43GS2F56 returns `legendaryOrder: [Perkeo, Yorick, Yorick, Yorick]` once, not per ante.

### S4. Durable searches on Nat's Ryzen (self-hosted worker)
- **Current state:** Search Party coordination exists: lease/report/status over Neon (`lib/party/coordinator.ts`, `lib/party/store.ts`, `app/api/party/{next,report,status}`, `workflows/party/`). `lib/party/PROTOCOL.md:3-6` says the worker half is `Motely.DistributedWorker` in MotelyJAML, but that project is **not on master**. It was removed in later commits (history: `fc3fde9` "MotelyHome + MotelyWorker + Motely.MCP", `836c660` "yeet"). So no native worker can join a party today. The app pins `motely-wasm` 28.0.0 (`package.json:40`, `lib/mcp/engine-run.js:1-36`), while the engine is v29.0.0.
- **Change:** (1) Ship a native lease worker against v29 (C# console or a `Motely.CLI --party <url> --partyId` mode) that heartbeats every 20s, runs `withStartBatchIndex/EndBatchIndex` per lease, and reports seeds. Run it as a systemd/Windows service on the Ryzen. (2) Add a "private party" (owner-only, `maxWorkers`, owner token) so Nat can queue his own long searches without paging the community. (3) Bump wasm to v29 after E1-E3 so server-side confirmation (`reportFinds` → `matchSeedsToJaml`) agrees with the worker.
- **Acceptance:** create a private party for `ColaOopsLite_a8.yaml`, kill the worker mid-lease, restart it, and the lease reopens after 60s (`DEFAULT_LEASE_TTL_MS`) with no gap in `blocksDone`. A Ryzen-reported seed is confirmed by the server.

### S5. Audit `claude/quirky-bohr-egowi1` before any merge (MotelyJAML)
- **What it adds** (4 commits on top of `bc0d40a`, `git diff --stat master...origin/claude/quirky-bohr-egowi1`): `Motely.MCP/` (MotelyTools.cs 323 lines, Searches.cs 195, Program.cs 149), `Motely.AppHost`, `Motely.ServiceDefaults`. It also deletes `ducklake.sqlite`. Commit `bccdc9c` says "(untested)".
- **Findings so far:** `Searches` is in-memory only (`ConcurrentDictionary`, "Searches live until the process exits"), so it isn't durable and doesn't meet S4. HTTP mode refuses non-loopback binds without `--Mcp:AuthServer` (good). It inherits every engine semantics bug above, because it calls `YamlConfigLoader`/`MotelySearchBuilder` directly.
- **To do:** build it, run its 4 tools against the acceptance seeds here, and decide whether it replaces S4's worker or sits on top of it. Don't merge until E1-E3 tests pass through it.

---

## Task A record (why E1/E2/E6 matter): LuckyCatPerkeoR1 seeds checked against the real game
Real-game route = skip ante 1 small blind → Charm Mega (pick 2) → shop after big blind shows only slots 0-1. "Charm" below = real Charm contents (ar1 draws 1-5).
| Seed | Charm (pick 2) | Reachable ante-1 shop pack (slot 1) | Class | Lucky Cat before R1 | + Perkeo before R1 | + Perkeo in ante 1 |
|---|---|---|---|---|---|---|
| QSEXYM36 | Emperor, **Soul**, **Magician**, **Judgement**, Hierophant | Jumbo Arcana: Sun, Fool, Star, Empress, Justice | a | yes | no (pick 2) | no |
| DONGCZB1 | **Soul**, Emperor, Chariot, **Judgement**, **Magician** | Normal Arcana: Justice, Magician, Temperance | a | yes | no | no |
| 1BDMSUN1 | Moon, **Soul**, **Magician**, Sun, **Judgement**; big-blind tag is also Charm: High Priestess, Lovers, Temperance, Death, **Judgement** | Mega Celestial | a → playable by double skip | yes | **yes**: skip both, Charm 1 Magician + Soul, Charm 2 Judgement | yes |
| KSUCKHKT | World, Fool, Star, **Judgement**, **Magician** | Standard (Soul is in slot 2, unreachable when skipping) | c | yes | no | no |
| 8WANKGMP | Fool, Judgement, Soul, Hierophant, Sun (no Magician) | Normal Arcana: Fool, Star, Magician | c | no | — | — |
| ML5WANK4 | Strength, Hermit, Death, Chariot, Tower | Jumbo Arcana (pick 1): Magician, Lovers, Soul, Chariot, Judgement | c | no | — | — |
| Z5MOWANK | Moon, Devil, Sun, Soul, Judgement (no Magician) | Normal: Judgement, Magician, Moon (pick 1) | c | no | — | — |
| 32YORGYN | Death, Lovers, Wheel, Chariot, Judgement | Normal: Devil, Soul, Magician (pick 1) | c | no | — | — |
| NUDEM1LM | Justice, World, Hierophant, Devil, Sun; 2nd Charm: Star, HP, Hanged Man, Lovers, **Soul** | Jumbo Arcana | c | no | (Perkeo only) | — |
| 7MOANEAW | Fool, HP, Temperance, Magician, Soul (no Judgement) | Standard | c | no | — | — |
| XF2ARSEI | World, Fool, Judgement, Hermit, Hierophant | Normal: Lovers, Magician, Strength | c | no | — | — |
All 11 have `Joker4` #1 = Perkeo and Judgement ante-1 roll #0 = Lucky Cat (raw). The engine passes all 11 because of E1/E2/E6, not because of a Charm route.

## E9 (Nat, 2026-10-12): Charm pack card slots
Let JAML pick which Charm Tag pack and which card slot inside it a tarot or legendary comes from. Example: Charm #1, slots 0-4, then Charm #2, slots 0-4, for a double skip. That way a filter can require the Magician before Judgement, plus the Soul, from specific packs. Today `charmTag:true` can't tell the two packs apart or check card order.

## Pomegranite notes, 2026-10-12 evening (for the MotelyJAML finish)
- E10 `--analyze` prints "The Soul" in packs but never says WHICH legendary it gives. Show the legendary (run-wide order, Nth Soul opened) right next to each Soul so Perkeo x2 can be read straight off analyze. Example: EQ5HCML1 has Souls in ante 1 (Jumbo Spectral) and ante 2 (Jumbo Arcana), but analyze can't tell you if both are Perkeo.
- E11 Analyze and Blueprint both need to show antes past 8 by default for LongCola (Negative Tags live at ante 10+; B724BEAT has none in 1-8 but one in ante 10).
- E12 `--replay` (filter's seeds: block) and `--seeds` exist; document them in `--help` / MCP so bots use them instead of full searches.
- E13 Cutoff auto drops passing seeds (13 of 62, 9 of 203). Keep all passing seeds in a second file or add a score-pass that holds every seen joker.
- E14 Tag results with mismatch warnings where engine != game (per-ante legendary reset, Charm pack card order, Lucky-card gate).
- E15 JAML `and:` works for "Negative Tag small AND big same ante"; add a shorthand for "both blinds" and for "the joker the Negative Tag lands on".
- Backup: ducklake.sqlite catalog copied to /workspace/backups/ before the Postgres/Neon move (catalog only, not the data).

## S6 (Nat, 2026-10-12, asked for months): seed-list viewer UI
A page where you paste seeds (or a CSV/replay result) and see ONLY what you ask for. The JAML/YAML filter itself drives it: the viewer shows the SHOULD clauses (which ones hit, where, and points). Musts are not worth displaying: a seed that passed the filter always has them, so you pick what you care about by writing the filter (e.g. Negative Tags, Diet Cola, Ankh, each Soul with the legendary it gives in run-wide Nth-Soul order, copy jokers, Oops), and it shows just those hits per seed, NOT a dump of every ante. It needs filters and highlights so you can pick the best seed at a glance. seedfinder.app has two partial versions of this, but the UI is badly drawn and needs a rework. Today this takes jamlyze calls, the probe tool and Blueprint, stitched together by hand.

### E16. Verify should-clause handling in the engine
Nat suspects the should-clause code (scoring and reporting which shoulds hit, where, and for how many points) is wrong, because earlier agents didn't follow the spec. Before building S6 on it, audit should evaluation and scoring against the JAML spec and add tests that check each should's hit location and points on known seeds.

### E17. Native Soul-order FilterDesc (run-wide legendary order)
**Template to copy:** `Motely/Filters/AnteCards/LegendaryJokerFilterDesc.cs` (`LegendaryJokerFilterDesc` / `LegendaryJokerClause`, 179 lines), `LegendarySoulEditionFilterDesc.cs` + `LegendarySoulEditionPrefilter.cs` (SIMD Negative-edition soul prefilter), `LegendarySoulMatcher.cs` (single-seed exact walk, `CountAnte`/`MatchAnte`). The pattern is: `CreateFilter` caches streams via `ctx.CacheLegendaryJokerStream`, `Filter(ref MotelyVectorSearchContext)` is a pure SIMD lane mask (comment: "Never SearchIndividualSeeds here"), and the exact pack/Soul check runs later in the should/must re-eval (`ClauseScoring.cs`, `JamlShouldScoreDesc`).

**Bug to fix first:** `LegendarySoulMatcher.CountAnte` (line ~81) and `MotelyJamlyzerFilterDesc.cs:271` call `ctx.CreateLegendaryJokerStream(ante)`, which builds a new stream for each ante (`MotelySingleSearchContext.Jokers.cs:207` passes `ante` into `CreateJokerFixedRarityStream` with `JokerSoulSource`). In the game the Soul's legendary roll (`Joker4`) is run-wide: the Nth Soul opened gives the Nth entry. `/workspace/probe/Program.cs` reads one stream (ante 1) and takes 4 draws as the global sequence. Verify this against the game source and known seeds (e.g. Nat's Perkeo x2 seeds) before relying on it. Either way, the per-ante stream is what makes "which legendary" wrong in analyze/jamlyze.

**New desc:** `LegendarySoulOrderFilterDesc` + `LegendarySoulOrderClause`.
- SIMD pass (rough cut): reuse the `LegendarySoulEditionPrefilter`/`SimdPackSupport` pack walk to keep lanes that have at least K Souls in arcana/spectral packs across the requested antes (K = number of listed legendaries). Optionally also mask on the global Joker4 sequence: lane survives only if the sequence's entries match the requested order. That is cheap because it is only one stream.
- Scoring pass (double-check): one `MotelySingleSearchContext` walk, antes 1..maxAnte sequentially (1–8 default, up to 20+ when clauses ask). Per ante, use `ClampBoosterPackSlotForAnte` (`ClauseScoring.cs:2261`) and `MotelyRunState.IsExtendedPackAnteActive` for Hieroglyph/Petroglyph (ante 1 slots 4–7 / ante-0 shop; see `SimdPackSupport.SlotReachableMask`, `ClauseScoring.cs:164`, `MotelyJamlyzerFilterDesc.cs:136-146`). For each Soul seen, in pack order (keep the stream alignment rules from `LegendarySoulMatcher`'s doc comment), draw the next entry from ONE run-wide legendary stream, and record (soulIndex, ante, packSlot, legendary).
- Matching: the user's list is an ordered list of (legendary, ante range). If the sequence is Perkeo, Yorick, Perkeo, "Perkeo ante 1, Perkeo ante 9" needs a Soul for Yorick between them (antes 1..9). The walk just requires the Nth soul's ante to fall in the listed range for listed entries, and any Soul (any ante in between) for unlisted ones.
- Duplicate rule: the game never gives a legendary you already own unless Showman is active (`MotelyRunState.ShowmanActive`/`ActivateShowman`, `MotelyRunState.cs:44,99`). Model the pool exclusion (reroll/skip to next in the sequence) when Showman is not active, and add a `showman: true` clause option. Confirm exact reroll behavior against the game's `get_current_pool` source.

**Proposed JAML:**
```yaml
must:
  - soulOrder:
      - { legendary: Perkeo, antes: [1] }
      - { legendary: Yorick }          # any ante between, order enforced
      - { legendary: Perkeo, antes: [9,10,11,12,13,14,15,16,17] }
    maxAnte: 17
    boosterPacks: [0,1,2,3,4,5]
    showman: false
should:
  - soulOrder: [{ legendary: Perkeo, antes: [1,2] }]
    score: 50
```
**Size:** the parsing/desc boilerplate is small copy-paste (~150–250 lines from LegendaryJokerFilterDesc + matcher). The real work is NOT copy-paste: switching to a run-wide soul stream (affects existing legendaryJoker/analyze results), the no-dup/Showman pool logic, and tests on known seeds (BH7I7UA1, Nat's Perkeo x2 seeds).

### E18. Motely.CLI + Motely.MCP share one tool layer
Put the tools in one library (e.g. `Motely/Tools/`), with thin CLI flags and MCP tool wrappers over it:
- `legendary_order(seeds, deck, stake, count)`: the run-wide Soul to legendary sequence plus where each Soul appears (ante/pack). This replaces `/workspace/probe`.
- `replay(filter, seeds)`: run a filter on only the given seeds (or the filter's `seeds:` block, same as CLI `--replay`).
- `should_hits(filter, seeds)`: for each seed, show the SHOULD clauses only (which hit, ante/slot, points). Leave musts out, since passing seeds always have them. Depends on E16.
- `analyze(seed, deck, stake, antes)` with antes past 8 (up to 20+), and the legendary from Nth-Soul order.

**Old Motely.MCP:** master once had it. It was deleted in `954dd48` "soften it for my boy claude" (2026-10-06, Nat), and earlier versions were deleted in `836c660` "yeet" and `34626e8` "cleanup" (MotelyTools.cs, FilterQueue.cs, DESIGN-check_shop.md, Party/PartyTools). The newest one is only on `origin/claude/quirky-bohr-egowi1`: `2776a96` "Motely.MCP: four tools and an MCP App, NativeAOT, stdio or OAuth HTTP", `bccdc9c` Aspire AppHost, `5aa90d9` removed the app UI (files: Motely.MCP/{GlobalUsings,Motely.MCP.csproj,MotelyTools,Program,Searches}.cs). It is Claude-written, so audit it before reuse. Don't merge without Nat's yes.

### H1. Hard rule for anything hosted (app, MCP, API)
Jammy Seed Finder was hit by attacks every day and lost thousands of dollars. Every hosted search endpoint needs auth, per-user rate limits, and a hard spending cap that shuts things off. No unauthenticated path may start a full search; public use is limited to instant replay or `--seeds` checks.
