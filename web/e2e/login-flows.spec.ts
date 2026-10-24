import { expect, test, type Page } from "@playwright/test";
import { setPlanCode, signInWithAuth0, signUpWithAuth0 } from "./helpers";

const apiOrigin = "http://localhost:5080";

async function confirmAlias(page: Page, alias: string) {
  const status = await page.evaluate(
    async ({ origin, value }) => {
      const response = await fetch(`${origin}/api/alias`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ alias: value })
      });
      return response.status;
    },
    { origin: apiOrigin, value: alias }
  );
  expect(status).toBe(200);
}

async function logOut(page: Page) {
  await page.getByRole("button", { name: "Menú" }).click();
  await page.getByRole("button", { name: "Cerrar sesión" }).click();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible({ timeout: 20_000 });
}

test("a new Auth0 account stays signed in across reloads and signs back in after logout", async ({ page }) => {
  const email = `e2e-reload-${Date.now()}@counter.test`;
  await signUpWithAuth0(page, email);

  await page.reload();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await page.goto("/usuarios");
  await expect(page.getByRole("heading", { name: "Configurar" })).toBeVisible();

  await logOut(page);
  await page.reload();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible();
  await page.goto("/usuarios");
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible();

  await signInWithAuth0(page, email);
});

test("a first-time email arriving through the login button creates the account", async ({ page }) => {
  // This is the path that used to end in "Invalid column name 'PasswordSet'".
  const email = `e2e-first-login-${Date.now()}@counter.test`;
  await signInWithAuth0(page, email);
  await page.getByRole("button", { name: "Menú" }).click();
  await expect(page.getByRole("link", { name: "Configurar" })).toBeVisible();
});

test("Punto de Alerta: invite, create password, log out, wrong password, log in, reload, log out", async ({ page }) => {
  const stamp = Date.now();
  await signUpWithAuth0(page, `e2e-pa-owner-${stamp}@counter.test`);
  await setPlanCode(page, "pack_mes");
  const alias = `pa${stamp}`.slice(0, 12);
  await confirmAlias(page, alias);

  await page.getByRole("button", { name: "Menú" }).click();
  await page.getByRole("link", { name: "Configurar" }).click();
  await page.getByLabel("Nombre *").fill("caja1");
  await page.getByRole("button", { name: "Crear invitación" }).click();
  const inviteUrl = await page.getByLabel("Enlace de invitación").inputValue();
  expect(inviteUrl).toContain("/invitar/");
  await logOut(page);

  // Invite link: choosing a password signs the Punto de Alerta in.
  await page.goto(new URL(inviteUrl).pathname);
  await expect(page.getByRole("heading", { name: "Creá tu contraseña" })).toBeVisible();
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Guardar e ingresar" }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await page.getByRole("button", { name: "Menú" }).click();
  await expect(page.getByRole("link", { name: "Configurar" })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "Transacciones" })).toBeVisible();
  await page.getByRole("button", { name: "Cerrar sesión" }).click();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible({ timeout: 20_000 });

  // The link is single use.
  await page.goto(new URL(inviteUrl).pathname);
  await page.getByLabel("Contraseña *").fill("password2");
  await page.getByRole("button", { name: "Guardar e ingresar" }).click();
  await expect(page.getByText("El enlace no sirve o ya se usó.")).toBeVisible();

  // Usuario/clave door.
  await page.goto("/");
  await page.getByRole("button", { name: "Entrar como Punto de Alerta" }).click();
  await page.getByLabel("Comercio *").fill(alias);
  await page.getByLabel("Usuario *").fill("caja1");
  await page.getByLabel("Contraseña *").fill("wrong-password");
  await page.getByRole("button", { name: "Iniciar sesión", exact: true }).click();
  await expect(page.getByText("El comercio, el usuario o la contraseña no sirven.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Menú" })).toHaveCount(0);

  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Iniciar sesión", exact: true }).click();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });

  await page.reload();
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
  await logOut(page);
});

test("a bad invite link says so", async ({ page }) => {
  await page.goto(`/invitar/${"0".repeat(48)}`);
  await page.getByLabel("Contraseña *").fill("password1");
  await page.getByRole("button", { name: "Guardar e ingresar" }).click();
  await expect(page.getByText("El enlace no sirve o ya se usó.")).toBeVisible();
});

test("login screen shows a message for every Auth0 error code", async ({ page }) => {
  const cases: Record<string, string> = {
    email_unverified: "Verificá tu email y volvé a intentar.",
    not_allowed: "Esa cuenta no puede entrar como admin.",
    invalid_state: "El inicio de sesión venció. Probá de nuevo.",
    server_error: "No pudimos abrir tu sesión. Probá de nuevo en un momento.",
    exchange_failed: "No se pudo iniciar sesión con Auth0.",
    access_denied: "No se pudo iniciar sesión con Auth0."
  };
  for (const [code, message] of Object.entries(cases)) {
    await page.goto(`/?auth0_error=${code}`);
    await expect(page.getByText(message)).toBeVisible();
  }
});
