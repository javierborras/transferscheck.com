import { expect, test } from "@playwright/test";
import { signInWithEmailCode } from "./helpers";

test("sign-in without code is refused; code field has no length copy", async ({ page }) => {
  const email = `e2e-code-${Date.now()}@counter.test`;
  await page.goto("/cuenta");
  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Crear cuenta" }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Menú" }).click();
  await page.getByRole("button", { name: "Cerrar sesión" }).click();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible({ timeout: 20_000 });

  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page.getByLabel("Código *")).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText(/6 dígitos|six digits/i)).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);

  await signInWithEmailCode(page, email);
});
