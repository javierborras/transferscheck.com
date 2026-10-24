import { defineConfig, devices } from "@playwright/test";

// Browser e2e: from web/, run `npm run e2e`.
// Starts API via e2e/start-api.ps1 (http://localhost:5080 + mock :5099) and Vite (`npm run dev` on :5183).
// Report: web/playwright-report/  Screenshots on failure: web/test-results/

export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  use: {
    ...devices["Desktop Chrome"],
    baseURL: "http://localhost:5183",
    screenshot: "only-on-failure",
    trace: "retain-on-failure"
  },
  webServer: [
    {
      command: "powershell -NoProfile -ExecutionPolicy Bypass -File e2e/start-api.ps1",
      url: "http://localhost:5080/swagger/v1/swagger.json",
      timeout: 180_000,
      reuseExistingServer: false
    },
    {
      command: "npm run dev",
      url: "http://localhost:5183",
      timeout: 60_000,
      reuseExistingServer: false
    }
  ]
});
