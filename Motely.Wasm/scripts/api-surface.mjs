// Shared parser for Bootsharp's generated typings (bin/motely-wasm/generated/modules/index.g.d.mts).
// Turns the declaration file into a plain "surface" object that api-check.mjs diffs and
// readme-gen.mjs renders. No dependencies: the file is line-oriented and brace-balanced, which
// is all the reader below relies on.
//
//   node scripts/api-surface.mjs [--typings <path>] [--out <path>]
//
// Surface shape:
//   {
//     namespaces: { Search: { members: { scoreList: { kind: "function", sig: "(jaml: string): Promise<void>" } } } },
//     interfaces: { SearchSettings: { members: { start: "(token: CancellationToken): Promise<void>" } } },
//     enums:      { MotelyDeck: ["Red", "Blue", …] },
//     types:      { MotelyItem: { shape: "Readonly<{…}>", fields: ["value: number", …] } | { alias: "string" } },
//                 (shape is the text around the braces; absent for a bare `{ … }`)
//     classes:    { Foo: { members: {…} } },
//     functions:  { name: { kind, sig } }            // top-level export function/const/let
//   }
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

export const here = dirname(fileURLToPath(import.meta.url));
export const wasmDir = join(here, "..");
export const defaultTypings = join(wasmDir, "bin", "motely-wasm", "generated", "modules", "index.g.d.mts");

const stripSemi = (s) => s.trim().replace(/;$/, "").trim();

/** Collect the lines of a `{ … }` block starting at index i (the line holding the opening brace). */
function block(lines, i) {
    const body = [];
    let depth = 0;
    let j = i;
    for (; j < lines.length; j++) {
        const l = lines[j];
        for (const ch of l) {
            if (ch === "{") depth++;
            else if (ch === "}") depth--;
        }
        if (j > i) body.push(l);
        if (depth <= 0) break;
    }
    // last pushed line is the closing brace itself
    let close = "";
    if (body.length && /^\s*\}/.test(body[body.length - 1])) close = body.pop().trim();
    return { body, end: j, close };
}

