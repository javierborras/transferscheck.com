import { expect, test } from "@playwright/test";

// Read-only verification against the published site and API (ADR-024).
const site = process.env.PROD_URL ?? "https://www.transferscheck.com";
const api = process.env.PROD_API ?? "https://auth.transferscheck.com";

test("landing publicada muestra planes, precios, prueba sonido y sin promesas retiradas", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", e => errors.push(e.message));
  page.on("console", m => { if (m.type() === "error" && !/status of 401/.test(m.text())) errors.push(m.text()); });
  page.on("response", r => { if (r.status() >= 400 && !(r.status() === 401 && r.url().endsWith("/api/session"))) errors.push(`${r.status()} ${r.url()}`); });

  await page.goto(site);
  await expect(page.getByRole("heading", { level: 1 })).toContainText("transferencia");
  await expect(page.getByRole("link", { name: /Probar 21 días gratis/ }).first()).toBeVisible();
  await page.getByRole("button", { name: /Probar cómo suena la alerta en caja/ }).click();
  await page.locator("#planes").scrollIntoViewIfNeeded();
  await expect(page.getByRole("heading", { name: "Pack Mes" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Individual / Evento" })).toBeVisible();
  await expect(page.locator("#planes")).toContainText("5.999");
  await expect(page.locator("#planes")).toContainText("1.000");
  const body = await page.locator("body").innerText();
  expect(body).not.toMatch(/14 días|500 consultas|cashback|cupón/i);
  await page.screenshot({ path: "test-results-prod/landing-desktop.png", fullPage: true });
  expect(errors, errors.join("\n")).toEqual([]);
});

test("landing publicada en móvil sin desbordes", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(site);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
  await page.screenshot({ path: "test-results-prod/landing-mobile.png", fullPage: true });
});

test("puertas de ingreso, alta y owner cargan", async ({ page }) => {
  for (const path of ["/ingresar", "/cuenta", "/owner"]) {
    const response = await page.goto(site + path);
    expect(response?.status()).toBe(200);
    await expect(page.locator("body")).not.toBeEmpty();
  }
  await expect(page.getByRole("heading", { name: "Owner" })).toBeVisible();
  await page.screenshot({ path: "test-results-prod/owner-door.png" });
});

test("API publicada: planes, rutas protegidas y rechazo de webhook sin firma válida (ADR-022)", async ({ request }) => {
  const plans = await request.get(`${api}/api/plans`);
  expect(plans.ok()).toBeTruthy();
  const body = await plans.json();
  expect(body.plans.map((p: { code: string }) => p.code).sort()).toEqual(["pack_evento", "pack_mes"]);
  expect(body.additionalPointPrice).toBe(1000);
  expect((await request.get(`${api}/api/billing`)).status()).toBe(401);
  expect((await request.get(`${api}/api/history`)).status()).toBe(401);
  expect((await request.get(`${api}/api/owner/services`)).status()).toBeGreaterThanOrEqual(401);

  // ADR-022: shared header, missing signature, and forged signature are all refused with 401 in production.
  expect((await request.post(`${api}/api/billing/webhook`, { data: { data: { id: "1" } } })).status()).toBe(401);
  expect((await request.post(`${api}/api/billing/webhook`, {
    headers: { "X-Webhook-Secret": "test-webhook-secret" },
    data: { data: { id: "1" } }
  })).status()).toBe(401);
  expect((await request.post(`${api}/api/billing/webhook?ref=order:123&sig=deadbeef`, {
    data: { data: { id: "1" } }
  })).status()).toBe(401);
  expect((await request.post(`${api}/api/billing/webhook?data.id=1`, {
    headers: { "x-signature": "ts=1700000000000,v1=deadbeef", "x-request-id": "req-1" },
    data: { data: { id: "1" } }
  })).status()).toBe(401);
});

test("Swagger publicado lista las rutas comerciales y administrativas", async ({ page }) => {
  const spec = await (await page.request.get(`${api}/swagger/v1/swagger.json`)).json();
  for (const route of [
    "/api/plans",
    "/api/billing/checkout",
    "/api/billing/start-event",
    "/api/billing/webhook",
    "/api/logins",
    "/api/transfers",
    "/api/owner/services",
    "/api/owner/plans",
    "/api/owner/help/refresh"
  ]) {
    expect(Object.keys(spec.paths), route).toContain(route);
  }
  await page.goto(`${api}/swagger`);
  await expect(page.locator(".swagger-ui").first()).toBeVisible();
  await page.screenshot({ path: "test-results-prod/swagger.png" });
});
