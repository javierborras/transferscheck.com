import { expect, test } from "@playwright/test";

test("signup, checklist, connect against the mock, see the ten, enable avisos", async ({ page, context }) => {
  await context.grantPermissions(["notifications"]);
  const email = `e2e-${Date.now()}@counter.test`;
  await page.goto("/");
  await page.getByRole("link", { name: "Creá tu cuenta" }).click();
  await page.getByLabel("Correo").fill(email);
  await page.getByLabel("Contraseña").fill("password1");
  await page.getByRole("button", { name: "Creá tu cuenta" }).click();
  await expect(page.getByText("1. Tenés que administrar esa cuenta de Mercado Pago.")).toBeVisible();
  await page.getByRole("button", { name: "Conectar Mercado Pago" }).click();
  await expect(page.getByRole("listitem")).toHaveCount(10, { timeout: 20_000 });
  await expect(page.getByText("Persona 1 Perez")).toBeVisible();
  await expect(page.getByText("Llegó").first()).toBeVisible();
  await page.getByRole("button", { name: "Activá los avisos" }).click();
  await expect(page.getByRole("button", { name: "Activá los avisos" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Actualizar" })).toBeVisible();
});
