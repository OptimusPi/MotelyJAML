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
exports.resolveMotelyLsp = resolveMotelyLsp;
exports.diagnoseJaml = diagnoseJaml;
exports.resolveMotelyCli = resolveMotelyCli;
exports.searchSeeds = searchSeeds;
exports.parseScoredSeeds = parseScoredSeeds;
exports.explainTopic = explainTopic;
exports.formatSearchMarkdown = formatSearchMarkdown;
exports.formatDiagnoseMarkdown = formatDiagnoseMarkdown;
exports.escapePipes = escapePipes;
/**
 * Resolve and invoke the Motely.Lsp binary for one-shot engine jobs (not the
 * long-running language server session). Same resolution order as LSP start.
 */
const node_child_process_1 = require("node:child_process");
const fs = __importStar(require("node:fs"));
const os = __importStar(require("node:os"));
const path = __importStar(require("node:path"));
const vscode = __importStar(require("vscode"));
/**
 * One resolution order for every Motely binary the extension shells out to:
 * 1. the configured `jaml.<settingKey>`
 * 2. a bundled build under `server/`
 * 3. the workspace project via `dotnet run --project`
 */
function resolveMotelyBinary(context, spec) {
    const configured = vscode.workspace
        .getConfiguration("jaml")
        .get(spec.settingKey)
        ?.trim();
    if (configured) {
        if (!fs.existsSync(configured)) {
            throw new Error(`jaml.${spec.settingKey} does not exist: ${configured}`);
        }
        return { command: configured, args: [], display: configured };
    }
    const bundledName = process.platform === "win32" ? `${spec.exeName}.exe` : spec.exeName;
    const bundled = path.join(context.extensionPath, "server", bundledName);
    if (fs.existsSync(bundled)) {
        return { command: bundled, args: [], display: spec.bundledDisplay };
    }
    const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (folder) {
        const csproj = path.join(folder, spec.projectDir, `${spec.projectDir}.csproj`);
        if (fs.existsSync(csproj)) {
            return {
                command: "dotnet",
                args: ["run", "--project", csproj, ...spec.runArgs, "--no-launch-profile", "--"],
                display: `dotnet run ${spec.projectDir}`,
            };
        }
    }
    throw new Error(spec.notFoundHint);
}
function resolveMotelyLsp(context) {
    return resolveMotelyBinary(context, {
        settingKey: "serverPath",
        exeName: "Motely.Lsp",
        projectDir: "Motely.Lsp",
        bundledDisplay: "bundled",
        // `-v q` is load-bearing, not tidiness: this process speaks LSP framing on stdout, and
        // `dotnet run` prints its build output there first. At default verbosity the client's
        // first bytes are "Determining projects to restore..." and the session never handshakes.
        runArgs: ["-c", "Release", "-v", "q"],
        notFoundHint: "Motely.Lsp not found. Set jaml.serverPath, publish Motely.Lsp into vscode-jaml/server/, " +
            "or open the MotelyJAML workspace.",
    });
}
/** Run engine diagnose on text or an absolute file path. */
async function diagnoseJaml(context, input) {
    const proc = resolveMotelyLsp(context);
    let filePath = input.filePath?.trim();
    let tempPath;
    if (!filePath && input.jamlText !== undefined) {
        tempPath = path.join(os.tmpdir(), `motely-validate-${Date.now()}.jaml`);
        fs.writeFileSync(tempPath, input.jamlText, "utf8");
        filePath = tempPath;
    }
    if (!filePath) {
        throw new Error("Provide jamlText or filePath.");
    }
    if (!fs.existsSync(filePath)) {
        throw new Error(`File not found: ${filePath}`);
    }
    try {
        const args = [...proc.args, "--diagnose", filePath];
        const { stdout, stderr, code } = await run(proc.command, args);
        let diagnostics = [];
        const raw = stdout.trim();
        if (raw) {
            try {
                diagnostics = JSON.parse(raw);
            }
            catch {
                throw new Error(`Motely.Lsp --diagnose returned non-JSON (exit ${code}): ${raw || stderr}`);
            }
        }
        return {
            ok: code === 0 && diagnostics.length === 0,
            diagnostics,
            raw,
            exitCode: code ?? 1,
            via: proc.display,
        };
    }
    finally {
        if (tempPath) {
            try {
                fs.unlinkSync(tempPath);
            }
            catch {
                /* ignore */
            }
        }
    }
}
function resolveMotelyCli(context) {
    return resolveMotelyBinary(context, {
        settingKey: "cliPath",
        exeName: "Motely.CLI",
        projectDir: "Motely.CLI",
        bundledDisplay: "bundled CLI",
        runArgs: ["-c", "Release"],
        notFoundHint: "Motely.CLI not found. Set jaml.cliPath, publish Motely.CLI into vscode-jaml/server/, " +
            "or open the MotelyJAML workspace.",
    });
}
/**
 * Real Motely search: `--jaml <temp> --collect N -q`.
 * Always copies the filter to a temp file so CLI seed-rewrite does not touch the user's path.
 */
