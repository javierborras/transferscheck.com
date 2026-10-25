import { expect, test } from "@playwright/test";
import { signUpWithAuth0 } from "./helpers";

const apiOrigin = "http://localhost:5080";

async function signUp(page: import("@playwright/test").Page, email: string) {
  await signUpWithAuth0(page, email);
}

async function openConfigurar(page: import("@playwright/test").Page) {
  await page.getByRole("button", { name: "Menú" }).click();
  await page.getByRole("navigation", { name: "Menú", exact: true }).getByRole("link", { name: "Puntos de Alerta" }).click();
  await expect(page.getByRole("heading", { name: "Accesos de Mostrador (Puntos de Alerta)" })).toBeVisible();
}

async function apiJson(
  page: import("@playwright/test").Page,
  path: string,
  method: string,
  body?: unknown
) {
  return page.evaluate(
    async ({ origin, path: apiPath, method: httpMethod, bodyJson }) => {
      const response = await fetch(`${origin}${apiPath}`, {
        method: httpMethod,
        credentials: "include",
        headers: bodyJson ? { "Content-Type": "application/json" } : undefined,
        body: bodyJson
      });
      return { status: response.status, body: await response.text() };
    },
    {
      origin: apiOrigin,
      path,
      method,
      bodyJson: body === undefined ? undefined : JSON.stringify(body)
    }
  );
}

async function confirmAlias(page: import("@playwright/test").Page, alias: string) {
  const result = await apiJson(page, "/api/alias", "POST", { alias });
  expect(result.status, result.body).toBe(200);
}

test("Individual includes one point and leaves the next pending payment", async ({ page }) => {
  await signUp(page, `e2e-evento-${Date.now()}@counter.test`);
  await apiJson(page, "/api/dev/plan-code", "PUT", { planCode: "pack_evento" });
  await confirmAlias(page, `ev${Date.now()}`.slice(0, 12));
  await openConfigurar(page);
  await expect(page.getByRole("button", { name: "Crear invitación" })).toBeVisible();
  const result = await apiJson(page, "/api/logins", "POST", { name: "extra" });
  expect(result.status).toBe(201);
  expect(JSON.parse(result.body).requiresPayment).toBe(false);
  const extra = await apiJson(page, "/api/logins", "POST", { name: "adicional" });
  expect(JSON.parse(extra.body).requiresPayment).toBe(true);
});

test("Configurar on Pack Mes includes four points and allows a fifth pending payment; revoke keeps the owner", async ({
  page
}) => {
  await signUp(page, `e2e-mes-${Date.now()}@counter.test`);
  const setPlan = await apiJson(page, "/api/dev/plan-code", "PUT", { planCode: "pack_mes" });
  expect(setPlan.status).toBe(204);
  const alias = `mes${Date.now()}`.slice(0, 12);
  await confirmAlias(page, alias);

  for (let i = 0; i < 4; i++) {
    const created = await apiJson(page, "/api/logins", "POST", { name: `alerta${i}` });
    expect(created.status, created.body).toBe(201);
  }

  const fifth = await apiJson(page, "/api/logins", "POST", { name: "alertaextra" });
  expect(fifth.status).toBe(201);
  expect(JSON.parse(fifth.body).requiresPayment).toBe(true);

  await openConfigurar(page);
  await expect(page.getByText("Punto de Alerta", { exact: true })).toHaveCount(5);
  await expect(page.locator(".login-row .role").filter({ hasText: /^Admin$/ })).toBeVisible();

  await expect(page.getByRole("button", { name: "Revocar" })).toHaveCount(5);
  await page.getByRole("button", { name: "Revocar" }).first().click();
  await page.getByRole("button", { name: "Tocá de nuevo para revocar" }).click();
  await expect(page.locator(".login-row .role").filter({ hasText: /^Admin$/ })).toBeVisible();
  await expect(page.getByRole("button", { name: "Revocar", disabled: true })).toHaveCount(1);
  await expect(page.getByRole("button", { name: "Revocar", disabled: false })).toHaveCount(4);
});
