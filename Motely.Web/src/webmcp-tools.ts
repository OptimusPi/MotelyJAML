// The WebMCP tool surface of the seed lab: two tools, `seed_validate` and `seed_search`,
// mirroring the sibling C# MCP server's vocabulary (../Motely.MCP/Tools/MotelyTools.cs) so an
// agent learns one set of words for either surface. Everything runs client-side on the
// already-loaded motely-wasm engine — the same calls jaml-ui's SeedLab makes.
//
// Text contracts intentionally match the C# server's output ("ok: … — n must, n should…",
// "<filter>: n matched / n searched in …", "Error: …"), minus server-only details (the user
// principal, filter-name-vs-text). An agent gets interchangeable results from either surface.
//
// This module is deliberately React-free (and jaml-ui-free): only motely-wasm, so the smoke
// check can import it in node with a real engine. Registration lives in webmcp.ts.

import bootsharp, {
  CancellationToken,
  Errors,
  JamlConfigLoader,
  JamlSearchBuilder,
  MotelyDeck,
  MotelyStake,
} from "motely-wasm";
import type { JamlConfig } from "motely-wasm";

/** motely-wasm is single-threaded; quiet mirrors the SeedLab's own search settings. */
const THREAD_COUNT = 1;

/** Mirrors Motely.MCP's SearchRunner.DefaultCollect. */
export const DEFAULT_COLLECT = 100;

/** Mirrors Motely.MCP's SearchRunner.MaxCollect. */
export const MAX_COLLECT = 10_000;

export const SEARCH_MODES = ["replay", "seeds", "keyword", "random", "sequential"] as const;
export type SearchMode = (typeof SEARCH_MODES)[number];

export interface WebMCPTextResult {
  content: Array<{ type: "text"; text: string }>;
}

interface ToolDefinition {
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
  execute: (args: unknown) => Promise<WebMCPTextResult>;
}

const text = (t: string): WebMCPTextResult => ({ content: [{ type: "text", text: t }] });

const numberFmt = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });

const fmt = (n: number) => numberFmt.format(n);

