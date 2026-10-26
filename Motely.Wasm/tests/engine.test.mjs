// The engine's own API through the published package, under Node.
// Usage: npm test   (after `dotnet publish -c Release` in Motely.Wasm)
import test from "node:test";
import assert from "node:assert/strict";
import bootsharp, {
  JamlConfigLoader,
  JamlSearchBuilder,
  MotelyJamlyzer,
  MotelyDeck,
  MotelyItemType,
  MotelyJoker,
  MotelyJokerRarity,
  MotelyJokers,
  CancellationToken,
  Errors,
} from "../bin/motely-wasm/index.mjs";

await bootsharp.boot();

const telescope = "name: smoke\ndeck: Red\nstake: White\nmust:\n  - voucher: Telescope\n    antes: [1]\n";

test("a filter loads into the engine's config", () => {
  const config = JamlConfigLoader.fromJaml(telescope);
  assert.equal(config.name, "smoke");
  assert.equal(config.deck, MotelyDeck.Red);
  assert.equal(config.must.length, 1);
});

test("a filter that does not load says why, with its line", () => {
  const bad = "name: x\nmust:\n  - joker: Blueprint\n    antes: [1\n";
  Errors.last();
  assert.throws(() => JamlConfigLoader.fromJaml(bad));
  assert.match(Errors.last() ?? "", /line \d+/i, "the thrown reason is kept on Errors.last()");
  assert.match(JamlConfigLoader.check(bad), /line \d+/i);
  assert.equal(JamlConfigLoader.check(telescope), null);
});

test("a value the engine cannot run is refused at its line", () => {
  // Was `min: 0`, refused by JamlValidator until 3227635 removed it; the engine runs `min: 0` as
  // the "at least zero" it says (every seed matches). A number where a name goes is refused.
  assert.match(JamlConfigLoader.check("must:\n  - joker: 5\n"), /JAML line 2: '5' is not a name/);
});

test("a seed list is searched once each, with one callback per find", async () => {
  const finds = [];
  const search = JamlSearchBuilder.createSettings(JamlConfigLoader.fromJaml(telescope))
    .withThreadCount(1)
    .withQuietMode(true)
    .withSeedList(["ALEEB", "AAAAAAAA", "PIROCKS"])
    .withSeedMatchCallback((seed) => finds.push(seed))
    .start();
  await search.waitForCompletionAsync();
  assert.equal(search.totalSeedsSearched, 3n);
  assert.equal(BigInt(finds.length), search.matchingSeeds);
  assert.equal(new Set(finds).size, finds.length, "a find arrives once");
});

test("a sequential slice sweeps its batches and finds seeds", async () => {
  const finds = [];
  const search = JamlSearchBuilder.createSettings(JamlConfigLoader.fromJaml(telescope))
    .withThreadCount(1)
    .withQuietMode(true)
    .withSequentialSearch()
    .withBatchCharacterCount(3)
    .withEndBatchIndex(2n)
    .withSeedMatchCallback((seed) => finds.push(seed))
    .start();
  await search.waitForCompletionAsync();
  assert.ok(search.isCompleted && search.totalSeedsSearched > 0n);
  assert.ok(finds.length > 0, "a Telescope ante-1 slice must find seeds");
});

test("cancelling stops an open-ended sweep", async () => {
  const token = new CancellationToken();
  const search = JamlSearchBuilder.createSettings(JamlConfigLoader.fromJaml(telescope))
    .withThreadCount(1)
    .withQuietMode(true)
    .withSequentialSearch()
    .withBatchCharacterCount(2)
    .start(token);
  const done = search.waitForCompletionAsync();
  token.cancel();
  await done;
  assert.ok(search.totalSeedsSearched < 35n ** 8n, "cancel stops the sweep");
});

