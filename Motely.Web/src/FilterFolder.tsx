import { useSyncExternalStore, useCallback } from "react";
import { JamlFiles } from "motely-wasm";
import { JimboButton, JimboPanel, JimboRow, JimboStack, JimboBadge } from "jaml-ui";

export type OpenedFilter = { name: string; jaml: string };

export interface FilterFolderProps {
  opened: OpenedFilter | null;
  onOpen: (filter: OpenedFilter | null) => void;
}

let version = 0;
JamlFiles.onChange.subscribe(() => version++);

function subscribeNames(cb: () => void) {
  const sub = () => { version++; cb(); };
  JamlFiles.onChange.subscribe(sub);
  return () => JamlFiles.onChange.unsubscribe(sub);
}

function getNames(): readonly string[] {
  return JamlFiles.list();
}

export function FilterFolder({ opened, onOpen }: FilterFolderProps) {
  const names = useSyncExternalStore(subscribeNames, getNames);
  const mounted = JamlFiles.isMounted();

  const pick = useCallback(async () => {
    await JamlFiles.pickFolder();
  }, []);

  const open = useCallback(async (name: string) => {
    const jaml = await JamlFiles.load(name);
    onOpen({ name, jaml });
  }, [onOpen]);

  if (!JamlFiles.isSupported()) return null;

  return (
    <JimboPanel title="Filters">
      <JimboStack gap="sm">
        <JimboRow gap="sm" align="center">
          <JimboButton size="sm" tone="blue" onClick={pick}>
            {mounted ? "Change folder" : "Open folder"}
          </JimboButton>
          {mounted && (
            <JimboBadge size="sm" tone="grey">
              {names.length} filter{names.length !== 1 ? "s" : ""}
            </JimboBadge>
          )}
        </JimboRow>
        {names.map((name) => (
          <JimboButton
            key={name}
            size="sm"
            tone={opened?.name === name ? "orange" : "grey"}
            onClick={() => open(name)}
          >
            {name}
          </JimboButton>
        ))}
      </JimboStack>
    </JimboPanel>
  );
}
