import { expect, test } from "@playwright/test";
import { setPlanCode, createAccountWithAuth0 } from "./helpers";

test("last-10 list uses adopted columns and refresh; no Sincronizar", async ({ page, context }) => {
  await context.grantPermissions(["notifications"]);
  const email = `e2e-list-${Date.now()}@counter.test`;
  await page.goto("/");
  await page.getByRole("link", { name: "Creá tu cuenta" }).click();
  await createAccountWithAuth0(page, email);
  await setPlanCode(page, "pack_mes");
  await page.goto("/conectar");
  await expect(page.getByText("1. Tenés que administrar esa cuenta de Mercado Pago.")).toBeVisible();
  await expect(page.getByText("No conectado", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Conectar Mercado Pago" }).click();
  // OAuth returns to /conectar; do not waitForURL(/conectar/) — that matches the current page.
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Activar avisos" }).click();
  await expect(page.locator("ol.transfers li.row")).toHaveCount(10, { timeout: 20_000 });
  await expect(page.locator(".transfer-columns")).toContainText("Fecha");
  await expect(page.locator(".transfer-columns")).toContainText("Pagador");
  await expect(page.locator(".transfer-columns")).toContainText("Monto");
  await expect(page.locator(".transfer-columns")).toContainText("Estado");
  await expect(page.getByText("Llegó").first()).toBeVisible();
  await expect(page.getByText("Pendiente").first()).toBeVisible();
  await expect(page.getByText("Rechazado").first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Actualizar ahora" })).toBeVisible();
  await expect(page.getByText("Sincronizar")).toHaveCount(0);

  await page.setViewportSize({ width: 390, height: 844 });
  const refresh = page.getByRole("button", { name: "Actualizar ahora" });
  const box = await refresh.boundingBox();
  expect(box).toBeTruthy();
  expect(box!.height).toBeGreaterThanOrEqual(48);
});
