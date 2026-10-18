"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
Object.defineProperty(exports, "__esModule", { value: true });
exports.activate = activate;
exports.deactivate = deactivate;
/**
 * VS Code client for Motely.Lsp + @jimbo chat participant.
 *
 * Language: Motely.Lsp over stdio (engine grammar only — no TS reimplementation).
 * Chat: @jimbo — slash engine paths + freeform LM tool loop (J4).
 */
const vscode = __importStar(require("vscode"));
const node_1 = require("vscode-languageclient/node");
const jimboChat_1 = require("./jimboChat");
const motelyEngine_1 = require("./motelyEngine");
const motelyTools_1 = require("./motelyTools");
const motelyWasm_1 = require("./motelyWasm");
let client;
async function activate(context) {
    (0, jimboChat_1.registerJimboChat)(context);
    (0, motelyTools_1.registerMotelyTools)(context);
    void (0, motelyWasm_1.wasmVersion)().then((version) => vscode.window.setStatusBarMessage(`JAML: Motely WASM ${version} + @jimbo`, 4000), () => undefined);
    // Not awaited: in the workspace-fallback path this is a `dotnet run`, so awaiting it holds
    // activation open for a full restore + build. Chat and tools do not depend on the server.
    void startLanguageServer(context).catch((err) => {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showWarningMessage(`JAML LSP: ${msg}`);
    });
}
async function startLanguageServer(context) {
    const server = (0, motelyEngine_1.resolveMotelyLsp)(context);
    // Long-running server: no --diagnose flags.
    const serverOptions = {
        run: { command: server.command, args: [...server.args], transport: node_1.TransportKind.stdio },
        debug: { command: server.command, args: [...server.args], transport: node_1.TransportKind.stdio },
    };
    const clientOptions = {
        documentSelector: [{ scheme: "file", language: "jaml" }, { scheme: "untitled", language: "jaml" }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher("**/*.jaml"),
        },
    };
    client = new node_1.LanguageClient("jaml", "Motely JAML Language Server", serverOptions, clientOptions);
    context.subscriptions.push(client);
    await client.start();
    vscode.window.setStatusBarMessage(`JAML: Motely.Lsp (${server.display}) + @jimbo + tools`, 4000);
}
async function deactivate() {
    if (client) {
        await client.stop();
        client = undefined;
    }
}
//# sourceMappingURL=extension.js.map