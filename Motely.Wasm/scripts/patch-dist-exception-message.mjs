// Gives JS the C# exception's message. Runs as a postbuild step after `dotnet publish`
// regenerates bin/motely-wasm/ from scratch.
//
// Why: the NativeAOT-LLVM runtime Bootsharp ships (dotnet/dotnet.runtime.js) turns every C#
// exception into `new Error("C# exception from NativeAOT")`. The managed side
// (JSMarshalerArgument.ToJS(Exception)) still writes `ex.Message` into the argument slot as a
// UTF-16 string (IntPtrValue @0 + Length @8; Type @12 becomes Exception, GCHandle @4), and the
// runtime's own string marshaller sits right next to it. So `Search.settings(badJaml)` threw
// a message-less Error while the loader's `JAML line 3: ...` text was in wasm memory, leaked.
// This rewires the exception marshaller to read (and free) that string through the string
// marshaller. Sync throws and promise rejections both go through it.
//
// Minified names change between runtime builds, so both functions are found by shape. If the
// shape is gone the script fails: a new runtime either fixed this or moved it, and someone
// has to look.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const MARKER = "/*__exception-message__*/";
const GENERIC = "C# exception from NativeAOT";
const id = "[A-Za-z_$][\\w$]*";

// function Ot(e){return 0==dn(e)?null:new Error("C# exception from NativeAOT")}
const exceptionFn = new RegExp(
    `function (${id})\\(e\\)\\{return 0==(${id})\\(e\\)\\?null:new Error\\("C# exception from NativeAOT"\\)\\}`,
);
// function Nt(e){if(0==dn(e))return null;{const t=_n(e),n=ce(t,t+2*Un(e));return l(t),n}}
const stringFn = (typeFn) =>
    new RegExp(
        `function (${id})\\(e\\)\\{if\\(0==${typeFn.replace(/\$/g, "\\$&")}\\(e\\)\\)return null;` +
            `\\{const t=${id}\\(e\\),n=${id}\\(t,t\\+2\\*${id}\\(e\\)\\);return ${id}\\(t\\),n\\}\\}`,
    );

/** Returns the patched runtime source, or the input unchanged when already patched. Throws when
 *  the runtime no longer has the shapes this patch expects. */
export function patchExceptionMessage(source) {
    if (source.includes(MARKER)) return source;
    const ex = source.match(exceptionFn);
    if (!ex) throw new Error(`no "${GENERIC}" marshaller found; re-check the runtime before shipping`);
    const [whole, name, typeFn] = ex;
    const str = source.match(stringFn(typeFn));
    if (!str) throw new Error(`no string marshaller next to ${name}; re-check the runtime before shipping`);
    const replacement =
        `${MARKER}function ${name}(e){if(0==${typeFn}(e))return null;let m=null;` +
        `try{m=${str[1]}(e)}catch(x){}return new Error(m||"${GENERIC}")}`;
    return source.replace(whole, () => replacement);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
    const path = join(dirname(fileURLToPath(import.meta.url)), "..", "bin", "motely-wasm", "dotnet", "dotnet.runtime.js");
    const original = readFileSync(path, "utf8");
    const patched = patchExceptionMessage(original);
    if (patched === original) {
        console.log("patch-dist-exception-message: dotnet/dotnet.runtime.js already patched, skipping");
    } else {
        writeFileSync(path, patched, "utf8");
        console.log("patch-dist-exception-message: rewired the exception marshaller in bin/motely-wasm/dotnet/dotnet.runtime.js");
    }
}