test("a search can be disposed, and has no blocking wait", async () => {
  const progress = [];
  const search = JamlSearchBuilder.createSettings(JamlConfigLoader.fromJaml(telescope))
    .withThreadCount(1)
    .withQuietMode(true)
    .withSequentialSearch()
    .withBatchCharacterCount(2)
    .withProgressReportIntervalMs(0n)
    .withProgressCallback((p) => progress.push(p.percentComplete))
    .start();
  assert.equal(typeof search.dispose, "function");
  assert.equal(search.awaitCompletion, undefined, "a blocking wait would hang the page");
  const done = search.waitForCompletionAsync();
  await new Promise((resolve) => setTimeout(resolve, 20));
  search.dispose();
  await done;
  assert.ok(search.totalSeedsSearched < 35n ** 8n, "dispose stops the sweep");
  for (const pct of progress) assert.ok(pct >= 0 && pct <= 100, `percentComplete is 0-100, got ${pct}`);
});

test("the filter's own cutoff is the floor unless the host gives one", async () => {
  const scored = "name: s\ncutoff: 1000\nmust:\n  - voucher: Telescope\n    antes: [1]\nshould:\n  - voucher: Telescope\n    antes: [1]\n";
  const config = JamlConfigLoader.fromJaml(scored);

  const withOwn = [];
  const own = JamlSearchBuilder.createSettings(config)
    .withThreadCount(1)
    .withQuietMode(true)
    .withSeedList(["P1111111"])
    .withScoredResultCallback((r) => withOwn.push(r.seed))
    .start();
  await own.waitForCompletionAsync();
  assert.deepEqual(withOwn, [], "a floor of 1000 drops a seed scoring 1");

  const withHost = [];
  const host = JamlSearchBuilder.createSettings(config, -2147483648)
    .withThreadCount(1)
    .withQuietMode(true)
    .withSeedList(["P1111111"])
    .withScoredResultCallback((r) => withHost.push(r.seed))
    .start();
  await host.waitForCompletionAsync();
  assert.deepEqual(withHost, ["P1111111"], "no floor reports the seed");
});

test("a rejected setting and a rejected range say why", () => {
  const sequential = JamlSearchBuilder.createSettings(JamlConfigLoader.fromJaml(telescope)).withThreadCount(1).withSequentialSearch();
  Errors.last();
  assert.throws(() => sequential.withBatchCharacterCount(99));
  assert.match(Errors.last() ?? "", /Batch character count must be 1-/);

  const backwards = JamlSearchBuilder.createSettings(JamlConfigLoader.fromJaml(telescope))
    .withThreadCount(1)
    .withSequentialSearch()
    .withBatchCharacterCount(3)
    .withStartBatchIndex(5n)
    .withEndBatchIndex(2n);
  Errors.last();
  assert.throws(() => backwards.start());
  assert.match(Errors.last() ?? "", /End batch \(exclusive\) is before start batch/);
});

test("createSettings leaves the config alone", () => {
  const config = JamlConfigLoader.fromJaml("name: o\nseeds: [ALEEB]\nmust:\n  - joker: Blueprint\n");
  const before = MotelyJamlyzer.analyze(config)[0].antes.map((a) => a.ante).join();
  JamlSearchBuilder.createSettings(config);
  assert.equal(MotelyJamlyzer.analyze(config)[0].antes.map((a) => a.ante).join(), before);
});

test("the Jamlyzer walks a seed, and resumes from where it stopped", () => {
  const config = JamlConfigLoader.fromJaml(telescope + "seeds: [ALEEB]\n");
  const first = MotelyJamlyzer.analyze(config);
  assert.equal(first.length, 1);
  assert.equal(first[0].seed, "ALEEB");
  const next = MotelyJamlyzer.resume(config, first[0].streamStates, 20);
  assert.equal(next.length, 1);
  assert.ok(next[0].streamStates.rollOffset > first[0].streamStates.rollOffset, "resume advances");
});

test("the Jamlyzer rides a search: each find arrives with its antes", async () => {
  const analyzed = [];
  const config = JamlConfigLoader.fromJaml(telescope);
  const search = JamlSearchBuilder.createSettings(config)
    .withThreadCount(1)
    .withQuietMode(true)
    .withSeedList(["ALEEB", "PIROCKS"])
    .withSeedAnalyzeProvider(MotelyJamlyzer.createRiderDesc(config, (r) => analyzed.push(r), 0))
    .start();
  await search.waitForCompletionAsync();
  assert.equal(BigInt(analyzed.length), search.matchingSeeds);
  for (const result of analyzed) assert.ok(result.antes.length > 0);
});

