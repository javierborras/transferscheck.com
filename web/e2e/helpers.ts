import { expect, type Page, type BrowserContext } from "@playwright/test";

// This headless Windows browser reports notifications denied even after grantPermissions.
// Simulate consent for the mock-token happy path; production still checks the real permission.
export async function allowMockNotifications(context: BrowserContext) {
  await context.addInitScript(() => Object.defineProperty(Notification, "permission", { get: () => "granted" }));
}

const apiOrigin = "http://localhost:5080";

// Commerce door is Auth0 Universal Login (ADR-018). The mock tenant in the mock server serves a
// one-field Universal Login page; the e2e API points Auth0:Authority at it (start-api.ps1).
async function finishMockUniversalLogin(page: Page, email: string) {
  await page.getByLabel("Email").fill(email);
  await page.getByRole("button", { name: "Continuar" }).click();
}

/** From the signup screen: Auth0 signup, then the app shows the signed-in menu. */
export async function createAccountWithAuth0(page: Page, email: string) {
  await page.getByRole("button", { name: "Crear mi cuenta" }).click();
  await finishMockUniversalLogin(page, email);
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
}

export async function signUpWithAuth0(page: Page, email: string) {
  await page.goto("/cuenta");
  await createAccountWithAuth0(page, email);
}

/** From the login screen: Auth0 sign-in for an existing commerce admin. */
export async function signInWithAuth0(page: Page, email: string) {
  await page.goto("/ingresar");
  await page.getByRole("button", { name: "Ingresar a mi cuenta" }).click();
  await finishMockUniversalLogin(page, email);
  await expect(page.getByRole("button", { name: "Menú" })).toBeVisible({ timeout: 20_000 });
}
export async function setPlanCode(page: Page, planCode: string) {
  const result = await page.evaluate(
    async ({ origin, planCode: code }) => {
      const response = await fetch(`${origin}/api/dev/plan-code`, {
        method: "PUT",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ planCode: code })
      });
      return response.status;
    },
    { origin: apiOrigin, planCode }
  );
  expect(result).toBe(204);
}
