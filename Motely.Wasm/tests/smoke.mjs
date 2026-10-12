// Boots the published package under Node and drives the engine's own API through it.
// Usage: node tests/smoke.mjs   (after `dotnet publish -c Release` in Motely.Wasm)
import assert from "node:assert/strict";
import bootsharp, {
  YamlConfigLoader,
  JamlSearchBuilder,
  MotelyJamlyzer,
  MotelyDeck,
  MotelyItemType,
  MotelyJokerRarity,
  MotelyJokers,
  JamlFiles,
  CancellationToken,
  Errors,
} from "../bin/motely-wasm/index.mjs";

await bootsharp.boot();

const text = "name: smoke\ndeck: Red\nstake: White\nmust:\n  - voucher: Telescope\n    antes: [1]\n";

// The loader: JAML text in, the engine's JamlConfig out (a record, so a plain JS object).
const config = YamlConfigLoader.fromYaml(text);
assert.equal(config.name, "smoke");
assert.equal(config.deck, MotelyDeck.Red);
assert.equal(config.must.length, 1);

// A bad filter: fromYaml throws, and NativeAOT drops the message at the boundary, so the reason
// is on Errors.last(); check gives it without throwing.
const bad = "name: x\nmust:\n  - joker: Blueprint\n    antes: [1\n";
Errors.last();
assert.throws(() => YamlConfigLoader.fromYaml(bad));
assert.match(Errors.last() ?? "", /line \d+/i, "the thrown reason is kept on Errors.last()");
assert.match(YamlConfigLoader.check(bad), /line \d+/i);
assert.equal(YamlConfigLoader.check(text), null);

// The engine's search settings, configured with its own fluent API. A seed list is bounded.
const finds = [];
const list = JamlSearchBuilder.createSettings(config)
  .withThreadCount(1)
  .withQuietMode(true)
  .withSeedList(["ALEEB", "AAAAAAAA", "PIROCKS"])
  .withSeedMatchCallback((seed) => finds.push(seed))
  .start();
await list.waitForCompletionAsync();
assert.equal(list.totalSeedsSearched, 3n);
assert.equal(BigInt(finds.length), list.matchingSeeds, "one callback per find");
assert.equal(new Set(finds).size, finds.length, "a find must arrive once");

// A sequential slice: a bounded sweep over the first batches.
const sliceFinds = [];
const slice = JamlSearchBuilder.createSettings(config)
  .withThreadCount(1)
  .withQuietMode(true)
  .withSequentialSearch()
  .withBatchCharacterCount(3)
  .withEndBatchIndex(2n)
  .withSeedMatchCallback((seed) => sliceFinds.push(seed))
  .start();
await slice.waitForCompletionAsync();
assert.ok(slice.isCompleted && slice.totalSeedsSearched > 0n);
assert.ok(sliceFinds.length > 0, "a Telescope ante-1 slice must find seeds");

// Cancellation through Bootsharp's CancellationToken: the open-ended sweep stops and resolves.
const token = new CancellationToken();
const long = JamlSearchBuilder.createSettings(config)
  .withThreadCount(1)
  .withQuietMode(true)
  .withSequentialSearch()
  .withBatchCharacterCount(2)
  .start(token);
const done = long.waitForCompletionAsync();
token.cancel();
await done;
assert.ok(long.totalSeedsSearched < 35n ** 8n, "cancel stops the sweep");

// A rejected setting and a rejected range: the reason is on Errors.last().
const sequential = JamlSearchBuilder.createSettings(config).withThreadCount(1).withSequentialSearch();
Errors.last();
assert.throws(() => sequential.withBatchCharacterCount(99));
assert.match(Errors.last() ?? "", /Batch character count must be 1-/);
const backwards = JamlSearchBuilder.createSettings(config).withThreadCount(1).withSequentialSearch()
  .withBatchCharacterCount(3).withStartBatchIndex(5n).withEndBatchIndex(2n);
Errors.last();
assert.throws(() => backwards.start());
assert.match(Errors.last() ?? "", /End batch \(exclusive\) is before start batch/);

// createSettings leaves the config alone: the Jamlyzer reads the same antes before and after.
const unscoped = YamlConfigLoader.fromYaml("name: o\nseeds: [ALEEB]\nmust:\n  - joker: Blueprint\n");
const antesBefore = MotelyJamlyzer.analyze(unscoped)[0].antes.map((a) => a.ante).join();
JamlSearchBuilder.createSettings(unscoped);
assert.equal(MotelyJamlyzer.analyze(unscoped)[0].antes.map((a) => a.ante).join(), antesBefore);

// The Jamlyzer on the config's seeds, then a resumed window from the returned stream states.
const seeded = YamlConfigLoader.fromYaml(text + "seeds: [ALEEB]\n");
const first = MotelyJamlyzer.analyze(seeded);
assert.equal(first.length, 1);
assert.equal(first[0].seed, "ALEEB");
const next = MotelyJamlyzer.analyzeWithResumeFrom(seeded, first[0].streamStates, 20);
assert.equal(next.length, 1);
assert.ok(next[0].streamStates.rollOffset > first[0].streamStates.rollOffset, "resume advances");

// Joker rarity: the export, and the same test as a mask with no call.
assert.equal(MotelyJokers.rarity(MotelyItemType.Joker), MotelyJokerRarity.Common);
assert.equal(MotelyJokers.rarity(MotelyItemType.Blueprint), MotelyJokerRarity.Rare);
assert.equal(MotelyJokers.rarity(MotelyItemType.Perkeo), MotelyJokerRarity.Legendary);
assert.equal(MotelyItemType.Perkeo & MotelyJokerRarity.Legendary, MotelyJokerRarity.Legendary);
assert.equal(MotelyItemType.Blueprint & MotelyJokerRarity.Legendary, MotelyJokerRarity.Rare);

// JamlFiles: nothing mounted under Node in either build; without Bootsharp.FileSystem the file
// calls reject and say why on Errors.last().
assert.equal(JamlFiles.isMounted(), false);
assert.deepEqual([...JamlFiles.list()], []);
if (!JamlFiles.isSupported()) {
  Errors.last();
  await assert.rejects(JamlFiles.pickFolder());
  assert.match(Errors.last() ?? "", /no folder access/);
}

console.log(
  `smoke ok: ${finds.length} list finds, ${sliceFinds.length} slice finds, ` +
    `cancelled after ${long.totalSeedsSearched} seeds`,
);
