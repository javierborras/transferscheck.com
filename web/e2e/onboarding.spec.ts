import { expect, test } from "@playwright/test";
import { setPlanCode } from "./helpers";

test("signup, checklist, connect against the mock, see the ten, enable avisos", async ({ page, context }) => {
  await context.grantPermissions(["notifications"]);
  const email = `e2e-${Date.now()}@counter.test`;
  await page.goto("/");
  await page.getByRole("link", { name: "Creá tu cuenta" }).click();
  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Crear cuenta" }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await setPlanCode(page, "pack_mes");
  await page.goto("/conectar");
  await expect(page.getByText("1. Tenés que administrar esa cuenta de Mercado Pago.")).toBeVisible();
  await expect(page.getByText("No conectado", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Conectar Mercado Pago" }).click();
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole("button", { name: "Activar avisos" })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Activar avisos" }).click();
  await expect(page.locator("ol.transfers li.row")).toHaveCount(10, { timeout: 20_000 });
  await expect(page.getByText("Persona 1 Perez")).toBeVisible();
  await expect(page.getByText("Llegó").first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Actualizar ahora" })).toBeVisible();
  await page.goto("/conectar");
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Re-conectar cuenta" })).toBeVisible();
});
