import { readFileSync } from "node:fs";

const EXPECTED = {
  root: ["filter", "deck", "stake", "eventRolls", "seeds"],
  filter: ["id", "name"],
  seed: ["seed", "score", "antes", "events", "streamStates"],
  seedOptional: ["erraticDeck"],
  ante: [
    "ante",
    "boss",
    "voucher",
    "smallBlindTag",
    "bigBlindTag",
    "shopItems",
    "packs",
    "pulls",
    "shopStreams",
  ],
  pack: ["pack", "items"],
  pulls: [
    "judgementJokers",
    "wraithJokers",
    "emperorTarots",
    "purpleSealTarots",
    "sixthSenseSpectrals",
    "seanceSpectrals",
    "riffRaffJokers",
    "rareTagJokers",
    "uncommonTagJokers",
    "legendaryJokers",
    "voucherSequence",
  ],
  shopStreams: [
    "shopJokers",
    "commonShopJokers",
    "uncommonShopJokers",
    "rareShopJokers",
    "shopTarots",
    "shopPlanets",
    "shopSpectrals",
  ],
  events: [
    "luckyMoney",
    "luckyMult",
    "wheelOfFortune",
    "cavendish",
    "grosMichel",
    "space",
    "business",
    "bloodstone",
    "parking",
    "eightBall",
    "glass",
    "omenGlobe",
    "theWheel",
    "misprint",
  ],
  streamStates: [
    "rollOffset",
    "luckyMoney",
    "luckyMult",
    "wheelOfFortune",
    "cavendish",
    "grosMichel",
    "space",
    "business",
    "bloodstone",
    "parking",
    "eightBall",
    "glass",
    "omenGlobe",
    "theWheel",
    "misprint",
  ],
  item: [
    "value",
    "type",
    "typeCategory",
    "seal",
    "enhancement",
    "edition",
    "standardcardSuit",
    "standardcardRank",
    "isPerishable",
    "isEternal",
    "isRental",
  ],
};

let failures = 0;
let checks = 0;

function checkKeys(label, obj, expected, optional = []) {
  checks++;
  const actual = Object.keys(obj);
  const missing = expected.filter((k) => !(k in obj));
  const extra = actual.filter((k) => !expected.includes(k) && !optional.includes(k));
  if (missing.length || extra.length) {
    failures++;
    console.error(
      `MISMATCH at ${label}:` +
        (missing.length ? ` missing=[${missing}]` : "") +
        (extra.length ? ` extra=[${extra}]` : ""),
    );
  }
}

function checkType(label, cond, want) {
  checks++;
  if (!cond) {
    failures++;
    console.error(`TYPE at ${label}: expected ${want}`);
  }
}

const isNum = (v) => typeof v === "number";
const isBool = (v) => typeof v === "boolean";
const isStr = (v) => typeof v === "string";
const isArr = Array.isArray;

function checkItem(label, item) {
  checkKeys(label, item, EXPECTED.item);
  checkType(`${label}.value`, isNum(item.value), "number");
  for (const k of ["type", "typeCategory", "seal", "enhancement", "edition", "standardcardSuit", "standardcardRank"])
    checkType(`${label}.${k}`, isNum(item[k]), "number (numeric enum)");
  for (const k of ["isPerishable", "isEternal", "isRental"])
    checkType(`${label}.${k}`, isBool(item[k]), "boolean");
}

const path = process.argv[2];
if (!path) {
  console.error("Usage: node validate-jamlui.mjs <path-to-jamlui.json>");
  process.exit(2);
}
const report = JSON.parse(readFileSync(path, "utf8"));

checkKeys("root", report, EXPECTED.root);
checkKeys("root.filter", report.filter, EXPECTED.filter);
checkType("root.deck", isNum(report.deck), "number");
checkType("root.stake", isNum(report.stake), "number");
checkType("root.eventRolls", isNum(report.eventRolls), "number");
checkType("root.seeds", isArr(report.seeds), "array");

report.seeds.forEach((seed, si) => {
  const s = `seeds[${si}](${seed.seed ?? "?"})`;
  checkKeys(s, seed, EXPECTED.seed, EXPECTED.seedOptional);
  checkType(`${s}.seed`, isStr(seed.seed), "string");
  checkType(`${s}.score`, isNum(seed.score), "number");
  if (seed.erraticDeck !== undefined)
    seed.erraticDeck.forEach((it, i) => checkItem(`${s}.erraticDeck[${i}]`, it));

  checkKeys(`${s}.events`, seed.events, EXPECTED.events);
  for (const [k, v] of Object.entries(seed.events)) {
    checkType(`${s}.events.${k}`, isArr(v), "array");
    const numeric = k === "wheelOfFortune" || k === "misprint";
    v.forEach((e, i) =>
      checkType(`${s}.events.${k}[${i}]`, numeric ? isNum(e) : isBool(e), numeric ? "number" : "boolean"),
    );
  }

  checkKeys(`${s}.streamStates`, seed.streamStates, EXPECTED.streamStates);
  for (const [k, v] of Object.entries(seed.streamStates))
    checkType(`${s}.streamStates.${k}`, isNum(v), "number");

  seed.antes.forEach((ante, ai) => {
    const a = `${s}.antes[${ai}]`;
    checkKeys(a, ante, EXPECTED.ante);
    for (const k of ["ante", "boss", "voucher", "smallBlindTag", "bigBlindTag"])
      checkType(`${a}.${k}`, isNum(ante[k]), "number");
    ante.shopItems.forEach((it, i) => checkItem(`${a}.shopItems[${i}]`, it));
    ante.packs.forEach((pack, pi) => {
      checkKeys(`${a}.packs[${pi}]`, pack, EXPECTED.pack);
      checkType(`${a}.packs[${pi}].pack`, isNum(pack.pack), "number");
      pack.items.forEach((it, i) => checkItem(`${a}.packs[${pi}].items[${i}]`, it));
    });

    checkKeys(`${a}.pulls`, ante.pulls, EXPECTED.pulls);
    for (const [k, v] of Object.entries(ante.pulls)) {
      checkType(`${a}.pulls.${k}`, isArr(v), "array");
      if (k === "voucherSequence")
        v.forEach((e, i) => checkType(`${a}.pulls.${k}[${i}]`, isNum(e), "number (numeric enum)"));
      else v.forEach((it, i) => checkItem(`${a}.pulls.${k}[${i}]`, it));
    }

    checkKeys(`${a}.shopStreams`, ante.shopStreams, EXPECTED.shopStreams);
    for (const [k, v] of Object.entries(ante.shopStreams)) {
      checkType(`${a}.shopStreams.${k}`, isArr(v), "array");
      v.forEach((it, i) => checkItem(`${a}.shopStreams.${k}[${i}]`, it));
    }
  });
});

if (failures) {
  console.error(`\nFAILED: ${failures} contract violation(s) across ${checks} checks.`);
  process.exit(1);
}
console.log(
  `OK: ${report.seeds.length} seed(s), ${checks} checks — every field matches the jaml-ui contract.`,
);