function readMembers(body) {
    const members = {};
    for (const raw of body) {
        const s = stripSemi(raw);
        if (!s) continue;
        let m;
        if ((m = s.match(/^export function (\w+)(\(.*)$/))) members[m[1]] = { kind: "function", sig: m[2] };
        else if ((m = s.match(/^export const (\w+): (.*)$/))) members[m[1]] = { kind: "const", sig: m[2] };
        else if ((m = s.match(/^export let (\w+): (.*)$/))) members[m[1]] = { kind: "let", sig: m[2] };
        else if ((m = s.match(/^(readonly )?(\w+)(\??)(\(.*|: .*)$/)))
            members[m[2]] = { kind: m[4].startsWith("(") ? "method" : m[1] ? "readonly property" : "property", sig: `${m[3]}${m[4]}` };
    }
    return members;
}

export function parseSurface(text) {
    const lines = text.split(/\r?\n/);
    const out = { namespaces: {}, interfaces: {}, enums: {}, types: {}, classes: {}, functions: {} };
    for (let i = 0; i < lines.length; i++) {
        const l = lines[i];
        let m;
        if ((m = l.match(/^export (?:declare )?namespace (\w+) \{/))) {
            const { body, end } = block(lines, i);
            out.namespaces[m[1]] = { members: readMembers(body) };
            i = end;
        } else if ((m = l.match(/^export (?:declare )?interface (\w+)[^{]*\{/))) {
            const { body, end } = block(lines, i);
            out.interfaces[m[1]] = { members: readMembers(body) };
            i = end;
        } else if ((m = l.match(/^export (?:declare )?(?:abstract )?class (\w+)[^{]*\{/))) {
            const { body, end } = block(lines, i);
            out.classes[m[1]] = { members: readMembers(body) };
            i = end;
        } else if ((m = l.match(/^export (?:declare )?(?:const )?enum (\w+) \{/))) {
            const { body, end } = block(lines, i);
            out.enums[m[1]] = body.map((s) => stripSemi(s).replace(/,$/, "").replace(/\s*=.*$/, "")).filter(Boolean);
            i = end;
        } else if ((m = l.match(/^export (?:declare )?type (\w+)(<[^=]*>)? = (.*)$/))) {
            if (l.includes("{") && !/;\s*$/.test(l)) {
                const { body, end, close } = block(lines, i);
                // `Readonly<{ … }>` and `{ … }` hold the same fields but are not the same type:
                // keep what wraps the braces, or mutable -> readonly slips past the gate.
                const shape = `${m[3].slice(0, m[3].indexOf("{")).trim()}{…}${stripSemi(close.slice(1))}`;
                out.types[m[1]] = { ...(shape === "{…}" ? {} : { shape }), fields: body.map(stripSemi).filter(Boolean) };
                i = end;
            } else {
                out.types[m[1]] = { alias: stripSemi(m[3]) };
            }
        } else if ((m = l.match(/^export (?:declare )?function (\w+)(\(.*)$/))) {
            out.functions[m[1]] = { kind: "function", sig: stripSemi(m[2]) };
        } else if ((m = l.match(/^export (?:declare )?(const|let) (\w+): (.*)$/))) {
            out.functions[m[2]] = { kind: m[1], sig: stripSemi(m[3]) };
        }
    }
    return out;
}

/**
 * Flatten a surface into `symbol -> signature` so two surfaces diff by name.
 * Containers (namespace/interface/enum/type/class) get one entry whose signature is the sorted
 * member list, and each namespace member gets its own `Ns.member` entry so a removed function is
 * named directly rather than only as "Search changed".
 */
export function flatten(surface) {
    const flat = new Map();
    for (const [ns, def] of Object.entries(surface.namespaces)) {
        flat.set(`namespace ${ns}`, Object.keys(def.members).sort().join(", "));
        for (const [name, mem] of Object.entries(def.members)) flat.set(`${ns}.${name}`, `${mem.kind} ${mem.sig}`);
    }
    const memberSig = ([k, v]) => `${k}${v.sig}${v.kind === "readonly property" ? " [readonly]" : ""}`;
    const members = (def) => Object.entries(def.members).sort(([a], [b]) => a.localeCompare(b)).map(memberSig).join("; ");
    for (const [name, def] of Object.entries(surface.interfaces)) flat.set(`interface ${name}`, members(def));
    for (const [name, def] of Object.entries(surface.classes)) flat.set(`class ${name}`, members(def));
    for (const [name, values] of Object.entries(surface.enums)) flat.set(`enum ${name}`, values.join(", "));
    for (const [name, def] of Object.entries(surface.types))
        flat.set(`type ${name}`, def.alias ?? `${def.shape ?? "{…}"} ${[...def.fields].sort().join("; ")}`);
    for (const [name, mem] of Object.entries(surface.functions)) flat.set(name, `${mem.kind} ${mem.sig}`);
    return flat;
}

/** Name-level diff of two flattened surfaces. */
export function diffSurfaces(baseline, current) {
    const a = flatten(baseline);
    const b = flatten(current);
    const removed = [...a.keys()].filter((k) => !b.has(k)).sort();
    const added = [...b.keys()].filter((k) => !a.has(k)).sort();
    const changed = [...a.keys()]
        .filter((k) => b.has(k) && a.get(k) !== b.get(k))
        .sort()
        .map((k) => ({ symbol: k, from: a.get(k), to: b.get(k) }));
    return { removed, added, changed };
}

/** Member-level detail for a changed container, for the human table. */
export function memberDetail(from, to) {
    const split = (s) => new Set(s.split(/; |, /).filter(Boolean));
    const A = split(from);
    const B = split(to);
    const gone = [...A].filter((x) => !B.has(x));
    const fresh = [...B].filter((x) => !A.has(x));
    // A signature change shows as one member gone and one fresh with the same leading name.
    const nameOf = (s) => s.match(/^\w+/)?.[0] ?? s;
    const goneNames = new Set(gone.map(nameOf));
    const freshNames = new Set(fresh.map(nameOf));
    return {
        removed: gone.filter((x) => !freshNames.has(nameOf(x))),
        added: fresh.filter((x) => !goneNames.has(nameOf(x))),
        changed: gone.filter((x) => freshNames.has(nameOf(x))).map((x) => `${x}  →  ${fresh.find((y) => nameOf(y) === nameOf(x))}`),
    };
}

function arg(name) {
    const i = process.argv.indexOf(name);
    return i >= 0 ? process.argv[i + 1] : undefined;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
    const typings = arg("--typings") ?? defaultTypings;
    const surface = parseSurface(readFileSync(typings, "utf8"));
    const json = JSON.stringify(surface, null, 2) + "\n";
    const out = arg("--out");
    if (out) {
        writeFileSync(out, json);
        console.log(`api-surface: wrote ${out} from ${typings}`);
    } else process.stdout.write(json);
}
