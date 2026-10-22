// WebMCP registration for the seed lab (W3C WebMCP draft: document.modelContext.registerTool).
// Two tools — seed_validate and seed_search — are registered while the SeedLab view is mounted
// and aborted on unmount (the draft has no unregisterTool: abort the signal the registration
// returned). modelContext exists only in supporting browsers and only in secure contexts; when
// it is absent this hook degrades silently — no errors, no UI.
//
// Typed minimally and defensively: only the registerTool surface is declared, everything is
// optional-chained, and an unexpected return shape simply means "nothing to abort".

import { useEffect, useState } from "react";
import { seedLabTools } from "./webmcp-tools.ts";

interface RegisteredTool {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
  execute: (args: unknown) => Promise<unknown>;
}

interface ModelContextLike {
  registerTool?: (tool: RegisteredTool) => unknown;
}

function getModelContext(): ModelContextLike | null {
  if (typeof document === "undefined") return null;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const mc = (document as any).modelContext;
  return mc && typeof mc.registerTool === "function" ? (mc as ModelContextLike) : null;
}

function abortRegistration(registration: unknown): void {
  if (!registration || typeof registration !== "object") return;
  const { signal, abort } = registration as { signal?: unknown; abort?: unknown };
  // Call as a method so AbortController.abort keeps its `this`.
  if (signal instanceof AbortSignal && !signal.aborted && typeof abort === "function") {
    (registration as { abort(): void }).abort();
  }
  // Any other return shape means there is nothing we can abort — the draft exposes no
  // unregisterTool, and registrations are scoped to the page regardless.
}

/**
 * Registers the seed lab's tools on a modelContext and returns a handle that aborts every
 * registration. Returns null when modelContext is absent or unsupported — the caller then
 * quietly skips the agent surface.
 */
export function registerSeedLabTools(modelContext: unknown): { abort(): void } | null {
  const mc = modelContext as ModelContextLike | null;
  if (!mc || typeof mc.registerTool !== "function") return null;
  const registrations = seedLabTools.map((tool) => mc.registerTool!(tool as RegisteredTool));
  return {
    abort() {
      for (const registration of registrations) abortRegistration(registration);
    },
  };
}

/**
 * Registers the seed lab's WebMCP tools on mount and aborts them on unmount.
 * Returns whether modelContext was found and the tools registered — the app shows its
 * "agent-ready" badge only when this is true.
 */
export function useSeedLabWebMCP(): boolean {
  const [registered, setRegistered] = useState(false);

  useEffect(() => {
    const registration = registerSeedLabTools(getModelContext());
    if (!registration) return;
    setRegistered(true);
    return () => {
      setRegistered(false);
      registration.abort();
    };
  }, []);

  return registered;
}
