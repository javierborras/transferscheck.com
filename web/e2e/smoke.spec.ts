import { expect, test } from "@playwright/test";
import { signUpWithAuth0 } from "./helpers";

// Post–wave-2 browser smoke: door and pay UI that already shipped.
// Run from web/: npm run e2e
// Playwright starts API (e2e/start-api.ps1 on :5080 + mock :5099) and Vite (:5183).

test("login door is reachable", async ({ page }) => {
  await page.goto("/ingresar");
  await expect(page.getByRole("button", { name: "Ingresar a mi cuenta" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Creá tu cuenta" })).toBeVisible();
});

test("signup door is reachable", async ({ page }) => {
  await page.goto("/cuenta");
  await expect(page.getByRole("heading", { name: "Creá tu cuenta" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Crear mi cuenta" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);
});

test("pay screen loads after signup", async ({ page }) => {
  const email = `e2e-pay-${Date.now()}@counter.test`;
  await signUpWithAuth0(page, email);
  await page.goto("/pagar");
  await expect(page.getByRole("heading", { name: "Extendé el acceso" })).toBeVisible({ timeout: 20_000 });
  await expect(page.locator(".commercial-plan .money").first()).toBeVisible();
  await expect(page.getByRole("button", { name: "Pagar Pack Mes con Mercado Pago" })).toBeVisible();
});
