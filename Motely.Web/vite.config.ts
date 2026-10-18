import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// motely-wasm is linked from ../Motely.Wasm (a `file:` dependency), outside this folder, so the
// dev server has to be allowed to serve it. Aspire hands the port in through PORT.
export default defineConfig({
  plugins: [react()],
  server: {
    fs: { allow: [".."] },
    port: Number(process.env.PORT) || 5173,
    strictPort: true,
  },
  build: {
    target: "esnext",
    // .NET's JavaScript probes for Node modules it never uses in a browser.
    rollupOptions: { external: ["process", "module"] },
  },
});
