// Minimal AG-UI client for the MCP server's /agui endpoint (AGUI.Server's canonical SSE
// transport). POSTs a RunAgentInput and folds the event stream into callbacks:
// run lifecycle, whole text messages, and state snapshots (the search agent's live progress).
//
// Deliberately React-free — only fetch + SSE parsing — so a node smoke check can import it.
// Event types are parsed defensively: unknown types are ignored and optional fields defaulted,
// because the wire is a stream, not a schema-validated document.

export interface AguiInputMessage {
  role: "user" | "assistant" | "system" | "tool";
  content: string;
}

export interface RunAgentInput {
  threadId?: string;
  runId?: string;
  messages: AguiInputMessage[];
}

export interface AguiProgress {
  percent: number;
  searched: number;
  matched: number;
  rate: number;
  /** Present on the final snapshot: the run's formatted duration. */
  elapsed?: string;
}

export interface AguiHandlers {
  onRunStarted?(threadId: string, runId: string): void;
  /** One complete assistant text message (TEXT_MESSAGE_START/CONTENT/END assembled). */
  onText?(text: string): void;
  /** A STATE_SNAPSHOT event — the agent's live progress widget state. */
  onProgress?(progress: AguiProgress): void;
  onRunFinished?(threadId: string, runId: string): void;
  onRunError?(message: string): void;
}

/** POSTs the run and consumes the SSE stream until the server closes it. Rejects on transport
 * errors and non-2xx responses (the caller turns that into UI); RUN_ERROR is delivered as an
 * onRunError event, not a rejection — it is still a completed run. */
export async function runAgent(
  input: RunAgentInput,
  handlers: AguiHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const res = await fetch("/agui", {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "text/event-stream" },
    body: JSON.stringify(input),
    signal,
  });
  if (!res.ok || !res.body) {
    const detail = (await res.text().catch(() => "")).slice(0, 300);
    throw new Error(`HTTP ${res.status}${detail ? `: ${detail}` : ""}`);
  }

  // Text deltas accumulate per messageId so interleaved messages can't cross the streams.
  const textParts = new Map<string, string>();
  let textOrder: string[] = [];

  await consumeSse(res.body, signal, (evt) => {
    switch (evt.type) {
      case "RUN_STARTED":
        handlers.onRunStarted?.(str(evt.threadId), str(evt.runId));
        break;
      case "RUN_FINISHED":
        handlers.onRunFinished?.(str(evt.threadId), str(evt.runId));
        break;
      case "RUN_ERROR":
        handlers.onRunError?.(str(evt.message) || "run failed");
        break;
      case "TEXT_MESSAGE_START": {
        const id = str(evt.messageId);
        if (!textParts.has(id)) {
          textParts.set(id, "");
          textOrder.push(id);
        }
        break;
      }
      case "TEXT_MESSAGE_CONTENT":
        textParts.set(str(evt.messageId), (textParts.get(str(evt.messageId)) ?? "") + str(evt.delta));
        break;
      case "TEXT_MESSAGE_END": {
        const id = str(evt.messageId);
        const text = (textParts.get(id) ?? "").replace(/\n$/, "");
        textParts.delete(id);
        textOrder = textOrder.filter((x) => x !== id);
        if (text) handlers.onText?.(text);
        break;
      }
      case "STATE_SNAPSHOT":
        handlers.onProgress?.(parseProgress(evt.snapshot));
        break;
    }
  });
}

function parseProgress(snapshot: unknown): AguiProgress {
  const s = (snapshot ?? {}) as Record<string, unknown>;
  const num = (v: unknown) => (typeof v === "number" && Number.isFinite(v) ? v : 0);
  return {
    percent: num(s.percent),
    searched: num(s.searched),
    matched: num(s.matched),
    rate: num(s.rate),
    ...(typeof s.elapsed === "string" ? { elapsed: s.elapsed } : {}),
  };
}

function str(v: unknown): string {
  return typeof v === "string" ? v : "";
}

type SseEvent = Record<string, unknown> & { type?: string };

/** Reads an SSE body event-by-event: buffers chunks, splits on blank lines, joins the data
 * lines of each block and parses them as one JSON event. */
async function consumeSse(
  body: ReadableStream<Uint8Array>,
  signal: AbortSignal | undefined,
  onEvent: (evt: SseEvent) => void,
): Promise<void> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    // Events are separated by a blank line; a block's payload is its `data:` lines.
    let sep: number;
    while ((sep = buffer.search(/\r?\n\r?\n/)) !== -1) {
      const block = buffer.slice(0, sep);
      buffer = buffer.slice(buffer[sep] === "\r" ? sep + 4 : sep + 2);
      const data = block
        .split(/\r?\n/)
        .filter((l) => l.startsWith("data:"))
        .map((l) => l.slice(5).replace(/^ /, ""))
        .join("\n");
      if (!data) continue;
      try {
        const evt = JSON.parse(data) as SseEvent;
        if (evt && typeof evt === "object" && typeof evt.type === "string") onEvent(evt);
      } catch {
        // A partial/invalid block — the next one carries on; the stream is best-effort.
      }
    }
  }

  if (signal?.aborted) throw new DOMException("Aborted", "AbortError");
}
