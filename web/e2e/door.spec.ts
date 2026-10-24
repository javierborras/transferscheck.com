import { expect, test } from "@playwright/test";

test("login door shows adopted copy and no identity providers", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("TransfersCheck").first()).toBeVisible();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible();
  await expect(page.getByText("Inicia sesión en tu cuenta de TransfersCheck")).toBeVisible();
  await expect(page.getByLabel("Dirección de email *")).toBeVisible();
  await expect(page.getByLabel("Contraseña *")).toBeVisible();
  await expect(page.getByRole("button", { name: "Iniciar sesión" })).toBeVisible();
  await expect(page.getByRole("button", { name: /mostrar|ocultar contraseña/i })).toBeVisible();
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);
  await expect(page.getByText("Auth0")).toHaveCount(0);
  await expect(page.getByText("Google")).toHaveCount(0);
});

test("signed-in menu lists adopted entries and Cerrar sesión ends the session", async ({ page }) => {
  const email = `e2e-door-${Date.now()}@counter.test`;
  await page.goto("/cuenta");
  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Crear cuenta" }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });

  await page.getByRole("button", { name: "Menú" }).click();
  await expect(page.getByRole("link", { name: "Transacciones" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Configurar" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Pagos" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Salir" })).toBeVisible();
  await page.getByRole("button", { name: "Cerrar sesión" }).click();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);
});
