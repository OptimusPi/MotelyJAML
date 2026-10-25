import { useEffect, useRef, useState } from "react";
import {
  JimboButton,
  JimboErrorBlock,
  JimboSectionHeader,
  JimboStatusPill,
  JimboText,
  JimboTextInput,
} from "jaml-ui";
import { runAgent, type AguiProgress } from "./agui.js";

// The server-agent panel: a thin AG-UI client for Motely.MCP's /agui endpoint, which runs the
// deterministic MotelySearchAgent (the real engine server-side, no model in the loop). The
// Vite dev server proxies /agui to the MCP server (vite.config.ts), so the browser always
// calls same-origin and CORS never comes into it.

/** Seeds are 8 characters from 1-9 and A-Z — the same alphabet the MCP server announces. */
const SEED_RE = /^[1-9A-Z]{8}$/;

const numberFmt = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

type RunStatus = "running" | "done" | "stopped" | "error";

interface RunRecord {
  prompt: string;
  status: RunStatus;
  /** Assistant narration lines (everything that isn't a bare seed). */
  narration: string[];
  /** Bare-seed text messages, in arrival order — the live match feed. */
  seeds: string[];
  progress: AguiProgress | null;
  /** Transport failure (server down) or the agent's RUN_ERROR message. */
  error?: string;
}

export function AgentPanel() {
  const [runs, setRuns] = useState<RunRecord[]>([]);
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  // One thread per page load: every run continues the same conversation, per the AG-UI spec.
  const [threadId] = useState(() => crypto.randomUUID().replaceAll("-", ""));
  const abortRef = useRef<AbortController | null>(null);
  const logRef = useRef<HTMLDivElement | null>(null);

  const active = busy ? runs[runs.length - 1] : null;
  useEffect(() => {
    logRef.current?.scrollIntoView({ block: "end", behavior: "smooth" });
  }, [runs, busy]);

  async function submit(prompt: string) {
    const text = prompt.trim();
    if (!text || busy) return;
    setInput("");
    setBusy(true);

    const record: RunRecord = { prompt: text, status: "running", narration: [], seeds: [], progress: null };
    setRuns((rs) => [...rs, record]);
    const index = runs.length;

    const patch = (p: Partial<RunRecord>) =>
      setRuns((rs) => rs.map((r, i) => (i === index ? { ...r, ...p } : r)));

    // Send the whole conversation each run — the spec's stateless model. The agent only reads
    // the last user message, so history is context for the future, not behavior today.
    const history = [
      ...runs.flatMap((r) => [
        { role: "user" as const, content: r.prompt },
        { role: "assistant" as const, content: [...r.narration, ...r.seeds].join("\n") },
      ]),
      { role: "user" as const, content: text },
    ];

    const controller = new AbortController();
    abortRef.current = controller;
    try {
      await runAgent(
        { threadId, messages: history },
        {
          onText: (t) =>
            setRuns((rs) =>
              rs.map((r, i) =>
                i === index
                  ? SEED_RE.test(t)
                    ? { ...r, seeds: [...r.seeds, t] }
                    : { ...r, narration: [...r.narration, t] }
                  : r,
              ),
            ),
          onProgress: (progress) => patch({ progress }),
          onRunFinished: () => patch({ status: "done" }),
          onRunError: (message) => patch({ status: "error", error: message }),
        },
        controller.signal,
      );
      setRuns((rs) => {
        const r = rs[index];
        // A completed stream without RUN_FINISHED still counts as done (best-effort stream).
        return r && r.status === "running" ? rs.map((x, i) => (i === index ? { ...x, status: "done" } : x)) : rs;
      });
    } catch (e) {
      if (controller.signal.aborted) {
        patch({ status: "stopped" });
      } else {
        patch({
          status: "error",
          error:
            "Could not reach the MCP server. Start it with `dotnet run --project Motely.MCP` " +
            `(or \`aspire run\`) and try again. (${e instanceof Error ? e.message : String(e)})`,
        });
      }
    } finally {
      setBusy(false);
      abortRef.current = null;
    }
  }

  function stop() {
    abortRef.current?.abort();
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.8rem" }}>
      <JimboSectionHeader label="Server agent — AG-UI / MotelySearchAgent" tone="purple" />
      <JimboText size="xs" tone="grey">
        The server agent runs the real engine in Motely.MCP (no model in the loop, so a request
        always replays the same run). Name a filter from JamlFilters/, with or without{" "}
        <code>search</code>: try <code>boss-the-eye</code>, <code>Zerkeo_Pure keyword COLA</code>,{" "}
        <code>longColaDanker random 500</code>.
      </JimboText>

      {runs.length === 0 ? (
        <JimboText size="sm" tone="grey">
          No runs yet — ask for a filter above.
        </JimboText>
      ) : (
        <div ref={logRef} style={{ display: "flex", flexDirection: "column", gap: "0.9rem", maxHeight: "55vh", overflowY: "auto", paddingRight: "0.3rem" }}>
          {runs.map((run, i) => (
            <RunCard key={i} run={run} live={run === active} />
          ))}
        </div>
      )}

      <form
        style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}
        onSubmit={(e) => {
          e.preventDefault();
          void submit(input);
        }}
      >
        <div style={{ flex: 1 }}>
          <JimboTextInput
            value={input}
            onChange={(e) => setInput(e.target.value)}
            placeholder='search boss-the-eye — or "boss-the-eye keyword DANK"'
            disabled={busy}
            style={{ width: "100%" }}
            aria-label="Filter to search"
          />
        </div>
        {busy ? (
          <JimboButton tone="red" onClick={stop}>
            Stop
          </JimboButton>
        ) : (
          <JimboButton tone="blue" type="submit" disabled={!input.trim()}>
            Run
          </JimboButton>
        )}
      </form>
    </div>
  );
}

