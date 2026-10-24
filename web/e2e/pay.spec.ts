import { expect, test } from "@playwright/test";
import { setPlanCode, signUpWithAuth0 } from "./helpers";

const apiOrigin = "http://localhost:5080";

test("pay screen shows Pack Evento and Pack Mes from the plan table", async ({ page }) => {
  const email = `e2e-plans-${Date.now()}@counter.test`;
  await signUpWithAuth0(page, email);
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

test("browser return from pay does not move access; checkout returns a payment link", async ({ page }) => {
  const email = `e2e-return-${Date.now()}@counter.test`;
  await signUpWithAuth0(page, email);
  await setPlanCode(page, "pack_mes");
  await page.goto("/conectar");
  await page.getByRole("button", { name: "Conectar Mercado Pago" }).click();
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible({ timeout: 20_000 });

  const before = await page.evaluate(async (origin) => {
    const response = await fetch(`${origin}/api/billing`, { credentials: "include" });
    const body = await response.json();
    return body.accessUntil as string;
  }, apiOrigin);
  expect(before).toBeTruthy();

  const checkout = await page.evaluate(async (origin) => {
    const response = await fetch(`${origin}/api/billing/checkout`, {
      method: "POST",
      credentials: "include"
    });
    return { status: response.status, body: await response.json() };
  }, apiOrigin);
  expect(checkout.status).toBe(200);
  expect(String(checkout.body.initPoint)).toContain("pref-1");

  await page.goto(`${apiOrigin}/api/billing/return`);
  await page.waitForURL(/localhost:5183/);

  const after = await page.evaluate(async (origin) => {
    const response = await fetch(`${origin}/api/billing`, { credentials: "include" });
    const body = await response.json();
    return body.accessUntil as string;
  }, apiOrigin);
  expect(after).toBe(before);
});
