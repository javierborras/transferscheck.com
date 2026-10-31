import { expect, test } from "@playwright/test";
import { apiOrigin, signUpWithAuth0 } from "./helpers";

async function leaveCommerce(page: import("@playwright/test").Page) {
  await page.getByRole("button", { name: "Menú" }).click();
  await page.getByRole("button", { name: "Cerrar sesión" }).click();
  await expect(page.getByRole("heading", { name: /Que tu mostrador|Hola de nuevo/ })).toBeVisible({ timeout: 20_000 });
}

async function plantOrder(page: import("@playwright/test").Page, amount: number) {
  const status = await page.evaluate(
    async ({ origin, amount: value }) => {
      const response = await fetch(`${origin}/api/dev/billing-order`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ amount: value, planCode: "pack_mes", paid: true })
      });
      return response.status;
    },
    { origin: apiOrigin, amount }
  );
  expect(status).toBe(200);
}

test("owner dashboard shows tasks, clients, and filtered billing", async ({ page }) => {
  test.setTimeout(90_000);
  const first = `dash-a-${Date.now()}@counter.test`;
  const second = `dash-b-${Date.now()}@counter.test`;

  await signUpWithAuth0(page, first);
  await plantOrder(page, 1111);
  await leaveCommerce(page);

  await signUpWithAuth0(page, second);
  await plantOrder(page, 2222);
  await leaveCommerce(page);

  await page.goto("/owner");
  await page.getByLabel("Dirección de email *").fill("owner@transferscheck.test");
  await page.getByLabel("Contraseña *").fill("owner-password-1");
  await page.getByRole("button", { name: "Entrar" }).click();

  const nav = page.getByRole("navigation", { name: "Secciones del owner" });
  await expect(nav.getByRole("button", { name: "Inicio" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Clientes" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Facturaciones" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Planes" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Servicios" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Helpdesk" })).toBeVisible();
  await expect(nav.getByRole("button", { name: "Datos comerciales" })).toHaveCount(0);
  await expect(page.getByText("Mail sin configurar")).toBeVisible();

  await nav.getByRole("button", { name: "Clientes" }).click();
  await expect(page.getByRole("cell", { name: first })).toBeVisible();
  await expect(page.getByRole("cell", { name: second })).toBeVisible();
  await expect(page.getByRole("columnheader", { name: "Pagador" })).toHaveCount(0);

  await nav.getByRole("button", { name: "Facturaciones" }).click();
  await expect(page.locator('[data-amount="1111"]')).toBeVisible();
  await expect(page.locator('[data-amount="2222"]')).toBeVisible();
  await page.getByLabel("Cliente").selectOption({ label: first });
  await expect(page.locator('[data-amount="1111"]')).toBeVisible();
  await expect(page.locator('[data-amount="2222"]')).toHaveCount(0);
  await expect(page.getByRole("columnheader", { name: "Pagador" })).toHaveCount(0);

  await nav.getByRole("button", { name: "Planes" }).click();
  await expect(page.getByText("pack_mes")).toBeVisible();
  await expect(page.getByText("pack_evento")).toBeVisible();

  await nav.getByRole("button", { name: "Servicios" }).click();
  await expect(page.getByText("WhatsApp")).toBeVisible();
  await expect(page.getByText("Solo wa.me · sin clave")).toBeVisible();

  await nav.getByRole("button", { name: "Helpdesk" }).click();
  await expect(page.getByRole("heading", { name: "Incidentes" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Mensajes" })).toBeVisible();
  await expect(page.getByRole("columnheader", { name: "Pagador" })).toHaveCount(0);
  await expect(page.getByRole("columnheader", { name: "CUIT" })).toHaveCount(0);
});
