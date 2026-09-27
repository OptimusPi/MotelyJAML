// Pins the root `CancellationToken` export to the real bcl class. Runs as a postbuild step
// after `dotnet publish` regenerates bin/motely-wasm/ from scratch.
//
// Why: Bootsharp's root index.mjs does `export * from "./bcl/index.mjs"` (the class) AND
// `export * from "./generated/modules/index.g.mjs"`, which exports an internal marshalling
// object with the same name. A name two `export *` clauses disagree on is dropped from the
// namespace: `import { CancellationToken } from "motely-wasm"` is a link-time SyntaxError in
// Node ("conflicting star exports") and silently `undefined` through bundlers, while tsc
// resolves the type to the class and reports nothing. An explicit export beats star exports
// by spec, so one line settles it in both index.mjs and index.d.mts.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const dist = join(dirname(fileURLToPath(import.meta.url)), "..", "bin", "motely-wasm");
const MARKER = "// __cancellation-token-export__";
const line = `${MARKER}\nexport { CancellationToken } from "./bcl/cancellation.mjs";\n`;

for (const file of ["index.mjs", "index.d.mts"]) {
    const path = join(dist, file);
    const original = readFileSync(path, "utf8");
    if (original.includes(MARKER)) {
        console.log(`patch-dist-cancellation-export: ${file} already patched, skipping`);
        continue;
    }
    writeFileSync(path, `${original.trimEnd()}\n${line}`, "utf8");
    console.log(`patch-dist-cancellation-export: appended to bin/motely-wasm/${file}`);
}
