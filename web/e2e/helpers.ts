import { expect, type Page } from "@playwright/test";

const apiOrigin = "http://localhost:5080";

export async function signInWithEmailCode(page: Page, email: string, password = "password1") {
  await page.goto("/");
  await page.getByLabel("Dirección de email *").fill(email);
  await page.getByLabel("Contraseña *").fill(password);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
  await expect(page.getByLabel("Código *")).toBeVisible({ timeout: 20_000 });
  const peek = await page.evaluate(
    async ({ origin, email: address }) => {
      const response = await fetch(`${origin}/api/dev/email-code?email=${encodeURIComponent(address)}`);
      if (!response.ok) return null;
      return (await response.json()).code as string;
    },
    { origin: apiOrigin, email }
  );
  expect(peek).toBeTruthy();
  await page.getByLabel("Código *").fill(peek!);
  await page.getByRole("button", { name: "Iniciar sesión" }).click();
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
