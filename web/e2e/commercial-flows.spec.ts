import { expect, test, type APIRequestContext } from "@playwright/test";
import { createAccountWithAuth0, signUpWithAuth0 } from "./helpers";

async function approveCheckout(request: APIRequestContext) {
  const approval = await request.post("http://localhost:5099/mock/checkout/approve");
  expect(approval.ok()).toBeTruthy();
  const { paymentId, amount } = await approval.json();
  const result = await request.post("http://localhost:5080/api/billing/webhook", {
    headers: { "X-Webhook-Secret": "test-webhook-secret" }, data: { data: { id: paymentId } }
  });
  expect(result.ok()).toBeTruthy(); expect((await result.json()).applied).toBe(true);
  return amount as number;
}

test("Individual: compra, confirmación, inicio explícito, horario de corte y vencimiento", async ({ page, request }) => {
  await test.step("Alta Individual y conexión sin iniciar las 24 horas", async () => {
    await page.goto("/cuenta?plan=pack_evento");
    await createAccountWithAuth0(page, `event-browser-${Date.now()}@counter.test`);
    await page.goto("/conectar");
    await page.getByRole("button", { name: "Conectar mi Mercado Pago" }).click();
    await expect(page.getByText("Conectado", { exact: true })).toBeVisible();
    const billing = await page.request.get("http://localhost:5080/api/billing");
    expect((await billing.json()).accessUntil).toBeNull();
  });
  await test.step("Comprar el pase y simular la aprobación del proveedor", async () => {
    await page.goto("/pagar");
    await page.getByRole("button", { name: "Comprar Individual (24 hs)" }).click();
    await page.waitForURL(/checkout\/pref-1/);
    await approveCheckout(request);
    await page.goto("/pagar");
    await expect(page.getByRole("button", { name: "Iniciar Evento", exact: true })).toBeVisible();
    await page.screenshot({ path: "test-results/individual-pago-sin-iniciar.png", fullPage: true });
  });
  await test.step("Confirmar el horario, iniciar y verificar el vencimiento de 24 horas", async () => {
    const dialogs: string[] = [];
    page.on("dialog", async dialog => { dialogs.push(dialog.message()); await dialog.accept(); });
    await page.getByRole("button", { name: "Iniciar Evento", exact: true }).click();
    await expect(page).toHaveURL("http://localhost:5183/");
    await expect(page.locator("ol.transfers li")).toHaveCount(10);
    expect(dialogs.some(text => text.includes("se deshabilitarán"))).toBe(true);
    expect(dialogs.some(text => text.includes("Evento iniciado"))).toBe(true);
    const billing = await page.request.get("http://localhost:5080/api/billing");
    const state = await billing.json();
    expect(Date.parse(state.accessUntil) - Date.parse(state.serverNow)).toBeGreaterThan(23.9 * 3600000);
    expect(state.eventReadyCount).toBe(0);
    await page.screenshot({ path: "test-results/individual-activo.png", fullPage: true });
  });
  await test.step("Simular el vencimiento y comprobar la pausa", async () => {
    const expired = await page.request.put("http://localhost:5080/api/dev/access-until", { data: { accessUntil: new Date(Date.now() - 60000).toISOString() } });
    expect(expired.ok()).toBeTruthy(); await page.reload();
    await expect(page.getByRole("link", { name: "Ir a Pagos", exact: true })).toBeVisible();
    await page.goto("/pagar");
    await expect(page.getByRole("heading", { name: "Las alertas de tu mostrador están en pausa" })).toBeVisible();
    await page.screenshot({ path: "test-results/individual-vencido.png", fullPage: true });
  });
});

test("Mensual: cuatro incluidos, adicional de $1.000 y selección de renovación", async ({ page, request }) => {
  await signUpWithAuth0(page, `extra-browser-${Date.now()}@counter.test`);
  await page.goto("/conectar"); await page.getByRole("button", { name: "Conectar mi Mercado Pago" }).click();
  await expect(page.getByText("Conectado", { exact: true })).toBeVisible();
  await page.goto("/usuarios");
  await page.getByLabel("Alias del comercio *").fill(`br${Date.now()}`.slice(0, 12));
  await page.getByRole("button", { name: "Confirmar alias", exact: true }).click();
  for (let i = 1; i <= 5; i++) {
    await page.getByLabel("Nombre *", { exact: true }).fill(`punto${i}`);
    await page.getByRole("button", { name: "Crear invitación", exact: true }).click();
    await expect(page.locator(".login-row").filter({ hasText: `punto${i}#` })).toBeVisible();
  }
  await expect(page.getByText("Incluido en tu plan", { exact: true })).toHaveCount(4);
  await expect(page.getByText("Adicional pendiente de pago o renovación", { exact: true })).toBeVisible();
  await page.screenshot({ path: "test-results/adicional-pendiente.png", fullPage: true });
  const before = await (await page.request.get("http://localhost:5080/api/billing")).json();
  await page.getByRole("button", { name: "Habilitar adicional por $ 1.000 hasta el vencimiento del Admin" }).click();
  await page.waitForURL(/checkout\/pref-1/); expect(await approveCheckout(request)).toBe(1000);
  await page.goto("/usuarios");
  await expect(page.getByText(/Adicional habilitado hasta/)).toBeVisible();
  const points = await (await page.request.get("http://localhost:5080/api/logins")).json();
  expect(points.find((point: { displayName: string }) => point.displayName.startsWith("punto5#")).extraAccessUntil).toBe(before.accessUntil);
  await page.screenshot({ path: "test-results/adicional-habilitado.png", fullPage: true });
  await page.goto("/pagar");
  await expect(page.getByText(/Renovación: 1 adicional/)).toContainText("6.999");
  await page.screenshot({ path: "test-results/mensual-renovacion-con-adicional.png", fullPage: true });
  await page.goto("/usuarios"); await page.getByLabel(/Incluir en la próxima renovación/).click();
  await expect(page.getByLabel(/Incluir en la próxima renovación/)).not.toBeChecked();
  await expect(page.getByLabel(/Incluir en la próxima renovación/)).toBeEnabled();
  await page.goto("/pagar"); await expect(page.getByText(/Renovación: 1 adicional/)).toHaveCount(0);
  await page.getByRole("button", { name: "Renovar Pack Mes con Mercado Pago" }).click();
  await page.waitForURL(/checkout\/pref-1/); expect(await approveCheckout(request)).toBe(5999);
  await page.goto("/usuarios");
  await expect(page.getByText(/Adicional habilitado hasta/)).toBeVisible();
  const after = await (await page.request.get("http://localhost:5080/api/billing")).json();
  expect(Date.parse(after.accessUntil) - Date.parse(before.accessUntil)).toBe(30 * 86400000);
  const retained = await (await page.request.get("http://localhost:5080/api/logins")).json();
  expect(retained.find((point: { displayName: string }) => point.displayName.startsWith("punto5#")).extraAccessUntil).toBe(before.accessUntil);
});
