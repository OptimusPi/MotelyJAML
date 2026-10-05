// Boots the built package under Node and drives the public surface once.
// Usage: node tests/smoke.mjs   (after `dotnet publish -c Release` in Motely.Wasm)
import assert from "node:assert/strict";
import { fileURLToPath, pathToFileURL } from "node:url";
import { join, dirname } from "node:path";

// The package embeds its assemblies as base64 and decodes them with Uint8Array.fromBase64,
// which Node ships from 24 (Chrome 140, Safari 18.2, Firefox 133). Older hosts bring a shim
// of their own; this is the test host's.
if (typeof Uint8Array.fromBase64 !== "function")
  Uint8Array.fromBase64 = (s) => new Uint8Array(Buffer.from(s, "base64"));

const dist = join(dirname(fileURLToPath(import.meta.url)), "..", "bin", "motely-wasm");
const { default: bootsharp, Search, Analyze, Jaml, JamlFiles } = await import(pathToFileURL(join(dist, "index.mjs")).href);

await bootsharp.boot();

const jaml = "name: smoke\ndeck: Red\nstake: White\nmust:\n  - voucher: Telescope\n    antes: [1]\n";

// Jaml.check: null when it loads, a line-numbered message when it does not.
assert.equal(Jaml.check(jaml), null);
assert.match(Jaml.check("name: x\nmust:\n  - joker: Blueprint\n    antes: [1\n"), /line \d+/i);

// Search: one find, one onScored.
const finds = [];
const onScored = (s) => finds.push(s.seed);
Search.onScored.subscribe(onScored);
const list = Search.settings(jaml).withSeedList(["ALEEB", "AAAAAAAA", "PIROCKS"]);
await list.start();
Search.onScored.unsubscribe(onScored);
assert.equal(list.totalSeedsSearched, 3n);
assert.equal(finds.length, Number(list.matchingSeeds));
assert.equal(new Set(finds).size, finds.length, "a find must arrive once");

// Sequential slice, then cancellation resolves the promise.
const slice = Search.settings(jaml).withSequentialSearch().withBatchCharacterCount(3).withEndBatchIndex(2n);
const sliceFinds = [];
const onSlice = (s) => sliceFinds.push(s.seed);
Search.onScored.subscribe(onSlice);
await slice.start();
Search.onScored.unsubscribe(onSlice);
assert.ok(slice.isCompleted && slice.totalSeedsSearched > 0n);
assert.ok(sliceFinds.length > 0, "a Telescope ante-1 slice must find seeds");
assert.equal(BigInt(sliceFinds.length), slice.matchingSeeds, "one onScored per find");

const long = Search.settings(jaml).withSequentialSearch().withBatchCharacterCount(2);
const running = long.start();
long.cancel();
await running;
assert.ok(!long.stoppedOnMatchLimit);
assert.ok(long.totalSeedsSearched < 35n ** 8n, "cancel stops the sweep");

// NativeAOT drops exception messages at the boundary, so the settings keep the reason on .error:
// for a value rejected on the spot and for one rejected when the run starts.
const badCount = Search.settings(jaml).withSequentialSearch();
assert.throws(() => badCount.withBatchCharacterCount(9));
assert.match(badCount.error ?? "", /\S/);
const badRange = Search.settings(jaml).withSequentialSearch().withBatchCharacterCount(3)
  .withStartBatchIndex(5n).withEndBatchIndex(2n);
await assert.rejects(badRange.start());
assert.match(badRange.error ?? "", /\S/);
console.log("errors:", JSON.stringify(badCount.error), "|", JSON.stringify(badRange.error));

// Analyze: one seed in, one result out.
const seeds = Analyze.seeds("name: a\ndeck: Red\nstake: White\nseeds: [ALEEB]\nmust:\n  - voucher: Telescope\n    antes: [1]\n");
assert.equal(seeds.length, 1);
assert.equal(seeds[0].seed, "ALEEB");

// A bad filter throws instead of searching.
assert.throws(() => Search.settings("name: x\nmust:\n  - joker: NotAJoker\n"));

// JamlFiles exists in both builds; without Bootsharp.FileSystem nothing can mount.
assert.equal(typeof JamlFiles.isSupported(), "boolean");
assert.deepEqual(JamlFiles.list(), []);
if (!JamlFiles.isSupported()) await assert.rejects(JamlFiles.pickFolder());

console.log(`smoke ok: ${sliceFinds.length} finds, ${slice.totalSeedsSearched} seeds in the slice`);
