import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// motely-wasm is linked from ../Motely.Wasm (a `file:` dependency), outside this folder, so the
// dev server has to be allowed to serve it. Aspire hands the port in through PORT.
//
// /agui is proxied to the MCP server so the agent panel calls same-origin and CORS never comes
// into it. Under Aspire the server's URL arrives as service discovery config; standalone it is
// the launch profile's port. MOTELY_MCP_URL overrides both.
const mcpUrl =
  process.env.services__mcp__http__0 ?? process.env.MOTELY_MCP_URL ?? "http://localhost:5200";

export default defineConfig({
  plugins: [react()],
  server: {
    fs: { allow: [".."] },
    port: Number(process.env.PORT) || 5173,
    strictPort: true,
    proxy: { "/agui": { target: mcpUrl } },
  },
  build: {
    target: "esnext",
    // .NET's JavaScript probes for Node modules it never uses in a browser.
    rollupOptions: { external: ["process", "module"] },
  },
});
