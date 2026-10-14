// YAMLFiles over the real @rewaffle/bootsharp-file-system package, mounted on in-memory
// directory handles: the same code path the site runs, with the browser's picker and observer
// stood in by handles.mjs. Usage: npm test   (after `dotnet publish -c Release`)
import test from "node:test";
import assert from "node:assert/strict";
import * as fs from "@rewaffle/bootsharp-file-system";
import bootsharp, { YAMLFiles, Errors } from "../bin/motely-wasm/index.mjs";
import { IFileMounter } from "../bin/motely-wasm/generated/modules/bootsharp/file-system.g.mjs";
import { DirMock, FileMock, installBrowserGlobals } from "./handles.mjs";

const folder = new DirMock("filters", [
  new FileMock("perkeo.yaml", "name: perkeo\n"),
  new DirMock("sub", [new FileMock("cola.yml", "name: cola\n")]),
  new FileMock("notes.txt", "not a filter"),
]);
let picked = () => folder;
installBrowserGlobals(() => picked());

// The mounter must be bound before boot; debounce 0 so each change reports at once.
fs.init(IFileMounter, 0);
await bootsharp.boot();

const changes = [];
YAMLFiles.onChange.subscribe((change) => changes.push(change));

test("this build has folder access and starts with nothing mounted", () => {
  assert.equal(YAMLFiles.isSupported(), true);
  assert.equal(YAMLFiles.isMounted(), false);
  assert.deepEqual([...YAMLFiles.list()], []);
});

test("a cancelled picker is false, not an error", async () => {
  picked = () => {
    throw new Error("cancelled");
  };
  assert.equal(await YAMLFiles.pickFolder(), false);
  assert.equal(YAMLFiles.isMounted(), false);
  picked = () => folder;
});

test("picking a folder lists its filters, yaml extension dropped, other files left out", async () => {
  assert.equal(await YAMLFiles.pickFolder(), true);
  assert.equal(YAMLFiles.isMounted(), true);
  assert.deepEqual([...YAMLFiles.list()], ["perkeo", "sub/cola.yml"]);
  assert.deepEqual(
    changes.map((c) => [c.kind, c.name]),
    [["added", "perkeo"], ["added", "sub/cola.yml"]],
  );
});

test("a filter loads by name", async () => {
  assert.equal(await YAMLFiles.load("perkeo"), "name: perkeo\n");
  assert.equal(await YAMLFiles.load("sub/cola.yml"), "name: cola\n");
});

test("saving creates or rewrites, and the list and events follow", async () => {
  changes.length = 0;
  await YAMLFiles.save("new", "name: new\n");
  assert.equal(await YAMLFiles.load("new"), "name: new\n");
  assert.deepEqual([...YAMLFiles.list()], ["new", "perkeo", "sub/cola.yml"]);

  await YAMLFiles.save("perkeo", "name: perkeo\nseeds: [ALEEB]\n");
  assert.equal(await YAMLFiles.load("perkeo"), "name: perkeo\nseeds: [ALEEB]\n");
  assert.deepEqual(
    changes.map((c) => [c.kind, c.name]),
    [["added", "new"], ["modified", "perkeo"]],
  );
});

test("renaming moves the file and reports where it came from", async () => {
  changes.length = 0;
  await YAMLFiles.rename("new", "sub/new");
  assert.deepEqual([...YAMLFiles.list()], ["perkeo", "sub/cola.yml", "sub/new"]);
  assert.equal(await YAMLFiles.load("sub/new"), "name: new\n");
  assert.deepEqual(changes, [{ kind: "moved", name: "sub/new", fromName: "new" }]);
});

test("removing deletes the file", async () => {
  changes.length = 0;
  await YAMLFiles.remove("sub/new");
  assert.deepEqual([...YAMLFiles.list()], ["perkeo", "sub/cola.yml"]);
  assert.deepEqual(changes, [{ kind: "removed", name: "sub/new" }]);
});

test("a file call that fails says why on Errors.last()", async () => {
  Errors.last();
  await assert.rejects(YAMLFiles.load("missing"));
  assert.match(Errors.last() ?? "", /missing/);
});

test("unmounting clears the list, and file calls then say no folder is mounted", async () => {
  await YAMLFiles.unmount();
  assert.equal(YAMLFiles.isMounted(), false);
  assert.deepEqual([...YAMLFiles.list()], []);
  Errors.last();
  await assert.rejects(YAMLFiles.load("perkeo"));
  assert.match(Errors.last() ?? "", /No folder is mounted/);
});
