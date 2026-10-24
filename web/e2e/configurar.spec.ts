import { expect, test } from "@playwright/test";
import { signUpWithAuth0 } from "./helpers";

const apiOrigin = "http://localhost:5080";

async function signUp(page: import("@playwright/test").Page, email: string) {
  await signUpWithAuth0(page, email);
}

async function openConfigurar(page: import("@playwright/test").Page) {
  await page.getByRole("button", { name: "Menú" }).click();
  await page.getByRole("link", { name: "Configurar" }).click();
  await expect(page.getByRole("heading", { name: "Configurar" })).toBeVisible();
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

test("Configurar refuses a second account when the plan is Pack Evento (unset)", async ({ page }) => {
  await signUp(page, `e2e-evento-${Date.now()}@counter.test`);
  await confirmAlias(page, `ev${Date.now()}`.slice(0, 12));
  await openConfigurar(page);
  await page.getByLabel("Nombre *").fill("extra");
  await page.getByRole("button", { name: "Crear invitación" }).click();
  await expect(page.getByText("No se puede agregar otro Punto de Alerta en este plan.")).toBeVisible({
    timeout: 10_000
  });
});

test("Configurar on Pack Mes allows four Puntos de Alerta and refuses the fifth; revoke keeps the owner", async ({
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
  expect(fifth.status).toBe(409);

  await openConfigurar(page);
  await expect(page.getByText("Punto de Alerta")).toHaveCount(4);
  await expect(page.getByText("Admin")).toBeVisible();

  await expect(page.getByRole("button", { name: "Revocar" })).toHaveCount(4);
  await page.getByRole("button", { name: "Revocar" }).first().click();
  await page.getByRole("button", { name: "Tocá de nuevo para revocar" }).click();
  await expect(page.getByText("Admin")).toBeVisible();
  await expect(page.getByRole("button", { name: "Revocar", disabled: true })).toHaveCount(1);
  await expect(page.getByRole("button", { name: "Revocar", disabled: false })).toHaveCount(3);
});
