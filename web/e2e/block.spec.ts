import { expect, test } from "@playwright/test";
import { setPlanCode } from "./helpers";

const apiOrigin = "http://localhost:5080";

test("web blocks on the access date until pay", async ({ page }) => {
  const email = `e2e-block-${Date.now()}@counter.test`;
  await page.goto("/cuenta");
  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Crear cuenta" }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await setPlanCode(page, "pack_mes");
  await page.goto("/conectar");
  await page.getByRole("button", { name: "Conectar Mercado Pago" }).click();
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible({ timeout: 20_000 });

  const accessUntil = new Date(Date.now() - 60_000).toISOString();
  const setAccess = await page.evaluate(
    async ({ origin, accessUntil: until }) => {
      const response = await fetch(`${origin}/api/dev/access-until`, {
        method: "PUT",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accessUntil: until })
      });
      return response.status;
    },
    { origin: apiOrigin, accessUntil }
  );
  expect(setAccess).toBe(204);

  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Se terminó el acceso" })).toBeVisible({ timeout: 20_000 });
  await expect(page.getByRole("link", { name: "Ir a Pagos" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Actualizar ahora" })).toHaveCount(0);
});