async function searchSeeds(context, input) {
    const collectN = Math.max(1, Math.min(input.collectN ?? 1, 100));
    const proc = resolveMotelyCli(context);
    let sourceText = input.jamlText;
    if (!sourceText && input.filePath?.trim()) {
        const p = input.filePath.trim();
        if (!fs.existsSync(p)) {
            throw new Error(`File not found: ${p}`);
        }
        sourceText = fs.readFileSync(p, "utf8");
    }
    if (sourceText === undefined) {
        throw new Error("Provide jamlText or filePath for search.");
    }
    // Validate first so we don't burn a long search on garbage.
    const diag = await diagnoseJaml(context, { jamlText: sourceText });
    if (!diag.ok) {
        const first = diag.diagnostics[0]?.message ?? "invalid JAML";
        throw new Error(`JAML invalid — fix with validate first. ${first}`);
    }
    const tempPath = path.join(os.tmpdir(), `motely-search-${Date.now()}.jaml`);
    fs.writeFileSync(tempPath, sourceText, "utf8");
    const timeoutMs = vscode.workspace.getConfiguration("jaml").get("searchTimeoutMs") ?? 120_000;
    try {
        const args = [
            ...proc.args,
            "--jaml",
            tempPath,
            "--collect",
            String(collectN),
            "-q",
            "--threads",
            "4",
        ];
        const { stdout, stderr, code, timedOut } = await run(proc.command, args, timeoutMs);
        if (timedOut) {
            throw new Error(`Motely.CLI timed out after ${timeoutMs}ms (via ${proc.display}). Raise jaml.searchTimeoutMs or simplify the filter.`);
        }
        if (code !== 0 && code !== null) {
            throw new Error(`Motely.CLI exited ${code} (via ${proc.display}). ${stderr.trim() || stdout.trim() || "no output"}`);
        }
        const seeds = parseScoredSeeds(stdout);
        return {
            seeds,
            collectN,
            via: proc.display,
            exitCode: code ?? 0,
            stdout,
            stderr,
            filterPath: tempPath,
            timedOut: false,
        };
    }
    finally {
        try {
            fs.unlinkSync(tempPath);
        }
        catch {
            /* ignore */
        }
    }
}
/** Parse Motely.CLI CSV-ish result lines: `SEED, score,...` */
function parseScoredSeeds(stdout) {
    const out = [];
    const seen = new Set();
    for (const line of stdout.split(/\r?\n/)) {
        const m = line.match(/^([1-9A-Z]{1,8}),\s*(-?\d+)/i);
        if (!m) {
            continue;
        }
        const seed = m[1].toUpperCase();
        if (seen.has(seed)) {
            continue;
        }
        seen.add(seed);
        out.push({ seed, score: Number(m[2]) });
    }
    return out;
}
/** Engine schema explain: Motely.Lsp --explain <topic> */
async function explainTopic(context, topic) {
    const q = topic.trim();
    if (!q) {
        throw new Error("Empty topic. Try: joker, must, voucher, Perkeo, pokerHand.");
    }
    const proc = resolveMotelyLsp(context);
    const args = [...proc.args, "--explain", ...q.split(/\s+/)];
    const { stdout, stderr, code } = await run(proc.command, args);
    const raw = stdout.trim();
    if (!raw) {
        throw new Error(`Motely.Lsp --explain returned empty (exit ${code}): ${stderr}`);
    }
    let parsed;
    try {
        parsed = JSON.parse(raw);
    }
    catch {
        throw new Error(`Motely.Lsp --explain non-JSON: ${raw}`);
    }
    return {
        ok: parsed.ok && !!parsed.markdown,
        topic: parsed.topic ?? q,
        markdown: parsed.markdown,
        via: proc.display,
    };
}
function formatSearchMarkdown(result, label) {
    if (result.seeds.length === 0) {
        return [
            `**No seeds found** (Motely via \`${result.via}\`, collect ${result.collectN})`,
            "",
            `Filter: \`${label}\``,
            "",
            "Engine ran; zero matches in the collect sweep. Try a looser filter or higher collect N.",
        ].join("\n");
    }
    const rows = result.seeds.map((s) => `| \`${s.seed}\` | ${s.score} |`);
    const listed = result.seeds.map((s) => s.seed).join(", ");
    return [
        `**Found ${result.seeds.length} seed(s)** (Motely via \`${result.via}\`, collect ${result.collectN}; SIMD may overshoot)`,
        "",
        `Filter: \`${label}\``,
        "",
        "| Seed | Score |",
        "|------|------:|",
        ...rows,
        "",
        "```",
        listed,
        "```",
    ].join("\n");
}
function run(command, args, timeoutMs) {
    return new Promise((resolve, reject) => {
        const child = (0, node_child_process_1.spawn)(command, args, { stdio: ["ignore", "pipe", "pipe"] });
        let stdout = "";
        let stderr = "";
        let timedOut = false;
        let timer;
        if (timeoutMs && timeoutMs > 0) {
            timer = setTimeout(() => {
                timedOut = true;
                child.kill("SIGTERM");
                setTimeout(() => child.kill("SIGKILL"), 2000);
            }, timeoutMs);
        }
        child.stdout.on("data", (c) => {
            stdout += c.toString("utf8");
        });
        child.stderr.on("data", (c) => {
            stderr += c.toString("utf8");
        });
        child.on("error", (err) => {
            if (timer) {
                clearTimeout(timer);
            }
            reject(err);
        });
        child.on("close", (code) => {
            if (timer) {
                clearTimeout(timer);
            }
            resolve({ stdout, stderr, code, timedOut });
        });
    });
}
function formatDiagnoseMarkdown(result, label) {
    if (result.ok) {
        return [
            `**JAML valid** (Motely engine via \`${result.via}\`)`,
            "",
            `Source: \`${label}\``,
        ].join("\n");
    }
    const rows = result.diagnostics.map((d) => `| L${d.startLine + 1}:${d.startColumn + 1} | ${d.severity} | \`${d.code}\` | ${escapePipes(d.message)} |`);
    return [
        `**JAML invalid** — ${result.diagnostics.length} issue(s) (via \`${result.via}\`)`,
        "",
        `Source: \`${label}\``,
        "",
        "| Loc | Sev | Code | Message |",
        "|-----|-----|------|---------|",
        ...rows,
    ].join("\n");
}
/** Make a value safe to drop into a markdown table cell. */
function escapePipes(s) {
    return s.replace(/\|/g, "\\|").replace(/\n/g, " ");
}
//# sourceMappingURL=motelyEngine.js.map