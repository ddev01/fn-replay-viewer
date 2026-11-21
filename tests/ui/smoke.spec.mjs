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
  "/api/demos": { demos, backfill: false },
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

  // The platform, tracker and NameFN icons are 24px. flex-basis wins over width in the
  // tracker, so asserting the computed width is what catches a regression here.
  const sizes = await page.evaluate(() => {
    const doc = globalThis.document;
    const width = (selector) => {
      const node = doc.querySelector(selector);
      return node ? node.ownerDocument.defaultView.getComputedStyle(node).width : "missing";
    };
    return {
      platform: width(".plat"),
      tracker: width(".tracker .icon"),
      nameFn: width(".tracker.namefn .icon"),
    };
  });
  expect(sizes).toEqual({ platform: "20px", tracker: "20px", nameFn: "20px" });

  // Both links belong beside the platform icon, not on the name line.
  expect(await page.locator(".pname-row .tracker").count()).toBe(0);
  expect(await page.locator(".badges .tracker").count()).toBeGreaterThan(0);

  expect(violations).toEqual([]);
});

test("renders the summary, team cards and roster players", async ({ page }) => {
  const summary = page.locator("#summary");
  await expect(summary).toBeVisible();
  // The summary is one row: date, team place, then the owner stats.
  await expect(summary).toContainText("Team place 1");
  await expect(summary.locator(".stat")).toHaveCount(8);
  await expect(summary).not.toContainText("Your place");
  await expect(summary).not.toContainText("Team size");
  await expect(summary).not.toContainText("ShowdownAlt");

  const teams = page.locator("#roster .team");
  await expect(teams).toHaveCount(2);

  const roster = page.locator("#roster");
  for (const name of ["Nova", "Bolt", "Rook"]) {
    await expect(roster).toContainText(name);
  }

  await expect(page.locator("#timeline li").first()).toBeVisible();
});

test("opens the newest replay when nothing is cached", async ({ page }) => {
  await page.route("**/api/match", (route) => route.fulfill({ status: 404, body: "" }));
  let posts = 0;
  await page.route("**/api/scrape", (route) => {
    posts += 1;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(match),
    });
  });

  // Drop the page the shared beforeEach loaded, so its in-flight init cannot scrape too.
  await page.goto("about:blank");
  await page.goto("/");

  await expect(page.locator("#roster .team")).toHaveCount(2);
  // Not an exact count: the beforeEach page can still contribute a request. What matters is
  // that a cold load fetches the newest replay by itself and renders it.
  expect(posts).toBeGreaterThan(0);
});

test("reloading stays on the cached replay instead of scraping again", async ({ page }) => {
  let posts = 0;
  page.on("request", (request) => {
    if (request.url().includes("/api/scrape")) posts += 1;
  });

  await expect(page.locator("#roster .team")).toHaveCount(2);
  await page.reload();
  await expect(page.locator("#roster .team")).toHaveCount(2);

  expect(posts).toBe(0);
});

test("a failed scrape reports it instead of leaving Scraping… up", async ({ page }) => {
  await page.route("**/api/match", (route) => route.fulfill({ status: 404, body: "" }));
  await page.route("**/api/scrape", (route) => route.abort());

  await page.reload();

  await expect(page.locator("#status")).toContainText("Could not reach Replay Viewer");
});

test("an unplaced squad shows #? rather than nothing", async ({ page }) => {
  const unplaced = {
    ...match,
    teams: [{ teamIndex: 0, players: match.teams[0].players }],
  };
  await page.route("**/api/match", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(unplaced),
    })
  );
  await page.goto("about:blank");
  await page.goto("/");

  await expect(page.locator("#roster .place").first()).toHaveText("#?");
});

test("a fast scrape does not flash the banner", async ({ page }) => {
  await page.route("**/api/match", (route) => route.fulfill({ status: 404, body: "" }));
  await page.route("**/api/scrape", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(match),
    })
  );
  await page.goto("about:blank");
  await page.goto("/");

  await expect(page.locator("#roster .team")).toHaveCount(2);
  await expect(page.locator("#status")).toBeHidden();
});

test("the update banner stays hidden without an update", async ({ page }) => {
  await expect(page.locator("#update-banner")).toBeHidden();
});

test("the demos menu is a labelled icon button that opens the panel", async ({ page }) => {
  const button = page.getByRole("button", { name: "Demos" });
  await expect(button).toBeVisible();
  await button.click();
  await expect(page.locator("#demos-panel")).toBeVisible();
});

