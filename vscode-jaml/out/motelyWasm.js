"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.validateWithWasm = validateWithWasm;
exports.listWasmItems = listWasmItems;
exports.wasmVersion = wasmVersion;
let runtime;
async function getRuntime() {
    runtime ??= import("motely-wasm");
    const engine = await runtime;
    if (engine.default.getStatus() === engine.default.BootStatus.Standby) {
        await engine.default.boot();
    }
    return engine;
}
async function validateWithWasm(text) {
    return (await getRuntime()).MotelyJaml.validate(text);
}
async function listWasmItems(kind, query) {
    return (await getRuntime()).MotelyJaml.listItems(kind, query);
}
async function wasmVersion() {
    return (await getRuntime()).MotelyWasm.getVersion();
}
//# sourceMappingURL=motelyWasm.js.map