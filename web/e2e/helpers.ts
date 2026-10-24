import { expect, type Page } from "@playwright/test";

const apiOrigin = "http://localhost:5080";

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
