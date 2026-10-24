import { expect, test } from "@playwright/test";

// Post–wave-2 browser smoke: door and pay UI that already shipped.
// Run from web/: npm run e2e
// Playwright starts API (e2e/start-api.ps1 on :5080 + mock :5099) and Vite (:5183).

test("login door is reachable", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByLabel("Dirección de email *")).toBeVisible();
  await expect(page.getByLabel("Contraseña *")).toBeVisible();
  await expect(page.getByRole("button", { name: "Iniciar sesión" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Creá tu cuenta" })).toBeVisible();
});

test("signup door is reachable", async ({ page }) => {
  await page.goto("/cuenta");
  await expect(page.getByRole("heading", { name: "Creá tu cuenta" })).toBeVisible();
  await expect(page.getByLabel("Dirección de email *")).toBeVisible();
  await expect(page.getByRole("button", { name: "Crear cuenta" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);
});

test("pay screen loads after signup", async ({ page }) => {
  const email = `e2e-pay-${Date.now()}@counter.test`;
  await page.goto("/cuenta");
  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Crear cuenta" }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await page.goto("/pagar");
  await expect(page.getByRole("heading", { name: "Extendé el acceso" })).toBeVisible({ timeout: 20_000 });
  await expect(page.locator(".plans .plan .amount").first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Pagar con Mercado Pago" })).toBeVisible();
});