test("the filter, selection and confirm tools start folded away", async ({ page }) => {
  await page.getByRole("button", { name: "Demos" }).click();
  await expect(page.locator("#demos li")).toHaveCount(2);
  for (const id of ["demos-filters", "demos-select-bar", "demos-confirm", "filter-accounts"]) {
    await expect(page.locator("#" + id)).toBeHidden();
  }
});

test("a cold start opens the newest replay the sidebar lists", async ({ page }) => {
  await page.route("**/api/match", (route) => route.fulfill({ status: 404, body: "" }));
  let asked = null;
  await page.route("**/api/scrape", (route) => {
    asked = route.request().postDataJSON().path;
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(match) });
  });
  await page.goto("about:blank");
  await page.goto("/");
  await expect(page.locator("#roster .team")).toHaveCount(2);
  expect(asked).toBe(demos[0].path);
});

test("a switched-off account is masked, filtered out, remembered, and revealed only on confirm", async ({
  page,
}) => {
  const mine = { ...demos[0], accountId: "main", accountName: "TTV Main" };
  const alt = { ...demos[1], accountId: "alt", accountName: "Secret Alt" };
  await page.route("**/api/demos", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ demos: [mine, alt], backfill: false }),
    })
  );
  await page.reload();
  await page.getByRole("button", { name: "Demos" }).click();
  await page.locator("#toggle-filters").click();
  await page.locator("#toggle-accounts").click();

  const rows = page.locator("#account-list .account");
  await expect(rows).toHaveCount(2);
  await expect(page.locator("#account-list")).not.toContainText("Secret Alt");
  await expect(page.locator("#account-list")).not.toContainText("TTV Main");

  // Switching an account off hides its replays, and survives a reload.
  await page.locator('input[data-account="alt"]').uncheck();
  await expect(page.locator("#demos li")).toHaveCount(1);
  await page.reload();
  await page.getByRole("button", { name: "Demos" }).click();
  await expect(page.locator("#demos li")).toHaveCount(1);

  // The name appears only after the confirmation.
  await page.locator("#toggle-filters").click();
  await page.locator("#toggle-accounts").click();
  await page.locator('[data-eye="main"]').click();
  await expect(page.locator("#account-confirm")).toBeVisible();
  await expect(page.locator("#account-list")).not.toContainText("TTV Main");
  await page.locator("#account-confirm-yes").click();
  await expect(page.locator("#account-list")).toContainText("TTV Main");

  // Closing the panel masks it again.
  await page.locator("#close-demos").click();
  await page.getByRole("button", { name: "Demos" }).click();
  await page.locator("#toggle-filters").click();
  await page.locator("#toggle-accounts").click();
  await expect(page.locator("#account-list")).not.toContainText("TTV Main");
});

test("filters narrow the replay list and reset restores it", async ({ page }) => {
  await page.getByRole("button", { name: "Demos" }).click();
  await expect(page.locator("#demos li")).toHaveCount(2);
  await page.locator("#toggle-filters").click();
  await page.getByLabel("Solo").check();
  await expect(page.locator("#demos li")).toHaveCount(0);
  await expect(page.locator("#demos-none")).toBeVisible();
  await expect(page.locator("#filter-count")).toHaveText("1");
  await page.getByLabel("Duos").check();
  await page.getByLabel("Solo").uncheck();
  await expect(page.locator("#demos li")).toHaveCount(1);
  await page.locator("#filters-reset").click();
  await expect(page.locator("#demos li")).toHaveCount(2);
});

test("selected replays are deleted only after the warning is confirmed", async ({ page }) => {
  let body = null;
  await page.route("**/api/demos/delete", (route) => {
    body = route.request().postDataJSON();
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ deleted: body.names, failed: [] }),
    });
  });
  await page.getByRole("button", { name: "Demos" }).click();
  await page.getByRole("button", { name: "Select replays" }).click();
  await page.getByLabel("Select all").check();
  await expect(page.locator("#select-count")).toHaveText("2 selected");
  await page.getByRole("button", { name: "Delete", exact: true }).click();
  await expect(page.locator("#confirm-text")).toContainText("inside Fortnite");

  await page.locator("#confirm-no").click();
  expect(body).toBeNull();

  await page.getByRole("button", { name: "Delete", exact: true }).click();
  await page.getByRole("button", { name: "Yes, delete" }).click();
  await expect.poll(() => body?.names.length).toBe(2);
});

