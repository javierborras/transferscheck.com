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
  await expect(page.getByText("Suscripción", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Pack Evento", exact: true })).toBeVisible();
  await expect(page.getByText("Un día, un usuario. Ideal para la fiesta, feria o show.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "Pack Mes", exact: true })).toBeVisible();
  await expect(page.getByText("24 horas", { exact: true })).toBeVisible();
  await expect(page.getByText("30 días", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Pagar Pack Mes con Mercado Pago" })).toBeVisible();
});

test("browser return from pay does not move access; checkout returns a payment link", async ({ page }) => {
  const email = `e2e-return-${Date.now()}@counter.test`;
  await signUpWithAuth0(page, email);
  await setPlanCode(page, "pack_mes");
  await page.goto("/conectar");
  await page.getByRole("button", { name: "Conectar mi Mercado Pago" }).click();
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

test("renewal does not promise another trial and each card checks out the selected plan", async ({ page }) => {
  await signUpWithAuth0(page, `renewal-${Date.now()}@counter.test`);
  await page.goto("/conectar");
  await page.getByRole("button", { name: "Conectar mi Mercado Pago" }).click();
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible();
  await page.evaluate(async origin => {
    await fetch(`${origin}/api/dev/access-until`, { method: "PUT", credentials: "include", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ accessUntil: new Date(Date.now() - 60000).toISOString() }) });
  }, apiOrigin);
  await page.goto("/pagar");
  await expect(page.getByRole("heading", { name: "Las alertas de tu mostrador están en pausa" })).toBeVisible();
  await expect(page.getByText("🎁 21 DÍAS DE PRUEBA GRATIS")).toHaveCount(0);
  await page.screenshot({ path: "test-results/renewal-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: "test-results/renewal-mobile.png", fullPage: true });
  const checkout = page.waitForRequest(request => request.url().endsWith("/api/billing/checkout"));
  await page.getByRole("button", { name: "Pagar Pack Evento (24 hs)" }).click();
  expect((await checkout).postDataJSON().planCode).toBe("pack_evento");
  await page.waitForURL(/checkout\/pref-1/);
});
