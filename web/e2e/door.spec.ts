import { expect, test } from "@playwright/test";
import { signInWithAuth0, signUpWithAuth0 } from "./helpers";

test("login door is Auth0 Universal Login and shows no menu", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByText("TransfersCheck").first()).toBeVisible();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible();
  await expect(page.getByText("Inicia sesión en tu cuenta de TransfersCheck")).toBeVisible();
  await expect(page.getByRole("button", { name: "Iniciar sesión con Auth0" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Creá tu cuenta" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);
  // Mercado Pago OAuth is not this door.
  await expect(page.getByText("Mercado Pago")).toHaveCount(0);
  // The commerce admin has no local email/password form; Punto de Alerta keeps usuario/clave.
  await expect(page.getByLabel("Dirección de email *")).toHaveCount(0);
  await page.getByRole("button", { name: "Entrar como Punto de Alerta" }).click();
  await expect(page.getByLabel("Comercio *")).toBeVisible();
  await expect(page.getByLabel("Usuario *")).toBeVisible();
  await expect(page.getByLabel("Contraseña *")).toBeVisible();
  await expect(page.getByRole("button", { name: "Iniciar sesión", exact: true })).toBeVisible();
});

test("signup door is Auth0 and signed-in menu lists adopted entries; Cerrar sesión ends the session", async ({ page }) => {
  const email = `e2e-door-${Date.now()}@counter.test`;
  await signUpWithAuth0(page, email);

  await page.getByRole("button", { name: "Menú" }).click();
  await expect(page.getByRole("link", { name: "Transacciones" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Historial" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Configurar" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Pagos" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Salir" })).toBeVisible();
  await page.getByRole("button", { name: "Cerrar sesión" }).click();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);

  // Second sign-in through Auth0 for the same admin opens a fresh session (no Código field).
  await signInWithAuth0(page, email);
  await expect(page.getByLabel("Código *")).toHaveCount(0);
});

test("Auth0 refuses an unverified email and says so on the login screen", async ({ page }) => {
  await page.goto("/cuenta");
  await page.getByRole("button", { name: "Crear cuenta con Auth0" }).click();
  await page.getByLabel("Email").fill(`unverified.e2e-${Date.now()}@counter.test`);
  await page.getByRole("button", { name: "Continuar" }).click();
  await expect(page.getByText("Verificá tu email y volvé a intentar.")).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);
});