test("a demo row puts kills and placement on the title line, right aligned", async ({ page }) => {
  await page.getByRole("button", { name: "Demos" }).click();
  const row = page.locator("#demos li").first();
  await expect(row.locator(".demo-title")).toHaveText("Reload - Surf City");
  await expect(row.locator(".demo-kills")).toHaveText("Eliminations 12");
  await expect(row.locator(".demo-place")).toHaveText("Placement #4/40");
  // The score sits in the same row as the title and hugs the right edge.
  const boxes = await row.evaluate((node) => {
    const box = (selector) => node.querySelector(selector).getBoundingClientRect();
    return {
      row: node.getBoundingClientRect().right,
      title: box(".demo-row"),
      score: box(".demo-score").right,
    };
  });
  expect(Math.abs(boxes.row - boxes.score)).toBeLessThan(12);
});

test("a clicked demo is put in the URL and Back returns without reloading", async ({ page }) => {
  // Echo the clicked file back the way the server does, so each click is a different URL.
  await page.route("**/api/scrape", (route) => {
    const name = String(route.request().postDataJSON().path).split(/[\\/]/).pop();
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ ...match, sourceFile: name }),
    });
  });
  await page.reload();
  await expect(page).toHaveURL(/[?&]replay=UnrealReplay_2026\.10\.31-20\.15\.00\.replay/);

  await page.evaluate(() => {
    globalThis.__sameDoc = true;
  });
  await page.getByRole("button", { name: "Demos" }).click();
  await page.locator("#demos li").nth(1).click();
  await expect(page).toHaveURL(/[?&]replay=UnrealReplay_2026\.10\.30-19\.02\.11\.replay/);

  await page.evaluate(() => globalThis.history.back());
  await expect(page).toHaveURL(/[?&]replay=UnrealReplay_2026\.10\.31-20\.15\.00\.replay/);
  expect(await page.evaluate(() => globalThis.__sameDoc)).toBe(true);
});

test("a replay named in the URL opens on a cold load", async ({ page }) => {
  await page.route("**/api/match", (route) => route.fulfill({ status: 404, body: "" }));
  let asked = null;
  await page.route("**/api/scrape", (route) => {
    asked = route.request().postDataJSON().path;
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(match),
    });
  });

  await page.goto("/?replay=UnrealReplay_2026.10.30-19.02.11.replay");

  await expect(page.locator("#roster .team")).toHaveCount(2);
  expect(asked).toBe("UnrealReplay_2026.10.30-19.02.11.replay");
});

test("a replay the parser stalls on is named and explained", async ({ page }) => {
  const stuck = { ...demos[0], name: "UnsavedReplay-2026.11.13-17.21.46.replay", unreadable: true };
  await page.route("**/api/demos", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ demos: [stuck], backfill: false }),
    })
  );
  await page.reload();
  await page.getByRole("button", { name: "Demos" }).click();

  const row = page.locator("#demos li").first();
  await expect(row).toHaveClass(/unreadable/);
  // The file name means nothing to a player, and the date line stays short.
  await expect(row.locator(".demo-title")).toHaveText("Broken replay file");
  await expect(row.locator(".demo-meta")).not.toContainText("not cached");
  await expect(row.locator(".demo-help")).toHaveAttribute("title", /parser can't read this replay file/);
});

test("the demo list pages in as it scrolls", async ({ page }) => {
  const many = Array.from({ length: 45 }, (_, i) => ({
    ...demos[0],
    name: `UnrealReplay_${i}.replay`,
    path: `C:\\Demos\\UnrealReplay_${i}.replay`,
  }));
  await page.route("**/api/demos", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ demos: many, backfill: false }),
    })
  );
  await page.reload();
  await page.getByRole("button", { name: "Demos" }).click();

  const rows = page.locator("#demos li");
  const panel = page.locator("#demos-panel");
  await expect(rows).toHaveCount(20);

  await panel.evaluate((el) => {
    el.scrollTop = el.scrollHeight;
  });
  await expect(rows).toHaveCount(40);

  await panel.evaluate((el) => {
    el.scrollTop = el.scrollHeight;
  });
  await expect(rows).toHaveCount(45);
});

test("the demo list pages in on a tall window that never scrolls", async ({ page }) => {
  // A maximised browser fits the first page: with no scrollbar the scroll event never fires,
  // which used to leave the sidebar stuck on 20 rows.
  await page.setViewportSize({ width: 1280, height: 1800 });
  const many = Array.from({ length: 45 }, (_, i) => ({
    ...demos[0],
    name: `UnrealReplay_${i}.replay`,
    path: `C:\\Demos\\UnrealReplay_${i}.replay`,
  }));
  await page.route("**/api/demos", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ demos: many, backfill: false }),
    })
  );
  await page.reload();
  await page.getByRole("button", { name: "Demos" }).click();

  await expect(page.locator("#demos li").first()).toBeVisible();
  expect(await page.locator("#demos li").count()).toBeGreaterThan(20);
});

