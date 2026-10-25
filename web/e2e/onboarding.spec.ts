import { expect, test } from "@playwright/test";
import { setPlanCode, createAccountWithAuth0, allowMockNotifications } from "./helpers";

test("signup, checklist, connect against the mock, see the ten, enable avisos", async ({ page, context }) => {
  await allowMockNotifications(context);
  const email = `e2e-${Date.now()}@counter.test`;
  await page.goto("/");
  await page.getByRole("link", { name: "Probar 21 días gratis", exact: true }).click();
  await createAccountWithAuth0(page, email);
  await setPlanCode(page, "pack_mes");
  await page.goto("/conectar");
  await expect(page.getByText("1. Tenés que administrar esa cuenta de Mercado Pago.")).toBeVisible();
  await expect(page.getByText("No conectado", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Conectar mi Mercado Pago" }).click();
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole("button", { name: "Activar avisos" })).toBeVisible({ timeout: 20_000 });
  expect(await page.evaluate(() => ({ notifications: "Notification" in window, permission: Notification.permission, worker: "serviceWorker" in navigator }))).toEqual({ notifications: true, permission: "granted", worker: true });
  await page.getByRole("button", { name: "Activar avisos" }).click();
  await expect(page.locator("ol.transfers li.row")).toHaveCount(10, { timeout: 20_000 });
  await expect(page.getByText("Pagó: Persona 1 Perez")).toBeVisible();
  await expect(page.getByText("✓ Acreditado").first()).toBeVisible();
  await expect(page.locator(".dock button")).toBeVisible();
  await page.goto("/conectar");
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Reconectar Mercado Pago" })).toBeVisible();
});
