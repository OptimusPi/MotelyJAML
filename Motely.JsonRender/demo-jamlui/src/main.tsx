import React from "react";
import { createRoot } from "react-dom/client";
import { JamlyzerView } from "jaml-ui";
import { MotelyDeck, MotelyStake } from "motely-wasm";
import type { MotelyJamlyzerSeedResult } from "motely-wasm";
import "jaml-ui/jimbo.css";

import report from "../fixtures/always-pass.jamlui.json";

const seeds = report.seeds as unknown as MotelyJamlyzerSeedResult[];

function splitCamelCase(key: string): string {
  return key.replace(/([A-Z])/g, " $1").trim();
}

function App() {
  const deckName = splitCamelCase(MotelyDeck[report.deck] ?? `Deck ${report.deck}`);
  const stakeName = splitCamelCase(MotelyStake[report.stake] ?? `Stake ${report.stake}`);
  return (
    <div
      style={{
        minHeight: "100vh",
        background: "var(--j-dark-bg, #2a2c3f)",
        color: "var(--j-white, #fff)",
        padding: "24px 32px 64px",
        fontFamily: "var(--j-font-body, m6x11, monospace)",
        display: "flex",
        flexDirection: "column",
        gap: "24px",
      }}
    >
      <header>
        <h1 className="j-text j-text--heading j-text--gold" style={{ margin: 0 }}>
          {report.filter.name ?? report.filter.id}
        </h1>
        <p className="j-text j-text--body" style={{ margin: "8px 0 0", opacity: 0.85 }}>
          {deckName} deck · {stakeName} stake · {seeds.length} seed
          {seeds.length === 1 ? "" : "s"} · {report.eventRolls} event rolls — rendered by
          jaml-ui's <code>JamlyzerView</code> from a Motely.JsonRender <code>--jamlui</code> file.
        </p>
      </header>

      {seeds.map((seed) => (
        <section key={seed.seed}>
          <JamlyzerView result={seed} deck={report.deck} stake={report.stake} />
        </section>
      ))}

      <footer style={{ marginTop: "32px", opacity: 0.6, fontSize: "13px" }}>
        Pixel fonts by Daniel Linssen · sprites from Balatro (LocalThunk) · jaml-ui by pifreak
      </footer>
    </div>
  );
}

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
