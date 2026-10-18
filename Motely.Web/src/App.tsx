import { useState } from "react";
import { JimboApp, JimboBackground, JimboBalatroFooter, JimboWordmark, SeedLab, STARTER_JAML } from "jaml-ui";
import { FilterFolder, type OpenedFilter } from "./FilterFolder.js";

export function App({ folders }: { folders: boolean }) {
  const [opened, setOpened] = useState<OpenedFilter | null>(null);

  return (
    <>
      <JimboBackground />
      <JimboApp variant="page" footer={<JimboBalatroFooter style={{ position: "static" }} />}>
        <JimboWordmark title="Motely" sub="Balatro seed search, in your browser, at full speed" />
        {folders ? <FilterFolder opened={opened} onOpen={setOpened} /> : null}
        {/* Opening a filter remounts the lab with that text: a new filter is a new search. */}
        <SeedLab key={opened?.name ?? "starter"} defaultJaml={opened?.yaml ?? STARTER_JAML} />
      </JimboApp>
    </>
  );
}
