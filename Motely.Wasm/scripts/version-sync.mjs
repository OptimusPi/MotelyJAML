// The version lives in exactly one place: <MotelyVersion> in Directory.Build.props at the repo
// root. This script copies it into Motely.Wasm/package.json (and Motely.Wasm/tests/package.json
// when that file carries a version field). Hand edits of package.json's version are pointless:
// the next sync overwrites them, and `--check` (run by prepublishOnly) fails the publish while
// the two disagree.
//
//   node scripts/version-sync.mjs           write the version into every package.json
//   node scripts/version-sync.mjs --check   exit 1 if any package.json disagrees with the props
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join, relative } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const wasmDir = join(here, "..");
const repoRoot = join(wasmDir, "..");
const propsPath = join(repoRoot, "Directory.Build.props");

export function readMotelyVersion() {
    const props = readFileSync(propsPath, "utf8");
    const m = props.match(/<MotelyVersion>\s*([^<\s]+)\s*<\/MotelyVersion>/);
    if (!m) throw new Error(`version-sync: no <MotelyVersion> in ${propsPath}`);
    return m[1];
}

const targets = [join(wasmDir, "package.json"), join(wasmDir, "tests", "package.json")].filter(existsSync);

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
    const check = process.argv.includes("--check");
    const version = readMotelyVersion();
    let stale = 0;
    for (const file of targets) {
        const text = readFileSync(file, "utf8");
        const pkg = JSON.parse(text);
        const label = relative(repoRoot, file);
        if (!("version" in pkg)) {
            console.log(`version-sync: ${label} has no version field, left alone`);
            continue;
        }
        if (pkg.version === version) {
            console.log(`version-sync: ${label} = ${version} (in sync)`);
            continue;
        }
        stale++;
        if (check) {
            console.error(`version-sync: ${label} says ${pkg.version}, Directory.Build.props says ${version}. Run \`npm run version:sync\`.`);
            continue;
        }
        // Keep the file's own indentation and trailing newline; only the version line moves.
        const indent = text.match(/^(\s+)"/m)?.[1] ?? "  ";
        const eol = text.endsWith("\n") ? "\n" : "";
        const was = pkg.version;
        pkg.version = version;
        writeFileSync(file, JSON.stringify(pkg, null, indent) + eol);
        console.log(`version-sync: ${label} ${was} -> ${version}`);
    }
    if (check && stale) process.exit(1);
}