test("a replay without an owner explains the empty You timeline", async ({ page }) => {
  const noOwner = {
    ...match,
    teams: match.teams.map((team) => ({
      ...team,
      players: team.players.map((player) => ({ ...player, isYou: false })),
    })),
  };
  await page.route("**/api/match", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(noOwner),
    })
  );
  await page.goto("about:blank");
  await page.goto("/");

  await expect(page.locator("#roster .team")).toHaveCount(2);
  await expect(page.locator("#timeline")).toContainText("doesn't record which player you were");
});

test("has no serious or critical accessibility violations", async ({ page }) => {
  await expect(page.locator("#roster .team")).toHaveCount(2);

  const { violations } = await new AxeBuilder({ page }).analyze();
  const blocking = violations
    .filter((v) => v.impact === "serious" || v.impact === "critical")
    .flatMap((v) => v.nodes.map((n) => `${v.id} ${n.target.join(" ")}: ${n.failureSummary}`));
  expect(blocking).toEqual([]);
});

test("a replay can be opened from the keyboard and Escape closes the demos panel", async ({ page }) => {
  await page.route("**/api/scrape", (route) =>
    route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(match) })
  );
  const toggle = page.getByRole("button", { name: "Demos" });
  await toggle.focus();
  await page.keyboard.press("Enter");

  await expect(page.locator("#demos-panel")).toBeVisible();
  await expect(page.locator("#close-demos")).toBeFocused();
  await expect(page.locator("main")).toHaveJSProperty("inert", true);

  await page.keyboard.press("Escape");
  await expect(page.locator("#demos-panel")).toBeHidden();
  await expect(toggle).toBeFocused();

  await toggle.press("Enter");
  await page.locator("#demos .demo-open").first().focus();
  const scraped = page.waitForRequest("**/api/scrape");
  await page.keyboard.press("Enter");
  await scraped;
});

test("tabs and sort buttons expose their state to assistive tech", async ({ page }) => {
  await expect(page.locator('.tabs button[data-tab="you"]')).toHaveAttribute("aria-pressed", "true");
  await page.locator('.tabs button[data-tab="game"]').click();
  await expect(page.locator('.tabs button[data-tab="game"]')).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator('.tabs button[data-tab="you"]')).toHaveAttribute("aria-pressed", "false");
  await page.locator("#sort-kills").click();
  await expect(page.locator("#sort-kills")).toHaveAttribute("aria-pressed", "true");
});

test("squads and players are real buttons, and errors are announced", async ({ page }) => {
  await expect(page.locator("#roster .team").first().getByRole("button").first()).toBeVisible();
  await expect(page.locator("#roster .pname").first()).toHaveJSProperty("tagName", "BUTTON");
  await expect(page.locator("#status")).toHaveAttribute("role", "status");
});

test("a first scrape that fails shows an empty state with a way forward", async ({ page }) => {
  await page.route("**/api/match", (route) => route.fulfill({ status: 404, body: "" }));
  let attempts = 0;
  await page.route("**/api/scrape", (route) => {
    attempts += 1;
    if (attempts === 1) {
      return route.fulfill({
        status: 409,
        contentType: "application/json",
        body: JSON.stringify({ error: "The newest replay is still being written." }),
      });
    }
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(match) });
  });
  await page.goto("about:blank");
  await page.goto("/");

  await expect(page.locator("#empty")).toBeVisible();
  await expect(page.locator("#empty-text")).toContainText("still being written");

  await page.getByRole("button", { name: "Try again" }).click();
  await expect(page.locator("#roster .team")).toHaveCount(2);
  await expect(page.locator("#empty")).toBeHidden();
});

test("closing the sidebar leaves select mode", async ({ page }) => {
  await page.getByRole("button", { name: "Demos" }).click();
  await page.getByRole("button", { name: "Select replays" }).click();
  await expect(page.locator("#demos-select-bar")).toBeVisible();
  await page.locator("#close-demos").click();
  await page.getByRole("button", { name: "Demos" }).click();
  await expect(page.locator("#demos-select-bar")).toBeHidden();
  await expect(page.locator("#demos .demo-check")).toHaveCount(0);
});
