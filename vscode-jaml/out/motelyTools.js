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
exports.registerMotelyTools = registerMotelyTools;
/**
 * Language model tools (MS Tools API):
 *  J1 motely_validate_jaml
 *  J2 motely_search_seeds
 *  J3 motely_explain_jaml
 *  J4 freeform @jimbo invokes these via lm.invokeTool
 * https://code.visualstudio.com/api/extension-guides/ai/tools
 */
const fs = __importStar(require("node:fs"));
const vscode = __importStar(require("vscode"));
const motelyEngine_1 = require("./motelyEngine");
const motelyWasm_1 = require("./motelyWasm");
function registerMotelyTools(context) {
    context.subscriptions.push(vscode.lm.registerTool("motely_validate_jaml", new ValidateJamlTool()), vscode.lm.registerTool("motely_search_seeds", new SearchSeedsTool(context)), vscode.lm.registerTool("motely_explain_jaml", new ExplainJamlTool(context)));
}
class ValidateJamlTool {
    async prepareInvocation(options, _token) {
        const where = options.input.filePath?.trim() ||
            (options.input.jamlText !== undefined ? "pasted JAML text" : "active editor?");
        return {
            invocationMessage: "Validating JAML with Motely engine",
            confirmationMessages: {
                title: "Validate JAML",
                message: new vscode.MarkdownString(`Run Motely engine validate on **${where}**? (in-process WASM, no network.)`),
            },
        };
    }
    async invoke(options, _token) {
        try {
            const input = await resolveJamlInput(options.input);
            const jamlText = input.jamlText ?? fs.readFileSync(input.filePath, "utf8");
            const error = await (0, motelyWasm_1.validateWithWasm)(jamlText);
            if (!error) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(`**JAML valid** (Motely WASM engine)\n\nSource: \`${input.label}\``),
                ]);
            }
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(`**JAML invalid** (Motely WASM engine)\n\n${error}`),
            ]);
        }
        catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            throw new Error(`motely_validate_jaml failed: ${msg}. Ensure the Motely WASM module loaded. Retry with absolute filePath or full jamlText.`);
        }
    }
}
class SearchSeedsTool {
    context;
    constructor(context) {
        this.context = context;
    }
    async prepareInvocation(options, _token) {
        const n = options.input.collectN ?? 1;
        const where = options.input.filePath?.trim() ||
            (options.input.jamlText !== undefined ? "pasted JAML" : "active .jaml");
        return {
            invocationMessage: `Motely seed search (collect ${n})`,
            confirmationMessages: {
                title: "Find Balatro seeds",
                message: new vscode.MarkdownString(`Run **Motely.CLI --collect ${n}** on **${where}**?\n\n` +
                    `Local CPU search (can take seconds–minutes). Filter is copied to a temp file so your disk file is not rewritten.`),
            },
        };
    }
    async invoke(options, _token) {
        try {
            const input = await resolveJamlInput(options.input);
            const result = await (0, motelyEngine_1.searchSeeds)(this.context, {
                ...input,
                collectN: options.input.collectN,
            });
            const md = (0, motelyEngine_1.formatSearchMarkdown)(result, input.label);
            return new vscode.LanguageModelToolResult([new vscode.LanguageModelTextPart(md)]);
        }
        catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            throw new Error(`motely_search_seeds failed: ${msg}. Validate JAML first. Need Motely.CLI (jaml.cliPath or MotelyJAML workspace).`);
        }
    }
}
class ExplainJamlTool {
    context;
    constructor(context) {
        this.context = context;
    }
    async prepareInvocation(options, _token) {
        return {
            invocationMessage: `Explain JAML: ${options.input.topic}`,
            confirmationMessages: {
                title: "Explain JAML",
                message: new vscode.MarkdownString(`Look up **${options.input.topic}** in the Motely engine schema? (local, no network.)`),
            },
        };
    }
    async invoke(options, _token) {
        try {
            const result = await (0, motelyEngine_1.explainTopic)(this.context, options.input.topic ?? "");
            if (!result.ok || !result.markdown) {
                return new vscode.LanguageModelToolResult([
                    new vscode.LanguageModelTextPart(`Unknown JAML topic \`${result.topic}\` (via \`${result.via}\`). Try a discriminator (\`joker\`, \`voucher\`), root key (\`must\`), or name (\`Perkeo\`).`),
                ]);
            }
            return new vscode.LanguageModelToolResult([
                new vscode.LanguageModelTextPart(result.markdown + `\n\n_via \`${result.via}\`_`),
            ]);
        }
        catch (err) {
            const msg = err instanceof Error ? err.message : String(err);
            throw new Error(`motely_explain_jaml failed: ${msg}`);
        }
    }
}
async function resolveJamlInput(params) {
    if (params.filePath?.trim()) {
        const filePath = params.filePath.trim();
        if (!fs.existsSync(filePath)) {
            throw new Error(`filePath does not exist: ${filePath}`);
        }
        return { filePath, label: filePath };
    }
    if (params.jamlText !== undefined && params.jamlText.length > 0) {
        return { jamlText: params.jamlText, label: "(inline text)" };
    }
    const editor = vscode.window.activeTextEditor;
    if (editor && (editor.document.languageId === "jaml" || editor.document.fileName.endsWith(".jaml"))) {
        if (editor.document.uri.scheme === "file") {
            return { filePath: editor.document.uri.fsPath, label: editor.document.uri.fsPath };
        }
        return { jamlText: editor.document.getText(), label: editor.document.uri.toString() };
    }
    throw new Error("No jamlText, filePath, or active .jaml editor. Pass the document text or an absolute path.");
}
//# sourceMappingURL=motelyTools.js.map