// Release gate: diff the exported API of the freshly built typings against the last PUBLISHED
// motely-wasm and refuse a publish that removes or reshapes a name without a major bump.
//
//   node scripts/api-check.mjs                       baseline = npm latest (npm pack, cached in .api-baseline/)
//   node scripts/api-check.mjs --offline             baseline = the checked-in api-baseline.json
//   node scripts/api-check.mjs --baseline 25.0.3     baseline = that published version (npm pack)
//   node scripts/api-check.mjs --baseline x.tgz|x.json|<dir>   baseline from a tarball, surface JSON, or unpacked package
//   node scripts/api-check.mjs --typings <index.g.d.mts>       current typings (default: bin/motely-wasm/generated/modules/index.g.d.mts)
//   node scripts/api-check.mjs --version 26.1.0      current version (default: <MotelyVersion> in Directory.Build.props)
//   node scripts/api-check.mjs --write-baseline      refresh api-baseline.json from the baseline (run after a publish)
//
// Rules, compared to the baseline's version:
//   removed or signature-changed name while MAJOR did not bump  -> exit 1, every symbol named
//   added name, or a container that only gained members         -> added; warning under a PATCH bump
//     (new enum member at the end, new optional field on a type, new interface/class member,
//      new namespace function; a new REQUIRED type field or a mid-list enum member is a change)
//   current version not above the baseline                      -> exit 1
//
// Runs after `dotnet publish`: bin/ is gitignored, so the typings only exist post-build.
import { readFileSync, writeFileSync, existsSync, mkdirSync, readdirSync, statSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { join, relative, resolve, basename } from "node:path";
import { gunzipSync } from "node:zlib";
import { parseSurface, diffSurfaces, memberDetailText, defaultTypings, wasmDir } from "./api-surface.mjs";
import { readMotelyVersion } from "./version-sync.mjs";

const repoRoot = join(wasmDir, "..");
const cacheDir = join(wasmDir, ".api-baseline");
const committedBaseline = join(wasmDir, "api-baseline.json");
const TYPINGS_SUFFIX = "generated/modules/index.g.d.mts";

function arg(name) {
    const i = process.argv.indexOf(name);
    return i >= 0 ? process.argv[i + 1] : undefined;
}
const has = (name) => process.argv.includes(name);

// ── semver (major.minor.patch, optional -pre / +build) ──
export function parseSemver(v) {
    const m = String(v).trim().match(/^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$/);
    if (!m) throw new Error(`not a semver version: ${v}`);
    return { major: +m[1], minor: +m[2], patch: +m[3], pre: m[4] ?? null, raw: v };
}
export function compareSemver(a, b) {
    for (const k of ["major", "minor", "patch"]) if (a[k] !== b[k]) return a[k] < b[k] ? -1 : 1;
    if (a.pre === b.pre) return 0;
    if (a.pre === null) return 1; // release > prerelease
    if (b.pre === null) return -1;
    return a.pre < b.pre ? -1 : 1;
}
export function bumpKind(from, to) {
    if (to.major !== from.major) return "major";
    if (to.minor !== from.minor) return "minor";
    if (to.patch !== from.patch) return "patch";
    return "none";
}

// ── tarball reading (ustar, zero dependencies; npm pack output is plain gzip'd tar) ──
function readTar(buffer) {
    const files = new Map();
    let off = 0;
    let longName = null;
    while (off + 512 <= buffer.length) {
        const header = buffer.subarray(off, off + 512);
        if (header.every((b) => b === 0)) break;
        const str = (s, l) => header.subarray(s, s + l).toString("utf8").replace(/\0.*$/s, "");
        let name = str(0, 100);
        const size = parseInt(str(124, 12).trim() || "0", 8);
        const type = String.fromCharCode(header[156]);
        const prefix = str(345, 155);
        if (prefix) name = `${prefix}/${name}`;
        const data = buffer.subarray(off + 512, off + 512 + size);
        off += 512 + Math.ceil(size / 512) * 512;
        if (type === "L") { longName = data.toString("utf8").replace(/\0+$/, ""); continue; }
        if (longName) { name = longName; longName = null; }
        if (type === "0" || type === "\0" || type === "") files.set(name, data);
    }
    return files;
}

function surfaceFromTarball(tgzPath) {
    const files = readTar(gunzipSync(readFileSync(tgzPath)));
    const typings = [...files.keys()].find((k) => k.endsWith(TYPINGS_SUFFIX));
    if (!typings) throw new Error(`${basename(tgzPath)} has no ${TYPINGS_SUFFIX}`);
    const pkg = JSON.parse(files.get("package/package.json").toString("utf8"));
    return { version: pkg.version, source: `${basename(tgzPath)}:${typings}`, surface: parseSurface(files.get(typings).toString("utf8")) };
}

function findTypingsInDir(dir) {
    const stack = [dir];
    while (stack.length) {
        const d = stack.pop();
        for (const name of readdirSync(d)) {
            const p = join(d, name);
            if (statSync(p).isDirectory()) { if (name !== "node_modules") stack.push(p); }
            else if (p.replaceAll("\\", "/").endsWith(TYPINGS_SUFFIX)) return p;
        }
    }
    throw new Error(`no ${TYPINGS_SUFFIX} under ${dir}`);
}

function surfaceFromDir(dir) {
    const typings = findTypingsInDir(dir);
    const pkgPath = join(dir, "package.json");
    const version = existsSync(pkgPath) ? JSON.parse(readFileSync(pkgPath, "utf8")).version : arg("--baseline-version");
    if (!version) throw new Error(`${dir} has no package.json; pass --baseline-version`);
    return { version, source: relative(process.cwd(), typings) || typings, surface: parseSurface(readFileSync(typings, "utf8")) };
}

function npm(args) {
    return execFileSync("npm", args, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"], shell: process.platform === "win32" }).trim();
}

function surfaceFromNpm(version) {
    mkdirSync(cacheDir, { recursive: true });
    if (!version) version = npm(["view", "motely-wasm", "dist-tags.latest"]);
    const tgz = join(cacheDir, `motely-wasm-${version}.tgz`);
    if (!existsSync(tgz)) {
        console.log(`api-check: fetching motely-wasm@${version} (npm pack) into ${relative(repoRoot, cacheDir)}/`);
        npm(["pack", `motely-wasm@${version}`, "--pack-destination", cacheDir, "--silent"]);
    } else console.log(`api-check: using cached ${relative(repoRoot, tgz)}`);
    return surfaceFromTarball(tgz);
}

function loadBaseline() {
    const spec = arg("--baseline");
    if (has("--offline") || spec === committedBaseline) {
        if (!existsSync(committedBaseline)) throw new Error(`--offline needs ${committedBaseline}`);
        const json = JSON.parse(readFileSync(committedBaseline, "utf8"));
        return { version: json.version, source: relative(repoRoot, committedBaseline), surface: json.surface };
    }
    if (spec && existsSync(spec)) {
        if (statSync(spec).isDirectory()) return surfaceFromDir(spec);
        if (spec.endsWith(".json")) {
            const json = JSON.parse(readFileSync(spec, "utf8"));
            return { version: json.version, source: spec, surface: json.surface };
        }
        if (spec.endsWith(".tgz")) return surfaceFromTarball(spec);
        throw new Error(`don't know how to read baseline ${spec}`);
    }
    if (spec && /[\\/]|\.(tgz|json)$/.test(spec)) throw new Error(`baseline ${spec} does not exist`);
    try {
        return surfaceFromNpm(spec);
    } catch (err) {
        // An explicit --baseline <version> that npm cannot fetch is an error, not a fallback.
        if (spec || !existsSync(committedBaseline)) throw err;
        console.warn(`api-check: npm unreachable (${err.message.split("\n")[0]}); falling back to ${relative(repoRoot, committedBaseline)}`);
        const json = JSON.parse(readFileSync(committedBaseline, "utf8"));
        return { version: json.version, source: `${relative(repoRoot, committedBaseline)} (offline fallback)`, surface: json.surface };
    }
}

function table(rows, headers) {
    const widths = headers.map((h, i) => Math.max(h.length, ...rows.map((r) => String(r[i]).length)));
    const line = (cells) => "| " + cells.map((c, i) => String(c).padEnd(widths[i])).join(" | ") + " |";
    return [line(headers), "|" + widths.map((w) => "-".repeat(w + 2)).join("|") + "|", ...rows.map(line)].join("\n");
}

function gitHead() {
    try {
        return execFileSync("git", ["rev-parse", "HEAD"], { cwd: repoRoot, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim();
    } catch {
        return "(git unavailable)";
    }
}

function loadBaselineOrExit() {
    try {
        return loadBaseline();
    } catch (err) {
        console.error(`api-check: ${err.message.split("\n")[0]}`);
        process.exit(1);
    }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
    const baseline = loadBaselineOrExit();

    if (has("--write-baseline")) {
        const json = { version: baseline.version, source: baseline.source, surface: baseline.surface };
        writeFileSync(committedBaseline, JSON.stringify(json, null, 2) + "\n");
        console.log(`api-check: wrote ${relative(repoRoot, committedBaseline)} from motely-wasm@${baseline.version}`);
        process.exit(0);
    }

    const typingsPath = resolve(arg("--typings") ?? defaultTypings);
    if (!existsSync(typingsPath)) {
        console.error(`api-check: ${typingsPath} not found. Run \`npm run build\` first (bin/ is gitignored, the typings only exist after dotnet publish).`);
        process.exit(1);
    }
    const current = parseSurface(readFileSync(typingsPath, "utf8"));
    const currentVersion = arg("--version") ?? readMotelyVersion();
    const from = parseSemver(baseline.version);
    const to = parseSemver(currentVersion);
    const bump = bumpKind(from, to);

    const { removed, added: addedNames, changed, extended } = diffSurfaces(baseline.surface, current);
    const added = [...addedNames, ...extended.map((e) => e.symbol)].sort();

    console.log(`\napi-check: motely-wasm ${baseline.version} (published) -> ${currentVersion} (this build): ${bump} bump`);
    console.log(`  baseline: ${baseline.source}`);
    console.log(`  current:  ${relative(process.cwd(), typingsPath) || typingsPath}\n`);

    const rows = [
        ...removed.map((s) => ["REMOVED", s, ""]),
        // Containers get member-level detail; a function/const/let/alias is one signature → show both.
        ...changed.map(({ symbol, from, to, members }) => ["CHANGED", symbol, (members && memberDetailText(members)) || `${from}  →  ${to}`]),
        ...extended.map(({ symbol, members }) => ["added", symbol, memberDetailText(members)]),
        ...addedNames.map((s) => ["added", s, ""]),
    ];
    console.log(rows.length ? table(rows, ["status", "symbol", "detail"]) : "  no API changes against the published typings");

    const errors = [];
    const warnings = [];
    if (compareSemver(to, from) <= 0)
        errors.push(`version ${currentVersion} is not above the published ${baseline.version}: bump <MotelyVersion> in Directory.Build.props`);
    if ((removed.length || changed.length) && bump !== "major")
        errors.push(
            `${removed.length} removed + ${changed.length} changed exported name(s) need a MAJOR bump (${from.major}.x -> ${from.major + 1}.0.0), ` +
                `this build is ${bump}: ${[...removed, ...changed.map((c) => c.symbol)].join(", ")}`,
        );
    if (added.length && bump === "patch") warnings.push(`${added.length} added name(s) under a patch bump; a minor bump (${from.major}.${from.minor + 1}.0) says so: ${added.join(", ")}`);
    if (added.length && bump === "none") warnings.push(`${added.length} added name(s) with no version bump at all: ${added.join(", ")}`);

    console.log(`\njaml-lang: publish jaml-lang from motelyjaml ${currentVersion} @ ${gitHead()} (separate repo, not published by this gate)`);

    for (const w of warnings) console.warn(`\napi-check WARN: ${w}`);
    for (const e of errors) console.error(`\napi-check FAIL: ${e}`);
    if (errors.length) process.exit(1);
    console.log(`\napi-check: OK (${removed.length} removed, ${changed.length} changed, ${added.length} added; ${bump} bump allows it)`);
}
