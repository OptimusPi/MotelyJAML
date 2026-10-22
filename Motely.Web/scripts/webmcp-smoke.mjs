// WebMCP smoke check for the seed lab (run: node scripts/webmcp-smoke.mjs).
//
// What is REAL here: the motely-wasm engine and the registration code. The script boots the
// same engine the page loads (the npm "motely-wasm" package) and runs seed_validate /
// seed_search against it — validation, searches and summary numbers all come from the real
// WASM code. Registration goes through src/webmcp.ts's own registerSeedLabTools, the same
// function the React hook calls; only document.modelContext is a recorder mock.
//
// Known engine boundary (verified by probing): on invalid JAML the wasm-boundary throw is
// process-fatal in node, so this smoke only feeds VALID filter text to the engine. The
// invalid-INPUT path asserted here is the tool's own argument validation, which runs before
// the engine is touched. In the browser, SeedLab uses the identical try/catch +
// Errors.last() pattern, where the throw is catchable.

import assert from "node:assert/strict";

// ── Mock document.modelContext BEFORE importing the registration module ───────
const registered = [];
const controllers = [];

globalThis.document = {
  modelContext: {
    registerTool(tool) {
      registered.push(tool);
      const controller = new AbortController();
      controllers.push(controller);
      return controller;
    },
  },
};

const { seedLabTools } = await import("../src/webmcp-tools.ts");
const { registerSeedLabTools } = await import("../src/webmcp.ts");

assert.deepEqual(
  seedLabTools.map((t) => t.name),
  ["seed_validate", "seed_search"],
  "exactly two tools: seed_validate, seed_search",
);

// Drive the real registration path (same call the mount hook makes).
const registration = registerSeedLabTools(globalThis.document.modelContext);
assert.ok(registration, "registration handle returned");
assert.deepEqual(
  registered.map((t) => t.name),
  ["seed_validate", "seed_search"],
  "both tools registered via registerSeedLabTools",
);
assert.equal(registered[0], seedLabTools[0], "registered tool objects are the tool table's own");
assert.ok(seedLabTools.every((t) => t.description && t.inputSchema && typeof t.execute === "function"),
  "every tool has description, inputSchema, execute");

const byName = Object.fromEntries(seedLabTools.map((t) => [t.name, t]));
assert.ok(byName.seed_validate.inputSchema.required.includes("filter"), "validate schema requires filter");
assert.ok(byName.seed_search.inputSchema.required.includes("filter"), "search schema requires filter");
assert.deepEqual(byName.seed_search.inputSchema.properties.mode.enum,
  ["replay", "seeds", "keyword", "random", "sequential"], "search mode enum");
assert.equal(byName.seed_search.inputSchema.properties.collect.default, 100);
assert.equal(byName.seed_search.inputSchema.properties.collect.maximum, 10000);

// ── seed_validate: trivial valid filter → ok-shaped text ─────────────────────
const VALID = "name: Smoke\ndeck: Red\nstake: White\nmust:\n  - joker: Blueprint\n    antes: [1]\nshould:\n  - voucher: Telescope\n    antes: [1]\n";

const ok = await byName.seed_validate.execute({ filter: VALID });
assert.equal(ok.content[0].type, "text");
assert.match(
  ok.content[0].text,
  /^ok: Smoke \(Red White\) — 1 must, 1 should, 0 mustNot; the search plan builds\.$/,
  `validate ok text: ${ok.content[0].text}`,
);
console.log("seed_validate ok →", JSON.stringify(ok.content[0].text));

// ── seed_search: invalid combos → corrective errors, engine untouched ─────────
const badCombo = await byName.seed_search.execute({ filter: VALID, mode: "random", seeds: "AAAAAAAA", randomCount: 5 });
assert.match(badCombo.content[0].text, /^Error: pass exactly one of seeds, keyword or randomCount/,
  `bad combo teaches: ${badCombo.content[0].text}`);

const modeMismatch = await byName.seed_search.execute({ filter: VALID, mode: "replay", keyword: "COLA" });
assert.match(modeMismatch.content[0].text, /^Error: mode "replay" replays the filter's own/,
  `mode mismatch teaches: ${modeMismatch.content[0].text}`);

const badCollect = await byName.seed_search.execute({ filter: VALID, mode: "random", randomCount: 5, collect: 0 });
assert.match(badCollect.content[0].text, /^Error: collect must be an integer between 1 and 10000\./,
  `collect range teaches: ${badCollect.content[0].text}`);
console.log("invalid combo  →", JSON.stringify(badCombo.content[0].text));

// ── seed_search: mode random, small collect → matched/searched text ──────────
const random = await byName.seed_search.execute({ filter: VALID, mode: "random", randomCount: 64, collect: 10 });
assert.match(random.content[0].text, /^Smoke: \d+ matched \/ 64 searched in [\d.]+m?s \([\d,]+ seeds\/s\)\./,
  `random summary: ${random.content[0].text}`);
assert.match(random.content[0].text, /\n(Matches: [A-Z0-9, ]+|No seeds matched\.)$/, "matches line present");
console.log("seed_search rd →", JSON.stringify(random.content[0].text));

// ── seed_search: seeds mode against two known seeds ──────────────────────────
const seeds = await byName.seed_search.execute({ filter: VALID, mode: "seeds", seeds: "84111111,FFFFFFFF" });
assert.match(seeds.content[0].text, /^Smoke: \d+ matched \/ 2 searched/, `seeds mode: ${seeds.content[0].text}`);
console.log("seed_search sd →", JSON.stringify(seeds.content[0].text));

// ── registration lifecycle: abort on unmount ─────────────────────────────────
assert.equal(controllers.length, 2, "two controllers returned");
assert.ok(controllers.every((c) => !c.signal.aborted), "signals live after registration");
registration.abort();
assert.ok(controllers.every((c) => c.signal.aborted), "abort() aborts every registration signal");
assert.equal(registerSeedLabTools(undefined), null, "absent modelContext degrades to null");
assert.equal(registerSeedLabTools({}), null, "modelContext without registerTool degrades to null");

console.log("\nWEBMCP-SMOKE-OK (real motely-wasm engine + real registration; mocked: document.modelContext only)");
