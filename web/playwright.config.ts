import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  use: {
    ...devices["Desktop Chrome"],
    baseURL: "http://localhost:5183"
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