function RunCard({ run, live }: { run: RunRecord; live: boolean }) {
  return (
    <div
      style={{
        border: "1px solid var(--j-inner-border)",
        borderRadius: 8,
        padding: "0.7rem 0.9rem",
        display: "flex",
        flexDirection: "column",
        gap: "0.55rem",
        background: "var(--j-surface-inset)",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: "0.6rem", flexWrap: "wrap" }}>
        <JimboText size="sm" tone="white">
          {run.prompt}
        </JimboText>
        <JimboStatusPill
          status={run.status === "running" ? "running" : run.status === "done" ? "ok" : "error"}
          label={live ? "running" : run.status}
        />
      </div>

      {run.progress ? <ProgressRow progress={run.progress} done={run.status !== "running"} /> : null}

      {run.narration.map((line, i) => (
        <JimboText key={i} size="sm" tone="grey">
          {line}
        </JimboText>
      ))}

      {run.seeds.length > 0 ? (
        <div style={{ display: "flex", flexWrap: "wrap", gap: "0.3rem" }}>
          {run.seeds.map((seed) => (
            <SeedChip key={seed} seed={seed} />
          ))}
        </div>
      ) : null}

      {run.error ? <JimboErrorBlock title="Run failed">{run.error}</JimboErrorBlock> : null}
    </div>
  );
}

function ProgressRow({ progress, done }: { progress: AguiProgress; done: boolean }) {
  const pct = Math.min(100, Math.max(0, progress.percent));
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "0.3rem" }}>
      <div
        role="progressbar"
        aria-valuenow={Math.round(pct)}
        style={{
          height: 10,
          borderRadius: 5,
          background: "var(--j-darkest)",
          border: "1px solid var(--j-inner-border)",
          overflow: "hidden",
        }}
      >
        <div
          style={{
            width: `${pct}%`,
            height: "100%",
            background: done ? "var(--j-green)" : "var(--j-blue)",
            transition: "width 0.2s ease",
          }}
        />
      </div>
      <JimboText size="xs" tone="grey">
        {numberFmt.format(progress.searched)} searched · {numberFmt.format(progress.matched)} matched
        · {numberFmt.format(Math.round(progress.rate))} seeds/s
        {progress.elapsed ? ` · ${progress.elapsed}` : ""}
      </JimboText>
    </div>
  );
}

/** One matched seed as a real Balatro button (hover brightens, press sinks — jaml-ui's face).
 * Click copies the seed; the chip flashes green "copied!" for a moment. */
function SeedChip({ seed }: { seed: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <JimboButton
      size="xs"
      tone={copied ? "green" : "blue"}
      title={`Click to copy ${seed}`}
      onClick={() => {
        void navigator.clipboard?.writeText(seed).then(
          () => {
            setCopied(true);
            setTimeout(() => setCopied(false), 1200);
          },
          () => {},
        );
      }}
    >
      {copied ? "copied!" : seed}
    </JimboButton>
  );
}
