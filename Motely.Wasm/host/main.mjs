import bootsharp, { Search, Analyze } from "../bin/motely-wasm/index.mjs";
import { CancellationToken } from "../bin/motely-wasm/bcl/index.mjs";

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
globalThis.motely = { Search, Analyze, CancellationToken, jimmolateSeen };
globalThis.dispatchEvent(new CustomEvent("motely-ready"));