test("a filter goes YAML → JSON → YAML through the engine, and the JSON loads", () => {
  const json = JamlConfigLoader.toJson(telescope);
  assert.deepEqual(JSON.parse(json), {
    name: "smoke",
    deck: "Red",
    stake: "White",
    must: [{ voucher: "Telescope", antes: [1] }],
  });
  assert.equal(JamlConfigLoader.check(json), null);
  assert.equal(JamlConfigLoader.toJson(JamlConfigLoader.toYaml(json)), json);
});

test("deep nesting is refused at its line, and the module lives on", () => {
  // 5,000 levels overflowed the stack in YamlDotNet's node building and killed the module.
  const deep = "name: d\nmust:\n  - joker: Joker\n    antes: " + "[".repeat(100_000) + "]".repeat(100_000) + "\n";
  Errors.last();
  assert.throws(() => JamlConfigLoader.toJson(deep));
  assert.match(Errors.last() ?? "", /^JAML line 4: the document nests deeper than 64 levels/);
  assert.match(JamlConfigLoader.check(deep) ?? "", /^JAML line 4: the document nests deeper than 64 levels/);
  assert.match(JamlConfigLoader.check(JamlConfigLoader.toJson("x: " + "[".repeat(63) + "]".repeat(63) + "\n")) ?? "", /'x' is not a key of a filter/);
  assert.equal(JamlConfigLoader.check(telescope), null);
});

test("toJson keeps a seed that looks like a number as written", () => {
  const json = JamlConfigLoader.toJson("name: s\nseeds: [99E99, 11E11111, 010]\nmust:\n  - joker: Joker\n");
  assert.match(json, /"seeds": \[\n    99E99,\n    11E11111,\n    "010"\n  \]/);
  assert.deepEqual(JamlConfigLoader.fromJaml(json).seeds, ["99E99", "11E11111", "010"]);
});

test("a legendary from the Jamlyzer is an item like every other card", () => {
  // C3DW94B8 (Red/White) holds one ante-1 Soul (Motely.Tests JAMLyzerUnitTests pins its slot).
  const result = MotelyJamlyzer.analyze(JamlConfigLoader.fromJaml("name: soul\nseeds: [C3DW94B8]\n"), 1)[0];
  const [legendary] = result.legendaryOrder;
  assert.equal(legendary.ante, 1);
  assert.equal(MotelyItemType[legendary.item.type], MotelyJoker[legendary.joker]);
  assert.equal(legendary.item.edition, legendary.edition);
  assert.equal(MotelyJokers.rarity(legendary.item.type), MotelyJokerRarity.Legendary);
});

test("an ante's shop depth and pack count are published before any result", () => {
  const result = MotelyJamlyzer.analyze(JamlConfigLoader.fromJaml("name: w\nseeds: [ALEEB]\n"), 1)[0];
  for (const ante of result.antes) {
    assert.equal(ante.shopItems.length, MotelyJamlyzer.defaultShopSlots(ante.ante));
    assert.equal(ante.packs.length, MotelyJamlyzer.packCount(ante.ante));
  }
});

test("joker rarity, as an export and as the same test with no call", () => {
  assert.equal(MotelyJokers.rarity(MotelyItemType.Joker), MotelyJokerRarity.Common);
  assert.equal(MotelyJokers.rarity(MotelyItemType.Blueprint), MotelyJokerRarity.Rare);
  assert.equal(MotelyJokers.rarity(MotelyItemType.Perkeo), MotelyJokerRarity.Legendary);
  assert.equal(MotelyItemType.Perkeo & MotelyJokerRarity.Legendary, MotelyJokerRarity.Legendary);
  assert.equal(MotelyItemType.Blueprint & MotelyJokerRarity.Legendary, MotelyJokerRarity.Rare);
});
