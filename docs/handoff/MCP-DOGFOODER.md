# MCP Dogfooder log

Running notes on where the seedfinder MCP falls short during real seed work. Newest at the bottom.

## 2026-10-11: Lucky Cat "before round 1"
- **Asked:** a Lucky Cat you can actually get before round 1.
- **What the MCP gave:** `learn_jaml` (query "lucky cat judgement") returned a Lucky Cat filter that searches ante 1 shop slots 0-3. Its own note admits the real game won't show Lucky Cat in a shop until a Lucky card is in the deck (the enhancement gate), and it searches the raw slot anyway. The seeds it finds aren't buyable.
- **Missing:** no guidance on getting Lucky Cat through Judgement (tarot creates a random joker), whether Judgement gets past the enhancement gate, or how to filter on a Judgement pull. Nat's trick: use Judgement as the source. To be documented once Nat shows it on Blueprint.

## 2026-10-11: Legendary order
- **Asked:** seeds whose legendary order goes Perkeo, Perkeo (or Perkeo, X, Perkeo).
- **Gap:** legendary order is the sequence the player sees as they open Souls, not a fixed legendary per ante. JAML `legendaryJoker` clauses only check per ante, so no clause can say "Soul #1 and #2 are both Perkeo." `jamlyze_seeds` with `include: ["pulls"]` shows the soul stream, but there's no searchable clause for it. Needs a new engine clause in MotelyJAML.

## 2026-10-11: Negative Tag + Oops placement
- **Gap:** Cola Oops Lite checks the Negative Tag and Oops in shop slots 2-8 as separate clauses. Nothing ties the Oops to being the joker the tag actually lands on (first joker in line, after rerolls or a double skip). The filter can't express the real trick.

## 2026-10-11: CLI keyword-less search
- **Observed:** `Motely.CLI --collect 1` with no keyword runs `sequential` from the first seed. It doesn't cycle through the aesthetic sets.

## 2026-10-11: No backward planning
- **Gap:** the MCP can't work backward from a goal ("Lucky Cat before round 1") to the steps that get there (tag, pack, tarot, joker pull). Nat had to lead the reasoning by hand. It should take a target item plus a timing and return the chain of sources that can produce it, then filter on that chain.

## Handoff note for a coder-planner (2026-10-11)
Wire the public seedfinder.app MCP (repo `OptimusPi/seedfinder.app`) so its searches run on Nat's Ryzen box instead of a server that sleeps, with Motely v29 as the engine. Searches should be queued jobs that survive disconnects and stream results back, so a long search like Cola Oops can run for hours. Before publishing, audit the Claude-written parts of seedfinder.app and the unmerged `claude/quirky-bohr-egowi1` branch (new Motely.MCP server, commit says "untested") against the engine's actual behavior.

## PROOF: Nat's Lucky Cat before round 1 trick (2026-10-11)
Nat designed this. The MCP's own Lucky Cat example was wrong: it searched a shop slot the game won't let you buy from.
- **Mechanic (checked in Balatro's Lua, `card.lua` + `functions/common_events.lua`):** Judgement makes its joker through `create_card` → `get_current_pool`. Lucky Cat has `enhancement_gate = 'm_lucky'`, so it can't spawn until a Lucky card is in the deck.
- **Route:** skip the ante 1 small blind for a Charm Tag, which gives a Mega Arcana (pick 2). Pick The Magician first (2 Lucky cards), then Judgement, which rolls Lucky Cat. Perkeo comes from The Soul in the ante 1 packs.
- **Filters:** `/workspace/whimsy/LuckyCatBeforeR1.yaml`, and `/workspace/whimsy/LuckyCatPerkeoR1.yaml` (adds Perkeo).
- **Results:**
  - LuckyCatBeforeR1, sequential: 15 matches in about 1.1M seeds. Best: 5MHF1111 (Oops by ante 3); from `--startSeed PEEPBOOB`, 5NRTBOOB (Oops by ante 3).
  - LuckyCatPerkeoR1, `--aesthetic nsfw --cutoff auto`: 11 of 302,944,676 seeds matched. QSEXYM36, KSUCKHKT, 8WANKGMP, ML5WANK4, Z5MOWANK, 32YORGYN, NUDEM1LM, 1BDMSUN1, DONGCZB1, 7MOANEAW, XF2ARSEI. None has Oops by ante 3.
- **Missing from the MCP:** it can't work backward from a goal to a route, and it doesn't know about enhancement gates on Judgement or Soul spawns.

## 2026-10-11 (later): correction to the Lucky Cat + Perkeo results
Checked the 11 LuckyCatPerkeoR1 seeds against the real Charm contents (the Charm Mega is opened first and takes the ante's first 5 Arcana draws; `tag.lua:209-216`). Only **1BDMSUN1** gets Lucky Cat + Perkeo before round 1 (skip both blinds, since both ante-1 tags are Charm). QSEXYM36 and DONGCZB1 have all three cards in one pick-2 Charm. KSUCKHKT gets Lucky Cat but no reachable Soul. The other 7 can't do Lucky Cat before round 1. Also **5NRTBOOB** (Charm has no Judgement) and **KVK31111** (no Magician) are not real before-round-1 Lucky Cat seeds; **5MHF1111** is. Root causes and fixes: `MCP-FIX-HANDOFF.md` E1/E2/E6.

## Feedback (Nat, 2026-10-12): cutoff auto drops passing seeds
Cutoff auto keeps raising the minimum score, so most seeds that pass get thrown away. NSFW saved only 13 of 62 passing seeds, and psychosis saved only 9 of 203. Nat's idea is to send a second scoring pass that collects and holds every face (joker) seen during normal shopping for each seed. That way the passing seeds and their full shop contents are kept and can be ranked later, instead of being lost to the cutoff.

## Feedback (Nat, 2026-10-12): mismatch tags
When the engine's results may differ from what the real game does, the output should tag each seed with the reason. Examples: the legendary order restarts every ante (Perkeo showing up in two antes), Charm pack contents and card order aren't modelled exactly, and the Lucky-card requirement for Lucky Cat isn't checked. Players could then see which hits to trust without having to re-analyze every seed.

## 2026-10-12: Perkeo x2 check from legendary rolls (probe, run-wide Joker4 order)
- EQ5HCML1: Perkeo > Perkeo > Chicot > Yorick. Souls in A1 Jumbo Spectral + A2 Jumbo Arcana, so 2 Perkeos. TOP PICK.
- RR4HIBP1: Perkeo > Perkeo > Yorick > Canio. Only 1 natural Soul in A1-8 (A1 Jumbo Arcana) but both A1 tags are Charm, so a Charm Mega could give Soul #2.
- LAUVJ7ON: Perkeo > Canio > Chicot > Perkeo. Only 1 Soul in A1-8, so 1 Perkeo; Negative Tags in A5 and A8, Diet Cola in A5/A6.
- Caveat: the game won't give a legendary you already hold unless you have Showman, so Soul #2 = Perkeo needs Showman or an empty slot for it.
- jamlyze_seeds (MCP) shows "TheSoul" in packs but never which legendary. Same gap as --analyze (E10).