/** C# FormatDuration: "D day(s) H hour(s)" at ≥24h, else hh:mm:ss.f. Below a second, plain ms. */
function formatDuration(ms: number): string {
  if (ms < 1000) return `${Math.max(0, Math.round(ms))}ms`;
  const totalHours = ms / 3_600_000;
  if (totalHours >= 24) {
    const days = Math.floor(totalHours / 24);
    const hours = Math.floor(totalHours % 24);
    return `${days} ${days === 1 ? "day" : "days"} ${hours} ${hours === 1 ? "hour" : "hours"}`;
  }
  const hours = Math.floor(ms / 3_600_000);
  const minutes = Math.floor((ms % 3_600_000) / 60_000);
  const seconds = (ms % 60_000) / 1000;
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${pad(hours)}:${pad(minutes)}:${pad(Math.floor(seconds))}.${Math.floor((seconds % 1) * 10)}`;
}

function enumName(enumObject: unknown, value: number): string {
  const name = (enumObject as Record<number, string>)[value];
  return name ?? String(value);
}

// Boot is idempotent and main.tsx boots before render; this is just defense for direct callers.
let booted: Promise<unknown> | null = null;
function ensureBooted(): Promise<unknown> {
  if (!booted) {
    booted =
      bootsharp.getStatus() === bootsharp.BootStatus.Booted
        ? Promise.resolve()
        : bootsharp.boot();
  }
  return booted;
}

/*
 * motely-wasm hands exceptions over without their message; the engine keeps the line-numbered
 * reason in Errors.last() — the exact pattern SeedLab's catch block uses. Errors.last() itself
 * needs a booted runtime, so never let the lookup mask the original failure.
 */
function engineError(prefix: string, fallback: unknown): string {
  let detail: string;
  try {
    detail = Errors.last() ?? (fallback instanceof Error ? fallback.message : String(fallback));
  } catch {
    detail = fallback instanceof Error ? fallback.message : String(fallback);
  }
  return `Error: ${prefix}${detail}`;
}

/**
 * Load a filter's JAML text into a config, or return a corrective error string. Shared by both
 * tools: seed_validate surfaces it directly; seed_search fails the same way before burning a run.
 */
async function loadConfig(filter: string): Promise<{ config?: JamlConfig; error?: string }> {
  if (!filter || !filter.trim()) {
    return { error: "Error: filter is empty — paste the filter's JAML (YAML) text." };
  }
  try {
    await ensureBooted();
    const config = JamlConfigLoader.fromJaml(filter);
    // Mirrors the C# validate's "the search plan builds": createSettings is the plan.
    JamlSearchBuilder.createSettings(config);
    return { config };
  } catch (e) {
    return { error: engineError("the filter did not load: ", e) };
  }
}

const validateTool: ToolDefinition = {
  name: "seed_validate",
  description: [
    "Validate a JAML seed filter: check that the text loads and builds a search plan.",
    "WHEN: you have filter text (paste it, or take it from this app's filter folder) and want",
    "to confirm it is well-formed before spending a search on it — or to get the line-numbered",
    "reason it is not.",
    "DO-NOT-USE-FOR: finding seeds — that is seed_search's job; validating never runs the engine.",
    "Example: { filter: \"name: F\\ndeck: Red\\nstake: White\\nmust:\\n  - joker: Blueprint\\n    antes: [1]\" }",
    '→ "ok: F (Red White) — 1 must, 0 should, 0 mustNot; the search plan builds."',
    "Cost: negligible — a client-side WASM parse, milliseconds.",
  ].join(" "),
  inputSchema: {
    type: "object",
    properties: {
      filter: {
        type: "string",
        description: "The filter's JAML (YAML) text, e.g. \"name: F\\ndeck: Red\\n…\".",
      },
    },
    required: ["filter"],
    additionalProperties: false,
  },
  async execute(args) {
    const filter = typeof (args as { filter?: unknown })?.filter === "string"
      ? (args as { filter: string }).filter
      : "";
    const { config, error } = await loadConfig(filter);
    if (error || !config) return text(error ?? "Error: the filter did not load.");
    // Same summary shape as the C# server's validate_filter.
    return text(
      `ok: ${config.name ?? "unnamed"} (${enumName(MotelyDeck, config.deck)} ` +
        `${enumName(MotelyStake, config.stake)}) — ${config.must.length} must, ` +
        `${config.should.length} should, ${config.mustNot.length} mustNot; the search plan builds.`,
    );
  },
};

interface SearchArgs {
  filter?: unknown;
  mode?: unknown;
  seeds?: unknown;
  keyword?: unknown;
  randomCount?: unknown;
  collect?: unknown;
}

interface ParsedSearchArgs {
  filter: string;
  mode: SearchMode;
  seeds?: string;
  keyword?: string;
  randomCount?: number;
  collect: number;
}

/** The selector each mode requires, and the corrective text when it is missing or mismatched. */
function validateArgs(raw: SearchArgs): { args?: ParsedSearchArgs; error?: string } {
  const filter = typeof raw.filter === "string" ? raw.filter : "";
  if (!filter.trim()) {
    return { error: "Error: filter is required — paste the filter's JAML (YAML) text." };
  }

  const modeRaw = typeof raw.mode === "string" ? raw.mode.toLowerCase() : "replay";
  const mode = (SEARCH_MODES as readonly string[]).includes(modeRaw) ? (modeRaw as SearchMode) : null;
  if (!mode) {
    return {
      error:
        `Error: unknown mode '${String(raw.mode)}'. Use replay, seeds, keyword, random, or sequential.`,
    };
  }

  const seeds = typeof raw.seeds === "string" && raw.seeds.trim() ? raw.seeds.trim() : undefined;
  const keyword = typeof raw.keyword === "string" && raw.keyword.trim() ? raw.keyword.trim() : undefined;
  const randomCount =
    typeof raw.randomCount === "number" && Number.isFinite(raw.randomCount)
      ? Math.floor(raw.randomCount)
      : undefined;

  if (raw.randomCount !== undefined && (randomCount === undefined || randomCount < 1)) {
    return { error: "Error: randomCount must be an integer ≥ 1 — how many random seeds to try." };
  }

  // Exactly one selector, and it must match the mode. Errors teach the fix.
  const selectors = [seeds !== undefined, keyword !== undefined, randomCount !== undefined];
  const given = selectors.filter(Boolean).length;
  if (given > 1) {
    const names = [
      seeds !== undefined ? "seeds" : null,
      keyword !== undefined ? "keyword" : null,
      randomCount !== undefined ? "randomCount" : null,
    ].filter(Boolean).join(" and ");
    return {
      error:
        `Error: pass exactly one of seeds, keyword or randomCount (you gave ${names}). ` +
        "Set mode to match: seeds → mode \"seeds\", keyword → mode \"keyword\", randomCount → mode \"random\".",
    };
  }
  const expected: Record<SearchMode, "seeds" | "keyword" | "randomCount" | null> = {
    replay: null,
    seeds: "seeds",
    keyword: "keyword",
    random: "randomCount",
    sequential: null,
  };
  const want = expected[mode];
  const got = seeds !== undefined ? "seeds" : keyword !== undefined ? "keyword" : randomCount !== undefined ? "randomCount" : null;
  if (want !== got) {
    const hint: Record<string, string> = {
      "replay+selector":
        "mode \"replay\" replays the filter's own `seeds:` block and takes no selector — remove seeds/keyword/randomCount, or change mode to match the selector you passed.",
      "seeds-missing": "mode \"seeds\" searches exactly the seeds you list — pass seeds \"A1B2C3D4,E5F6G7H8\".",
      "keyword-missing": "mode \"keyword\" searches seeds containing a word — pass keyword \"COLA\".",
      "random-missing": "mode \"random\" tries random seeds — pass randomCount (integer ≥ 1), e.g. 1000.",
      "sequential+selector":
        "mode \"sequential\" sweeps the seed space from the start and takes no selector — remove seeds/keyword/randomCount, or change mode to match the selector you passed.",
    };
    const key = want === null ? `${mode}+selector` : `${mode}-missing`;
    return { error: `Error: ${hint[key]}` };
  }

  let collect = typeof raw.collect === "number" && Number.isFinite(raw.collect)
    ? Math.floor(raw.collect)
    : DEFAULT_COLLECT;
  if (raw.collect !== undefined && (collect < 1 || collect > MAX_COLLECT)) {
    return { error: `Error: collect must be an integer between 1 and ${MAX_COLLECT}.` };
  }
  collect = Math.min(Math.max(collect, 1), MAX_COLLECT);

  return {
    args: {
      filter,
      mode,
      collect,
      ...(seeds !== undefined ? { seeds } : {}),
      ...(keyword !== undefined ? { keyword } : {}),
      ...(randomCount !== undefined ? { randomCount } : {}),
    },
  };
}

const searchTool: ToolDefinition = {
  name: "seed_search",
  description: [
    "Search Balatro seeds against a JAML filter and return the matching seeds, entirely",
    "client-side in WASM (no server, no network).",
    "WHEN: the filter text is valid (run seed_validate first if unsure) and you want seeds",
    "that satisfy it.",
    "DO-NOT-USE-FOR: merely checking that a filter loads — use seed_validate; it is cheaper and",
    "returns the line-numbered reason on failure.",
    "Modes: replay (default; the filter's own `seeds:` block), seeds (comma-separated list in",
    "`seeds`), keyword (seeds containing `keyword`), random (`randomCount` random seeds),",
    "sequential (sweep from the start of the ~2.3-trillion-seed space — prefer random or keyword",
    "probes unless you mean an exhaustive sweep). Exactly one selector must match `mode`.",
    'Example: { filter: "…", mode: "random", randomCount: 1000, collect: 10 } → "F: 3 matched /',
    '1000 searched in 12ms (83,000 seeds/s).\\nMatches: A1B2C3D4, …" — collect caps matches kept',
    "(default 100, max 10000).",
    "Cost: WASM speed, tens of thousands to millions of seeds per second in the browser;",
    "sequential on a loose filter can run effectively forever — keep collect modest.",
  ].join(" "),
  inputSchema: {
    type: "object",
    properties: {
      filter: {
        type: "string",
        description: "The filter's JAML (YAML) text, e.g. \"name: F\\ndeck: Red\\n…\".",
      },
      mode: {
        type: "string",
        enum: [...SEARCH_MODES],
        default: "replay",
        description: "Which seeds to try: replay (default), seeds, keyword, random, sequential.",
      },
      seeds: {
        type: "string",
        description: 'Comma-separated seeds for mode "seeds", e.g. "A1B2C3D4,E5F6G7H8".',
      },
      keyword: { type: "string", description: 'Seed word for mode "keyword", e.g. "COLA".' },
      randomCount: {
        type: "integer",
        minimum: 1,
        description: 'How many random seeds for mode "random".',
      },
      collect: {
        type: "integer",
        minimum: 1,
        maximum: MAX_COLLECT,
        default: DEFAULT_COLLECT,
        description: "Stop after this many matches (default 100, max 10000).",
      },
    },
    required: ["filter"],
    additionalProperties: false,
  },
  async execute(rawArgs) {
    const { args, error } = validateArgs((rawArgs ?? {}) as SearchArgs);
    if (error || !args) return text(error ?? "Error: bad arguments.");

    const { config, error: loadError } = await loadConfig(args.filter);
    if (loadError || !config) return text(loadError ?? "Error: the filter did not load.");

    // The C# server reports the filter argument in the summary; on the web the argument is the
    // filter's full text, so the config's name identifies the run instead.
    const label = config.name ?? "unnamed";

    const matches: string[] = [];
    try {
      await ensureBooted();
      let settings = JamlSearchBuilder.createSettings(config)
        .withThreadCount(THREAD_COUNT)
        .withQuietMode(true);

      switch (args.mode) {
        case "replay":
          settings = settings.withSeedList(config.seeds);
          break;
        case "seeds":
          settings = settings.withSeedList(
            args.seeds!.split(",").map((s) => s.trim()).filter(Boolean),
          );
          break;
        case "keyword":
          settings = settings.withKeywordSearch([args.keyword!]);
          break;
        case "random":
          settings = settings.withRandomSearch(args.randomCount!);
          break;
        case "sequential":
          settings = settings.withSequentialSearch();
          break;
      }

      // collect caps kept matches, mirroring the C# runner's StopAfter. On replay/seed lists the
      // run still finishes the whole (finite) list when matches stay under the cap.
      settings = settings.stopAfter(BigInt(args.collect));

      // A scored filter reports every find on the scored channel (and the bare seed on the match
      // channel too): listen to exactly one, or each hit lands twice — SeedLab's rule.
      if (settings.seedScoreDesc != null)
        settings = settings.withScoredResultCallback((r) => {
          matches.push(r.seed);
        });
      else
        settings = settings.withSeedMatchCallback((seed: string) => {
          matches.push(seed);
        });

      const search = settings.start(new CancellationToken());
      try {
        await search.waitForCompletionAsync();
      } finally {
        search.dispose();
      }

      // Same summary shape as the C# server's search tool (without its server-side "by <user>").
      const summary =
        `${label}: ${fmt(Number(search.matchingSeeds))} matched / ` +
        `${fmt(Number(search.totalSeedsSearched))} searched in ` +
        `${formatDuration(Number(search.elapsedMs))} (${fmt(Math.round(search.seedsPerSecond))} seeds/s).`;
      return text(
        matches.length > 0 ? `${summary}\nMatches: ${matches.join(", ")}` : `${summary}\nNo seeds matched.`,
      );
    } catch (e) {
      return text(engineError("the search failed: ", e));
    }
  },
};

/** The two registered tools, in registration order. */
export const seedLabTools: readonly ToolDefinition[] = [validateTool, searchTool];
