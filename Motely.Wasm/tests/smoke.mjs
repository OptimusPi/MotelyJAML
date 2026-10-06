// Boots the published package under Node and drives the engine's own API through it.
// Usage: node tests/smoke.mjs   (after `dotnet publish -c Release` in Motely.Wasm)
import assert from "node:assert/strict";
import bootsharp, {
  JamlConfigLoader,
  JamlSearchBuilder,
  MotelyJamlyzer,
  MotelyDeck,
  CancellationToken,
  Errors,
} from "../bin/motely-wasm/index.mjs";

await bootsharp.boot();

const text = "name: smoke\ndeck: Red\nstake: White\nmust:\n  - voucher: Telescope\n    antes: [1]\n";

// The loader: JAML text in, the engine's JamlConfig out (a record, so a plain JS object).
const config = JamlConfigLoader.fromJaml(text);
assert.equal(config.name, "smoke");
assert.equal(config.deck, MotelyDeck.Red);
assert.equal(config.must.length, 1);

// A bad filter: fromJaml throws, and NativeAOT drops the message at the boundary, so the reason
// is on Errors.last(); check gives it without throwing.
const bad = "name: x\nmust:\n  - joker: Blueprint\n    antes: [1\n";
Errors.last();
assert.throws(() => JamlConfigLoader.fromJaml(bad));
assert.match(Errors.last() ?? "", /line \d+/i, "the thrown reason is kept on Errors.last()");
assert.match(JamlConfigLoader.check(bad), /line \d+/i);
assert.equal(JamlConfigLoader.check(text), null);

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

// A sequential slice with the Jamlyzer riding along: every find is analyzed in the same pass.
const analyzed = [];
const rider = MotelyJamlyzer.createRiderDesc(config, (r) => analyzed.push(r), 0);
const sliceFinds = [];
const slice = JamlSearchBuilder.createSettings(config)
  .withThreadCount(1)
  .withQuietMode(true)
  .withSequentialSearch()
  .withBatchCharacterCount(3)
  .withEndBatchIndex(2n)
  .withSeedMatchCallback((seed) => sliceFinds.push(seed))
  .withSeedAnalyzeProvider(rider)
  .start();
await slice.waitForCompletionAsync();
assert.ok(slice.isCompleted && slice.totalSeedsSearched > 0n);
assert.ok(sliceFinds.length > 0, "a Telescope ante-1 slice must find seeds");
assert.deepEqual(new Set(analyzed.map((r) => r.seed)), new Set(sliceFinds), "one analysis per find");

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
const unscoped = JamlConfigLoader.fromJaml("name: o\nseeds: [ALEEB]\nmust:\n  - joker: Blueprint\n");
const antesBefore = MotelyJamlyzer.analyze(unscoped)[0].antes.map((a) => a.ante).join();
JamlSearchBuilder.createSettings(unscoped);
assert.equal(MotelyJamlyzer.analyze(unscoped)[0].antes.map((a) => a.ante).join(), antesBefore);

// The Jamlyzer on the config's seeds, then a resumed window from the returned stream states.
const seeded = JamlConfigLoader.fromJaml(text + "seeds: [ALEEB]\n");
const first = MotelyJamlyzer.analyze(seeded);
assert.equal(first.length, 1);
assert.equal(first[0].seed, "ALEEB");
const next = MotelyJamlyzer.analyzeWithResumeFrom(seeded, first[0].streamStates, 20);
assert.equal(next.length, 1);
assert.ok(next[0].streamStates.rollOffset > first[0].streamStates.rollOffset, "resume advances");

console.log(
  `smoke ok: ${finds.length} list finds, ${sliceFinds.length} slice finds analyzed in-pass, ` +
    `cancelled after ${long.totalSeedsSearched} seeds`,
);
