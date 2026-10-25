import { expect, test } from "@playwright/test";
import { signUpWithAuth0 } from "./helpers";

test("checklist shows adopted connect words, numbered help, no video player, no pay step", async ({ page }) => {
  const email = `e2e-check-${Date.now()}@counter.test`;
  await signUpWithAuth0(page, email);
  await page.goto("/conectar");

  await expect(page.getByText("Creá tu cuenta de comercio")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Conectá tu Mercado Pago de forma segura" })).toBeVisible();
  await expect(page.getByText("No conectado", { exact: true })).toBeVisible();
  await expect(page.getByText("1. Tenés que administrar esa cuenta de Mercado Pago.")).toBeVisible();
  await expect(page.getByText("2. Tocá Conectar.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Conectar mi Mercado Pago" })).toBeVisible();
  await expect(page.getByText("Activá los avisos en este dispositivo")).toBeVisible();
  await expect(page.getByText(/Tenés 21 días de prueba gratis/)).toBeVisible();
  await expect(page.locator("iframe.video")).toHaveCount(0);
  await expect(page.getByText("Pagar")).toHaveCount(0);
  await expect(page.getByText("Suscripción")).toHaveCount(0);
});
