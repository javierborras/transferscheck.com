import { expect, test } from "@playwright/test";

test("pay screen shows Pack Evento and Pack Mes from the plan table", async ({ page }) => {
  const email = `e2e-plans-${Date.now()}@counter.test`;
  await page.goto("/cuenta");
  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Crear cuenta" }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await page.goto("/pagar");
  await expect(page.getByRole("heading", { name: "Extendé el acceso" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText("Email", { exact: true })).toBeVisible();
  await expect(page.getByText(email)).toBeVisible();
  await expect(page.getByText("Plan", { exact: true })).toBeVisible();
  await expect(page.getByText("Suscripción")).toHaveCount(0);
  await expect(page.getByText("Pack Evento")).toBeVisible();
  await expect(page.getByText("Un día, un usuario. Ideal para la fiesta o el show.")).toBeVisible();
  await expect(page.getByText("Pack Mes")).toBeVisible();
  await expect(page.getByText("24 horas")).toBeVisible();
  await expect(page.getByText("30 días")).toBeVisible();
  await expect(page.getByRole("button", { name: "Pagar con Mercado Pago" })).toBeVisible();
});
