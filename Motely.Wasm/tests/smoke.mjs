import { spawn } from "node:child_process";
import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { chromium } from "playwright-core";

const root = process.argv[2] ?? join(import.meta.dirname, "..", "..");
const port = 8123;

function findChromium() {
  if (process.env.CHROMIUM_PATH && existsSync(process.env.CHROMIUM_PATH))
    return process.env.CHROMIUM_PATH;
  return undefined;
}

const server = spawn(process.execPath, [join(import.meta.dirname, "serve.mjs"), root, String(port)], {
  stdio: "inherit",
});

try {
  const browser = await chromium.launch({
    executablePath: findChromium(),
    args: ["--no-sandbox"],
  });
  const page = await browser.newPage();
  page.on("console", (msg) => console.log("[page]", msg.text()));
  await page.goto(`http://127.0.0.1:${port}/Motely.Wasm/host/index.html`);
  await page.waitForFunction(() => document.title.startsWith("MOTELY-WASM"), null, {
    timeout: 120_000,
  });
  const title = await page.title();
  const body = await page.locator("#out").textContent();
  console.log(body);
  await browser.close();
  // exitCode, not exit(): exit() here skips the finally and orphans the server, which keeps
  // the port and any pipe on this process's stdout open.
  if (title !== "MOTELY-WASM-OK") {
    console.error(`SMOKE FAIL: title=${title}`);
    process.exitCode = 1;
  } else console.log("SMOKE PASS");
} finally {
  server.kill();
}
