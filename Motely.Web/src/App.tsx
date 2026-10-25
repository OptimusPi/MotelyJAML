import { useState } from "react";
import {
  JimboApp,
  JimboBackground,
  JimboBalatroFooter,
  JimboBadge,
  JimboTabs,
  JimboWordmark,
  SeedLab,
  STARTER_JAML,
} from "jaml-ui";
import { AgentPanel } from "./AgentPanel.js";
import { FilterFolder, type OpenedFilter } from "./FilterFolder.js";
import { useSeedLabWebMCP } from "./webmcp.js";

export function App({ folders }: { folders: boolean }) {
  const [opened, setOpened] = useState<OpenedFilter | null>(null);
  const [tab, setTab] = useState<"lab" | "agent">("lab");
  // WebMCP tools are registered for as long as the lab is mounted; the badge says an
  // in-page agent (native WebMCP) can drive seed_validate / seed_search directly.
  const agentReady = useSeedLabWebMCP();

  return (
    <div style={{ minHeight: "100dvh", display: "flex", flexDirection: "column", paddingBottom: 48 }}>
      <JimboBackground />
      <JimboApp variant="page" style={{ flex: 1 }}>
        <div style={{ display: "flex", alignItems: "center", gap: "0.6rem", flexWrap: "wrap" }}>
          <JimboWordmark title="Motely" sub="Balatro seed search, in your browser, at full speed" />
          {agentReady ? (
            <JimboBadge
              size="sm"
              tone="grey"
              title="WebMCP ready: this page exposes seed_validate and seed_search to in-page agents"
            >
              🃏 agent-ready
            </JimboBadge>
          ) : null}
        </div>
        <JimboTabs
          tabs={[
            { id: "lab", label: "Seed Lab" },
            { id: "agent", label: "Server Agent" },
          ]}
          activeTab={tab}
          onTabChange={(id) => setTab(id === "agent" ? "agent" : "lab")}
        />
        {tab === "agent" ? (
          <AgentPanel />
        ) : (
          <>
            {folders ? <FilterFolder opened={opened} onOpen={setOpened} /> : null}
            {/* Opening a filter remounts the lab with that text: a new filter is a new search. */}
            <SeedLab key={opened?.name ?? "starter"} defaultJaml={opened?.yaml ?? STARTER_JAML} />
          </>
        )}
      </JimboApp>
      {/* Pinned to the viewport bottom, like Balatro's page chrome — content scrolls under
          the 48px of padding above, so nothing ever hides behind it. */}
      <JimboBalatroFooter style={{ position: "fixed", bottom: 0, left: 0, right: 0, marginTop: 0 }} />
    </div>
  );
}
