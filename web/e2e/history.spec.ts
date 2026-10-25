import { expect, test } from "@playwright/test";
import { signUpWithAuth0 } from "./helpers";

test("admin history preserves real transfers and the CSV download", async ({ page }) => {
  await signUpWithAuth0(page, `history-${Date.now()}@counter.test`);
  await page.goto("/conectar");
  await expect(page.getByRole("heading", { name: "Dejá listo tu mostrador en 3 pasos" })).toBeVisible();
  await page.screenshot({ path: "test-results/onboarding-desktop.png", fullPage: true });
  await page.getByRole("button", { name: "Conectar mi Mercado Pago" }).click();
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible();
  await page.goto("/");
  await expect(page.locator("ol.transfers li")).toHaveCount(10);
  await page.goto("/historial");
  await expect(page.getByRole("heading", { name: "Historial Completo de Transferencias" })).toBeVisible();
  await expect(page.locator("ol.transfers li")).toHaveCount(10);
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "📥 Descargar planilla (Excel / CSV)" }).click();
  expect((await download).suggestedFilename()).toBe("historial.csv");
  await page.screenshot({ path: "test-results/history-desktop.png", fullPage: true });
});
