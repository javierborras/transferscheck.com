import { defineConfig, devices } from "@playwright/test";

// Read-only checks against the published site: from web/, `npx playwright test -c playwright.prod.config.ts`.
export default defineConfig({
  testDir: "./e2e-prod",
  timeout: 60_000,
  reporter: [["list"]],
  use: { ...devices["Desktop Chrome"], screenshot: "only-on-failure" }
});
