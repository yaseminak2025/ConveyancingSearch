import { test, expect } from "@playwright/test";
test("reviewer can search and inspect a firm with keyboard controls", async ({
  page,
}) => {
  await page.route("**/api/search", async (route) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        location: "leeds",
        status: "refreshed",
        lastSuccessAtUtc: "2026-10-25T12:00:00Z",
        lastAttemptAtUtc: "2026-10-25T12:00:00Z",
        warning: null,
        listings: [
          {
            id: "10000000-0000-4000-8000-000000000001",
            details: {
              name: "Example Solicitors",
              address: "1 High Street, Leeds",
              phone: "0113 000 0000",
              website: null,
              contactUrl: null,
              sourceUrl: "https://www.solicitors.com/example.html",
            },
            isActive: true,
            discovery: "new",
            firstSeenAtUtc: "2026-10-25T12:00:00Z",
            lastSeenAtUtc: "2026-10-25T12:00:00Z",
            inactivatedAtUtc: null,
          },
        ],
      }),
    });
  });
  await page.route("**/api/locations", (route) =>
    route.fulfill({ json: [{ key: "leeds", displayName: "Leeds" }] }),
  );
  await page.goto("/");
  await expect(
    page.getByRole("button", { name: "Search cities" }),
  ).toBeEnabled();
  await page.getByLabel("Cities to search").fill("Leeds");
  await page.getByRole("button", { name: "Search cities" }).focus();
  await page.keyboard.press("Enter");
  await expect(
    page.getByRole("heading", { name: "Example Solicitors" }),
  ).toBeVisible();
  await expect(
    page.getByRole("link", { name: "View source listing" }),
  ).toHaveAttribute("href", "https://www.solicitors.com/example.html");
  await page.getByLabel("New listings only").check();
  await expect(
    page.getByRole("heading", { name: "Example Solicitors" }),
  ).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(
    page.getByRole("heading", { name: "Example Solicitors" }),
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
});
