import { expect, test } from "@playwright/test";

test("counter unlocks audio and deduplicates an accreditation across live events and refresh", async ({ page }) => {
  await page.addInitScript(() => {
    const state = window as unknown as { stream: EventTarget; tones: number };
    state.tones = 0;
    const create = AudioContext.prototype.createOscillator;
    AudioContext.prototype.createOscillator = function () { state.tones++; return create.call(this); };
    class Stream extends EventTarget {
      constructor() { super(); state.stream = this; }
      close() {}
    }
    Object.defineProperty(window, "EventSource", { value: Stream });
  });
  const row = { id: "new-payment", payerName: "Cliente real", amount: 1234.56, occurredAt: new Date().toISOString(), status: "approved", statusLabel: "Llegó" };
  let received = false;
  await page.route("**/api/session", route => route.fulfill({ json: { accountId: "cashier", tenantId: "commerce", email: "", isOwner: false } }));
  await page.route("**/api/alias", route => route.fulfill({ json: { alias: "#comercio" } }));
  await page.route("**/api/checklist", route => route.fulfill({ json: { alertsDone: true } }));
  await page.route("**/api/billing", route => route.fulfill({ json: { connected: true, blocked: false, accountEnabled: true } }));
  await page.route("**/api/transfers", route => route.fulfill({ json: { items: received ? [row] : [], pollIntervalSeconds: 300, buttonCooldownSeconds: 60 } }));
  await page.route("**/api/transfers/refresh", route => route.fulfill({ json: { count: 1 } }));
  await page.goto("/");
  await expect(page.getByText(/Todavía no detectamos transferencias/)).toBeVisible();
  await page.getByRole("button", { name: /Tocá acá para activar/ }).click();
  await expect(page.getByText("🔊 Alerta sonora activa")).toBeVisible();
  const tones = () => page.evaluate(() => (window as unknown as { tones: number }).tones);
  expect(await tones()).toBe(3);
  received = true;
  const send = () => page.evaluate(payment => {
    (window as unknown as { stream: EventTarget }).stream.dispatchEvent(new MessageEvent("LiveTransferArrived", { data: JSON.stringify({ messageType: "LiveTransferArrived", ...payment }) }));
  }, row);
  await send();
  await expect(page.locator(".latest-transfer .amount")).toContainText("1.234,56");
  await expect.poll(tones).toBe(6);
  await send();
  await page.getByRole("button", { name: "🔄 Revisar si entró un pago ahora" }).click();
  await expect(page.getByRole("button", { name: /Esperá/ })).toBeDisabled();
  await expect.poll(tones).toBe(6);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: "test-results/counter-mobile.png", fullPage: true });
});

test("denied browser notifications show an actionable message without registering a device", async ({ page }) => {
  await page.addInitScript(() => Object.defineProperty(Notification, "permission", { get: () => "denied" }));
  let registered = false;
  await page.route("**/api/session", route => route.fulfill({ json: { isOwner: false, accountId: "cashier" } }));
  await page.route("**/api/alias", route => route.fulfill({ json: {} }));
  await page.route("**/api/checklist", route => route.fulfill({ json: { alertsDone: false } }));
  await page.route("**/api/billing", route => route.fulfill({ json: { connected: true, blocked: false, accountEnabled: true } }));
  await page.route("**/api/transfers", route => route.fulfill({ json: { items: [], pollIntervalSeconds: 300, buttonCooldownSeconds: 60 } }));
  await page.route("**/api/push/tokens", route => { registered = true; return route.fulfill({ status: 204 }); });
  await page.goto("/");
  await expect(page.getByRole("status", { name: "Estado de las notificaciones" })).toBeVisible();
  await expect(page.getByText(/Tu navegador las bloqueó/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Activar notificaciones" })).toHaveCount(0);
  expect(registered).toBe(false);
});


test("an initially empty counter fills its ten rows when payments arrive together", async ({ page }) => {
  await page.addInitScript(() => {
    class Stream extends EventTarget { constructor() { super(); (window as unknown as { stream: EventTarget }).stream = this; } close() {} }
    Object.defineProperty(window, "EventSource", { value: Stream });
  });
  await page.route("**/api/session", route => route.fulfill({ json: { accountId: "point", tenantId: "batch", isOwner: false } }));
  await page.route("**/api/alias", route => route.fulfill({ json: {} }));
  let completedLoads = 0;
  await page.route("**/api/checklist", route => { completedLoads++; return route.fulfill({ json: {} }); });
  await page.route("**/api/billing", route => route.fulfill({ json: { connected: true, blocked: false, accountEnabled: true } }));
  await page.route("**/api/transfers", route => route.fulfill({ json: { items: [], take: 10, pollIntervalSeconds: 30, buttonCooldownSeconds: 10 } }));
  await page.route("**/api/transfers/refresh", route => route.fulfill({ json: {} }));
  await page.goto("/");
  // Complete initial read + automatic refresh before injecting genuinely new payments.
  await expect.poll(() => completedLoads).toBeGreaterThanOrEqual(2);
  await expect(page.getByText(/Todavía no detectamos/)).toBeVisible();
  await page.evaluate(() => {
    for (let i = 0; i < 12; i++) (window as unknown as { stream: EventTarget }).stream.dispatchEvent(new MessageEvent("LiveTransferArrived", { data: JSON.stringify({ messageType: "LiveTransferArrived", id: `batch-${i}`, payerName: `Persona ${i}`, amount: i + 1, occurredAt: new Date(Date.now() + i * 1000).toISOString(), status: "approved", statusLabel: "Llegó" }) }));
  });
  await expect(page.locator("ol.transfers li")).toHaveCount(10);
  await expect(page.locator("ol.transfers li").first()).toContainText("Persona 11");
});
