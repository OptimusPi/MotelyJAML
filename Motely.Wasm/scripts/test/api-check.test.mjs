// node --test scripts/test/   (from Motely.Wasm/)
// Release-gate classification: which typings changes api-check lets through on a minor bump.
// Each case writes a baseline + current pair of index.g.d.mts shapes to a temp dir, runs the
// real CLI against them, and checks the exit code and the table it prints.
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { parseSurface, diffSurfaces } from "../api-surface.mjs";

const apiCheck = fileURLToPath(new URL("../api-check.mjs", import.meta.url));

const BASE = `export namespace Search {
    export function settings(jaml: string): SearchSettings;
}
export interface SearchSettings {
    readonly isCompleted: boolean;
    withDeck(deck: MotelyDeck): SearchSettings;
    createShopItemStream(ante: number, flags: MotelyShopStreamFlags, jokerFlags: MotelyJokerStreamFlags, isCached?: boolean): number;
}
export enum MotelyDeck {
    Red,
    Blue
}
export type MotelyItem = Readonly<{
    value: number;
    seal: MotelySeal;
}>;
export type Plain = {
    a: number;
};
`;

/** Run api-check with `base` published as 1.0.0 and `current` built as `version`. */
function gate(base, current, version) {
    const dir = mkdtempSync(join(tmpdir(), "api-check-test-"));
    try {
        const baseline = join(dir, "baseline.json");
        const typings = join(dir, "index.g.d.mts");
        writeFileSync(baseline, JSON.stringify({ version: "1.0.0", surface: parseSurface(base) }));
        writeFileSync(typings, current);
        const run = spawnSync(process.execPath, [apiCheck, "--baseline", baseline, "--typings", typings, "--version", version], { encoding: "utf8" });
        return { code: run.status, out: `${run.stdout}${run.stderr}` };
    } finally {
        rmSync(dir, { recursive: true, force: true });
    }
}

const row = (out, symbol) => out.split("\n").find((l) => l.includes(`| ${symbol} `));

// ── finding 3: the text around a block-form type's braces is part of its shape ──

test("parseSurface keeps a block type's wrapper", () => {
    const s = parseSurface(BASE);
    assert.equal(s.types.MotelyItem.shape, "Readonly<{…}>");
    assert.equal(s.types.Plain.shape, undefined);
});

test("mutable -> Readonly<> on a block type is a change the gate sees", () => {
    const r = gate(BASE, BASE.replace("export type Plain = {\n    a: number;\n};", "export type Plain = Readonly<{\n    a: number;\n}>;"), "1.1.0");
    assert.equal(r.code, 1, r.out);
    assert.match(row(r.out, "type Plain"), /^\| CHANGED\s+\|/);
});

test("Readonly<{…}> -> {…} is a change too", () => {
    const d = diffSurfaces(parseSurface(BASE), parseSurface(BASE.replace("export type MotelyItem = Readonly<{", "export type MotelyItem = {").replace("    seal: MotelySeal;\n}>;", "    seal: MotelySeal;\n};")));
    assert.deepEqual(d.changed.map((c) => c.symbol), ["type MotelyItem"]);
});
