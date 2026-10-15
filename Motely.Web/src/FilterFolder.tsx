import { useSyncExternalStore, useCallback } from "react";
import { YAMLFiles } from "motely-wasm";
import { JimboButton, JimboPanel, JimboRow, JimboStack, JimboBadge } from "jaml-ui";

export type OpenedFilter = { name: string; yaml: string };

export interface FilterFolderProps {
  opened: OpenedFilter | null;
  onOpen: (filter: OpenedFilter | null) => void;
}

// list() is a new array per call; useSyncExternalStore needs a stable snapshot.
let snapshot: readonly string[] | null = null;

function subscribeNames(cb: () => void) {
  const sub = () => { snapshot = null; cb(); };
  YAMLFiles.onChange.subscribe(sub);
  return () => YAMLFiles.onChange.unsubscribe(sub);
}

function getNames(): readonly string[] {
  return (snapshot ??= YAMLFiles.list());
}

export function FilterFolder({ opened, onOpen }: FilterFolderProps) {
  const names = useSyncExternalStore(subscribeNames, getNames);
  const mounted = YAMLFiles.isMounted();

  const open = useCallback(async (name: string) => {
    const yaml = await YAMLFiles.load(name);
    onOpen({ name, yaml });
  }, [onOpen]);

  if (!YAMLFiles.isSupported()) return null;

  return (
    <JimboPanel title="Filters">
      <JimboStack gap="sm">
        <JimboRow gap="sm" align="center">
          <JimboButton size="sm" tone="blue" onClick={() => YAMLFiles.pickFolder()}>
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
