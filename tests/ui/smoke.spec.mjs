// UI smoke tests: the page is served statically from src/ReplayViewer/wwwroot and
// the C# API is stubbed with fixtures written from MatchTypes.cs + HttpJson.cs.
// The WinForms app is deliberately not started: the JSON contract is covered by
// the .NET tests, and booting a tray app is not CI-reliable.
import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import config from "./fixtures/config.mjs";
import demos from "./fixtures/demos.mjs";
import match from "./fixtures/match.mjs";

const fixtures = {
  "/api/config": config,
  "/api/demos": demos,
  "/api/match": match,
};

test.beforeEach(async ({ page }) => {
  await page.route("**/api/**", (route) => {
    const path = new URL(route.request().url()).pathname;
    if (!Object.hasOwn(fixtures, path)) {
      return route.fulfill({ status: 404, body: "" });
    }
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(fixtures[path]),
    });
  });
  await page.goto("/");
});

test("themed icons are masked, not painted as solid blocks", async ({ page }) => {
  const violations = [];
  page.on("console", (message) => {
    if (message.text().includes("Content Security Policy")) {
      violations.push(message.text());
    }
  });
  await page.reload();
  await expect(page.locator("#roster .team")).toHaveCount(2);

  // An inline style attribute holding --icon is blocked by style-src 'self', which leaves
  // mask-image: none and the element paints a solid currentColor block instead of the icon.
  const masks = await page
    .locator(".icon.themed")
    .evaluateAll((nodes) =>
      nodes.map((node) => node.ownerDocument.defaultView.getComputedStyle(node).maskImage)
    );
  expect(masks.length).toBeGreaterThan(0);
  expect(masks.filter((mask) => mask === "none")).toEqual([]);

  expect(violations).toEqual([]);
});

test("renders the summary, team cards and roster players", async ({ page }) => {
  const summary = page.locator("#summary");
  await expect(summary).toBeVisible();
  await expect(summary).toContainText("ShowdownAlt Solo");
  await expect(summary).toContainText("Your place 1");

  const teams = page.locator("#roster .team");
  await expect(teams).toHaveCount(2);

  const roster = page.locator("#roster");
  for (const name of ["Nova", "Bolt", "Rook"]) {
    await expect(roster).toContainText(name);
  }

  await expect(page.locator("#timeline li").first()).toBeVisible();
});

test("has no serious or critical accessibility violations", async ({ page }) => {
  await expect(page.locator("#roster .team")).toHaveCount(2);

  const { violations } = await new AxeBuilder({ page }).analyze();
  const blocking = violations
    .filter((v) => v.impact === "serious" || v.impact === "critical")
    .flatMap((v) => v.nodes.map((n) => `${v.id} ${n.target.join(" ")}: ${n.failureSummary}`));
  expect(blocking).toEqual([]);
});
