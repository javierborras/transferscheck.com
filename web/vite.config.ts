import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, process.cwd(), "VITE_");
  if (command === "build") {
    let origin: URL;
    try { origin = new URL(env.VITE_API_ORIGIN); }
    catch { throw new Error("Production build requires VITE_API_ORIGIN with the HTTPS URL of the real API."); }
    if (origin.protocol !== "https:" || ["localhost", "127.0.0.1", "[::1]"].includes(origin.hostname) || origin.hostname.endsWith(".localhost"))
      throw new Error("Production build cannot point to a local/mock API; VITE_API_ORIGIN must use HTTPS.");
    if (env.VITE_ENABLE_MOCK_SERVICES === "true") throw new Error("Mock services cannot be enabled in a production build.");
  }
  return { plugins: [react()], server: { port: 5183, strictPort: true } };
});
