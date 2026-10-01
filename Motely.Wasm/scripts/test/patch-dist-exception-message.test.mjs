// node --test scripts/test/   (from Motely.Wasm/)
// The exception-message patch against the runtime's real minified shapes (copied from the
// NativeAOT-LLVM dotnet.runtime.js Bootsharp 0.9.0 ships), with the slot reads stubbed out.
import { test } from "node:test";
import assert from "node:assert/strict";
import { patchExceptionMessage } from "../patch-dist-exception-message.mjs";

const RUNTIME =
    'function Nt(e){if(0==dn(e))return null;{const t=_n(e),n=ce(t,t+2*Un(e));return l(t),n}}' +
    'function Ot(e){return 0==dn(e)?null:new Error("C# exception from NativeAOT")}' +
    "const Wt=function(e,t,n){if(t(n),nn(n))throw Ot(tn(n,0))};";

/** Evaluates the runtime snippet with a fake slot: `type` 0 is None, anything else carries `text`. */
function marshal(source, slot) {
    const freed = [];
    const run = new Function(
        "slot",
        "freed",
        `const dn=(e)=>e.type,_n=(e)=>e.ptr,Un=(e)=>e.text.length,ce=(a,b)=>slot.text,l=(p)=>freed.push(p);
         ${source}; return Ot(slot);`,
    );
    return { error: run(slot, freed), freed };
}

test("the patched marshaller carries the C# message and frees it", () => {
    const patched = patchExceptionMessage(RUNTIME);
    const { error, freed } = marshal(patched, { type: 15, ptr: 4096, text: "JAML line 3: `NotAJoker` is not a MotelyJoker" });
    assert.equal(error.message, "JAML line 3: `NotAJoker` is not a MotelyJoker");
    assert.deepEqual(freed, [4096]);
});

test("a None slot is still null", () => {
    const { error } = marshal(patchExceptionMessage(RUNTIME), { type: 0, ptr: 0, text: "" });
    assert.equal(error, null);
});

test("an empty message falls back to the generic text", () => {
    const { error } = marshal(patchExceptionMessage(RUNTIME), { type: 15, ptr: 8, text: "" });
    assert.equal(error.message, "C# exception from NativeAOT");
});

test("the unpatched runtime drops the message (the bug this patch exists for)", () => {
    const { error } = marshal(RUNTIME, { type: 15, ptr: 4096, text: "JAML line 3" });
    assert.equal(error.message, "C# exception from NativeAOT");
});

test("patching twice is a no-op", () => {
    const once = patchExceptionMessage(RUNTIME);
    assert.equal(patchExceptionMessage(once), once);
});

test("other minified names are found by shape", () => {
    const renamed = RUNTIME.replaceAll("Ot", "Q$").replaceAll("Nt", "k_").replaceAll("dn", "z9");
    const patched = patchExceptionMessage(renamed);
    assert.match(patched, /function Q\$\(e\)\{if\(0==z9\(e\)\)return null;let m=null;try\{m=k_\(e\)\}/);
});

test("a runtime without the generic marshaller fails loudly", () => {
    assert.throws(() => patchExceptionMessage("function Ot(e){return null}"), /re-check the runtime/);
});

test("a runtime without the string marshaller fails loudly", () => {
    const noString = 'function Ot(e){return 0==dn(e)?null:new Error("C# exception from NativeAOT")}';
    assert.throws(() => patchExceptionMessage(noString), /no string marshaller/);
});
