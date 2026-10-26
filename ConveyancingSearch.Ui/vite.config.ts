import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
export default defineConfig({
  plugins: [react()],
  server: { proxy: { "/api": "http://127.0.0.1:5080" } },
  // The API serves the production build from its own wwwroot.
  build: { outDir: "../ConveyancingSearch.Api/wwwroot", emptyOutDir: true },
  test: {
    environment: "jsdom",
    exclude: ["browser-tests/**", "node_modules/**"],
    setupFiles: ["./src/test-setup.ts"],
  },
});
