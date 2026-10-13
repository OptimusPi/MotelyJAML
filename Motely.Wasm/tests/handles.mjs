// In-memory stand-ins for the browser's FileSystemDirectoryHandle and FileSystemFileHandle: just
// the surface @rewaffle/bootsharp-file-system touches, holding real bytes so a file written
// through the mounted folder reads back.

const encoder = new TextEncoder();

export class FileMock {
  kind = "file";

  constructor(name, text = "") {
    this.name = name;
    this.bytes = encoder.encode(text);
  }

  async getFile() {
    const bytes = this.bytes;
    return {
      type: "application/yaml",
      size: bytes.length,
      lastModified: 0,
      arrayBuffer: async () => bytes.slice().buffer,
    };
  }

  async createWritable() {
    const chunks = [];
    return {
      write: async (chunk) => chunks.push(chunk),
      close: async () => {
        this.bytes = new Uint8Array(chunks.reduce((n, c) => n + c.length, 0));
        let at = 0;
        for (const chunk of chunks) {
          this.bytes.set(chunk, at);
          at += chunk.length;
        }
      },
    };
  }

  async queryPermission() {
    return "granted";
  }

  async requestPermission() {
    return "granted";
  }

  async isSameEntry(other) {
    return other === this;
  }
}

export class DirMock {
  kind = "directory";

  constructor(name, entries = []) {
    this.name = name;
    this.entries = new Map(entries.map((e) => [e.name, e]));
  }

  async *[Symbol.asyncIterator]() {
    yield* this.entries;
  }

  async getFileHandle(name, { create = false } = {}) {
    let handle = this.entries.get(name);
    if (!handle && create) this.entries.set(name, (handle = new FileMock(name)));
    if (!handle || handle.kind !== "file") throw new Error(`no file ${name}`);
    return handle;
  }

  async getDirectoryHandle(name, { create = false } = {}) {
    let handle = this.entries.get(name);
    if (!handle && create) this.entries.set(name, (handle = new DirMock(name)));
    if (!handle || handle.kind !== "directory") throw new Error(`no directory ${name}`);
    return handle;
  }

  async removeEntry(name) {
    if (!this.entries.delete(name)) throw new Error(`no entry ${name}`);
  }

  /** The path from this directory down to the handle, or null when it is not inside. */
  async resolve(handle) {
    for (const [name, entry] of this.entries) {
      if (entry === handle) return [name];
      if (entry.kind === "directory") {
        const below = await entry.resolve(handle);
        if (below) return [name, ...below];
      }
    }
    return null;
  }

  async queryPermission() {
    return "granted";
  }

  async requestPermission() {
    return "granted";
  }

  async isSameEntry(other) {
    return other === this;
  }
}

/** What the file-system package reaches for on `window`, pointed at the given picked folder. */
export function installBrowserGlobals(picked) {
  class FileSystemObserver {
    constructor() {}
    async observe() {}
    disconnect() {}
  }
  globalThis.FileSystemObserver = FileSystemObserver;
  globalThis.window = {
    FileSystemObserver,
    showDirectoryPicker: async () => {
      if (typeof picked === "function") return picked();
      return picked;
    },
  };
}
