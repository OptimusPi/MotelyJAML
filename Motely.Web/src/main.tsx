import { createRoot } from "react-dom/client";
import * as fs from "@rewaffle/bootsharp-file-system";
import { IFileMounter } from "motely-wasm/bootsharp/file-system";
import "jaml-ui/jimbo.css";
import { App } from "./App.js";

// Folder access binds before the engine boots (SeedLab boots it on the first search). A browser
// without the File System Access API still runs everything but the filters folder.
const folders = fs.isFileSystemAvailable();
if (folders) fs.init(IFileMounter);

createRoot(document.getElementById("root")!).render(<App folders={folders} />);
