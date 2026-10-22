import { useState } from "react";
import { JimboApp, JimboBackground, JimboBalatroFooter, JimboBadge, JimboWordmark, SeedLab, STARTER_JAML } from "jaml-ui";
import { FilterFolder, type OpenedFilter } from "./FilterFolder.js";
import { useSeedLabWebMCP } from "./webmcp.js";

export function App({ folders }: { folders: boolean }) {
  const [opened, setOpened] = useState<OpenedFilter | null>(null);
  // WebMCP tools are registered for as long as the lab is mounted; the badge says an
  // in-page agent (native WebMCP) can drive seed_validate / seed_search directly.
  const agentReady = useSeedLabWebMCP();

  return (
    <>
      <JimboBackground />
      <JimboApp variant="page" footer={<JimboBalatroFooter style={{ position: "static" }} />}>
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
        {folders ? <FilterFolder opened={opened} onOpen={setOpened} /> : null}
        {/* Opening a filter remounts the lab with that text: a new filter is a new search. */}
        <SeedLab key={opened?.name ?? "starter"} defaultJaml={opened?.yaml ?? STARTER_JAML} />
      </JimboApp>
    </>
  );
}
