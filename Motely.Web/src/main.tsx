import { createRoot } from "react-dom/client";
import * as fs from "@rewaffle/bootsharp-file-system";
import bootsharp from "motely-wasm";
import { IFileMounter } from "motely-wasm/bootsharp/file-system";
import "jaml-ui/jimbo.css";
import { App } from "./App.js";

// Folder access binds before boot; boot comes before render because FilterFolder reads YAMLFiles.
const folders = fs.isFileSystemAvailable();
if (folders) fs.init(IFileMounter);
await bootsharp.boot();

createRoot(document.getElementById("root")!).render(<App folders={folders} />);
