// CancellationToken from the root entry on purpose: that import used to resolve to nothing
// (two `export *` clauses claimed the name). scripts/patch-dist-cancellation-export.mjs pins it.
import bootsharp, { Search, Analyze, Jaml, JamlFiles, CancellationToken } from "../bin/motely-wasm/index.mjs";

// MotelyIndividualSeedSearcher — bind BEFORE boot.
// ctx is the live MotelySingleSearchContext object (specialization), not a string.
const jimmolateSeen = [];
Search.jimmolate = (ctx) => {
  jimmolateSeen.push({
    seed: ctx.getSeed(),
    voucher: ctx.getAnteFirstVoucher(1),
    boss: ctx.getBossForAnte(1),
  });
  return 1;
};

await bootsharp.boot();
globalThis.motely = { Search, Analyze, Jaml, JamlFiles, CancellationToken, jimmolateSeen };
globalThis.dispatchEvent(new CustomEvent("motely-ready"));
