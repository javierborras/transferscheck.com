import { expect, test } from "@playwright/test";
test("landing reads public prices and leads to the real signup and login", async ({ page, request }) => {
  const response = await request.get("http://localhost:5080/api/plans");
  expect(response.ok()).toBeTruthy();
  const { plans } = await response.json();
  await page.goto("/");
  await expect(page.getByRole("heading", { name: /Confirmá al instante cada transferencia/ })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Casos reales todos los días" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Pack Corporativo" })).toBeVisible();
  await expect(page.locator("#desarrolladores")).toBeVisible();
  await expect(page.locator("#desarrolladores form")).toBeVisible();
  for (const plan of plans) {
    const card = page.locator(".commercial-plan").filter({ has: page.getByRole("heading", { name: plan.displayName, exact: true }) });
    await expect(card.locator(".money")).toHaveText(new Intl.NumberFormat("es-AR", { style: "currency", currency: plan.currency }).format(plan.amount));
  }
  await page.screenshot({ path: "test-results/landing-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: "test-results/landing-mobile.png", fullPage: true });
  await page.getByRole("link", { name: "Ingresar", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Hola de nuevo" })).toBeVisible();
});
