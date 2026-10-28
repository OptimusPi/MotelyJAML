// Prepends a guarded Uint8Array base64/hex polyfill to bin/motely-wasm/index.mjs, the
// package's guaranteed first-loaded entry (package.json main/import). Runs
// as a postbuild step after `dotnet publish` regenerates bin/motely-wasm/ from scratch.
//
// Why: config.mjs's resolveBinary() calls Uint8Array.fromBase64 to decode the
// embedded .NET assemblies during bootsharp.boot(). That TC39 method is native
// only on newer runtimes — Safari 18.2+, Chrome 140+, Firefox 133+, Node 24+.
// Everything older (a 2024 iPhone that hasn't updated, an Android WebView, a
// Vercel serverless Node) lands in the fallback, so the fallback must work in
// EVERY environment: Buffer where it exists (Node), a plain atob/btoa decode
// loop where it does not (every browser back to the beginning of time). Each
// install is guarded — the real (spec) method wins whenever it's present.
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const indexPath = join(here, "..", "bin", "motely-wasm", "index.mjs");

const MARKER = "// __uint8-base64-polyfill__";

const polyfill = `${MARKER}
{
    const __U8 = Uint8Array;
    const __proto = Uint8Array.prototype;
    const __hasBuffer = typeof Buffer === "function" && typeof Buffer.from === "function";
    const __normalize = (s, opts) =>
        opts?.alphabet === "base64url" ? s.replace(/-/g, "+").replace(/_/g, "/") : s;
    const __decodeB64 = (s, opts) => {
        if (__hasBuffer)
            return new Uint8Array(Buffer.from(s, opts?.alphabet === "base64url" ? "base64url" : "base64"));
        const bin = atob(__normalize(s, opts).replace(/=+$/, ""));
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return bytes;
    };
    const __decodeHex = (s) => {
        if (__hasBuffer) return new Uint8Array(Buffer.from(s, "hex"));
        const bytes = new Uint8Array(s.length >> 1);
        for (let i = 0; i < bytes.length; i++) bytes[i] = parseInt(s.substr(i * 2, 2), 16);
        return bytes;
    };
    const __view = (u8) => u8 instanceof Uint8Array ? u8 : new Uint8Array(u8.buffer, u8.byteOffset, u8.byteLength);
    if (typeof __U8.fromBase64 !== "function")
        __U8.fromBase64 = (s, opts) => __decodeB64(s, opts);
    if (typeof __U8.fromHex !== "function")
        __U8.fromHex = (s) => __decodeHex(s);
    if (typeof __proto.toBase64 !== "function")
        __proto.toBase64 = function (opts) {
            if (__hasBuffer) {
                const enc = opts?.alphabet === "base64url" ? "base64url" : "base64";
                return Buffer.from(this.buffer, this.byteOffset, this.byteLength).toString(enc);
            }
            let bin = "";
            const view = __view(this);
            for (let i = 0; i < view.length; i++) bin += String.fromCharCode(view[i]);
            const b64 = btoa(bin);
            return opts?.alphabet === "base64url"
                ? b64.replace(/\\+/g, "-").replace(/\\//g, "_").replace(/=+$/, "")
                : b64;
        };
    if (typeof __proto.toHex !== "function")
        __proto.toHex = function () {
            if (__hasBuffer)
                return Buffer.from(this.buffer, this.byteOffset, this.byteLength).toString("hex");
            const view = __view(this);
            let hex = "";
            for (let i = 0; i < view.length; i++) hex += view[i].toString(16).padStart(2, "0");
            return hex;
        };
    if (typeof __proto.setFromBase64 !== "function")
        __proto.setFromBase64 = function (s, opts) {
            // read counts input CHARACTERS consumed, not bytes written: decode only
            // the whole 4-char groups that fit, so a short target reports honestly.
            const src = __normalize(s, opts).replace(/=+$/, "");
            const maxBytes = this.byteLength;
            let chars = src.length;
            if ((chars * 3) >> 2 > maxBytes) chars = ((maxBytes / 3) | 0) * 4;
            const bytes = __decodeB64(src.slice(0, chars), opts);
            const written = Math.min(bytes.length, maxBytes);
            this.set(bytes.subarray(0, written));
            return { read: chars === src.length ? s.length : chars, written };
        };
    if (typeof __proto.setFromHex !== "function")
        __proto.setFromHex = function (s) {
            // read counts input CHARACTERS consumed (2 hex chars per byte).
            const chars = Math.min(s.length & ~1, this.byteLength * 2);
            const bytes = __decodeHex(s.slice(0, chars));
            const written = bytes.length;
            this.set(bytes);
            return { read: chars, written };
        };
}
`;

const original = readFileSync(indexPath, "utf8");
if (original.includes(MARKER)) {
    console.log("patch-dist-base64-polyfill: already applied, skipping");
} else {
    writeFileSync(indexPath, polyfill + original, "utf8");
    console.log("patch-dist-base64-polyfill: prepended to bin/motely-wasm/index.mjs");
}
