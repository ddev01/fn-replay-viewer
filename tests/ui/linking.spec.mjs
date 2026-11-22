// UI tests for the owner-only streamer linking, against the contract's canned responses.
import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import config from "./fixtures/config.mjs";
import match from "./fixtures/match.mjs";
import { linkedDemos, lookups, replay, savedLink } from "./fixtures/linking.mjs";

const NOVA = "8a1f2b3c4d5e6f708192a3b4c5d6e7f8";
const BOLT = "1b2c3d4e5f60718293a4b5c6d7e8f901";
const ROOK = "2c3d4e5f60718293a4b5c6d7e8f90112";

/**
 * Serves the page's API and records every request to /api/linking. `overrides` replaces the
 * response of one linking route: { "POST /api/linking/links": { status, body } }.
 */
async function open(page, { linking, overrides = {} }) {
  const calls = [];
  await page.route("**/api/**", (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const key = `${request.method()} ${path}`;
    if (path.startsWith("/api/linking/")) {
      calls.push({ key, body: request.postData() ? request.postDataJSON() : null });
    }
    const json = (body, status = 200) =>
      route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (overrides[key]) return json(overrides[key].body, overrides[key].status);
    const table = {
      "GET /api/config": { ...config, linking },
      "GET /api/demos": { demos: linkedDemos, backfill: false },
      "GET /api/match": match,
      "GET /api/linking/lookups": lookups,
      "POST /api/linking/replay": replay,
      "POST /api/linking/links": savedLink,
    };
    if (key === "DELETE /api/linking/links" || key === "POST /api/linking/rejections") {
      return route.fulfill({ status: 204 });
    }
    return Object.hasOwn(table, key) ? json(table[key]) : route.fulfill({ status: 404, body: "" });
  });
  await page.goto("/");
  await expect(page.locator("#roster .team")).toHaveCount(2);
  return calls;
}

const row = (page, name) => page.locator(".player", { hasText: name });
const pencil = (page, name) => page.getByRole("button", { name: `Streamer links for ${name}` });

test("with linking off there are no linking elements and no linking request", async ({ page }) => {
  const calls = await open(page, { linking: false });
  await page.locator("#toggle-demos").click();
  await expect(page.locator("#demos li")).toHaveCount(2);

  await expect(page.locator(".link-edit, .linkinfo, .link-flag, .link-dialog")).toHaveCount(0);
  expect(calls).toEqual([]);
});

test("opening a replay asks once and shows links and F9 attempts per player", async ({ page }) => {
  const calls = await open(page, { linking: true });
  await expect(row(page, "Nova").locator(".lchip.link")).toHaveText(/Twitch nova\s+manual/);
  await expect(row(page, "Bolt").locator(".lchip.attempt")).toHaveCount(2);
  await expect(row(page, "Bolt").locator(".lchip.attempt.weak")).toContainText(
    "Twitch bolt · stripped · exact · live · weak"
  );

  const asked = calls.filter((c) => c.key === "POST /api/linking/replay");
  expect(asked).toHaveLength(1);
  expect(asked[0].body.game_session_id).toBe(match.gameSessionId);
  expect(asked[0].body.played_at).toBe(Math.floor(Date.parse(match.recordedAt) / 1000));
  // Bots are never sent; the others go as the replay has them.
  expect(asked[0].body.epic_ids.sort()).toEqual([BOLT, ROOK, NOVA].sort());
});

test("the sidebar marks replays with no streamer or only weak guesses", async ({ page }) => {
  await open(page, { linking: true });
  await page.locator("#toggle-demos").click();

  // The older replay is flagged from the lookup list; the open one from the replay call.
  await expect(page.locator("#demos .link-flag.none_found")).toHaveCount(1);
  await expect(page.locator("#demos .link-flag.weak_only")).toHaveCount(1);
  await expect(page.locator("#demos .link-flag.none_found")).toHaveAttribute(
    "aria-label",
    "F9 found no streamer in this match"
  );
  await expect(page.locator("#demos .link-flag.weak_only")).toHaveAttribute(
    "title",
    "F9 only found weak streamer guesses in this match"
  );
});

test("the pencil opens a panel with search links and a paste box", async ({ page }) => {
  await open(page, { linking: true });
  await pencil(page, "Bolt").click();

  const dialog = page.getByRole("dialog", { name: "Streamer links: Bolt" });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel("Correct Twitch or TikTok URL")).toBeFocused();
  const twitch = dialog.getByRole("link", { name: "Search Twitch for Bolt" });
  await expect(twitch).toHaveAttribute("href", "https://www.twitch.tv/search?term=Bolt");
  await expect(twitch).toHaveAttribute("target", "_blank");
  await expect(twitch).toHaveAttribute("rel", /noopener/);
  await expect(dialog.getByRole("link", { name: "Search TikTok for Bolt" })).toHaveAttribute(
    "href",
    "https://www.tiktok.com/search/user?q=Bolt"
  );
  await expect(dialog).toContainText("ttvbolt · tag kept");

  const { violations } = await new AxeBuilder({ page }).analyze();
  expect(violations.filter((v) => v.impact === "serious" || v.impact === "critical")).toEqual([]);

  await page.keyboard.press("Escape");
  await expect(dialog).toBeHidden();
  await expect(pencil(page, "Bolt")).toBeFocused();
});

test("a pasted URL is saved and the row updates at once", async ({ page }) => {
  const calls = await open(page, { linking: true });
  await pencil(page, "Bolt").click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Correct Twitch or TikTok URL").fill("https://www.twitch.tv/Bolt");
  await dialog.getByRole("button", { name: "Save link" }).click();

  await expect(dialog.locator("#link-msg")).toHaveText("Saved Twitch bolt.");
  await expect(dialog.locator(".link-rows").first()).toContainText("bolt");
  await expect(row(page, "Bolt").locator(".lchip.link")).toHaveText(/Twitch bolt\s+manual/);
  expect(calls.find((c) => c.key === "POST /api/linking/links").body).toEqual({
    epic_id: BOLT,
    url: "https://www.twitch.tv/Bolt",
    epic_name: "Bolt",
  });
});

test("a refused URL shows the server's message", async ({ page }) => {
  await open(page, {
    linking: true,
    overrides: {
      "POST /api/linking/links": {
        status: 400,
        body: { error: "Only twitch.tv/<login> and tiktok.com/@<handle> URLs work." },
      },
    },
  });
  await pencil(page, "Bolt").click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("Correct Twitch or TikTok URL").fill("https://example.com/x");
  await dialog.getByRole("button", { name: "Save link" }).click();

  await expect(dialog.getByRole("alert")).toHaveText(
    "Only twitch.tv/<login> and tiktok.com/@<handle> URLs work."
  );
  // Nothing was stored, and the text is shown as text, never parsed as markup.
  await expect(row(page, "Bolt").locator(".lchip.link")).toHaveCount(0);
  await expect(dialog.locator("#link-msg *")).toHaveCount(0);
});

test("a link can be deleted", async ({ page }) => {
  const calls = await open(page, { linking: true });
  await pencil(page, "Nova").click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: "Delete Twitch link nova" }).click();

  await expect(row(page, "Nova").locator(".lchip.link")).toHaveCount(0);
  await expect(dialog).toContainText("No link yet.");
  expect(calls.find((c) => c.key === "DELETE /api/linking/links").body).toEqual({
    epic_id: NOVA,
    platform: "twitch",
  });
});

test("a wrong guess is rejected for that player", async ({ page }) => {
  const calls = await open(page, { linking: true });
  await pencil(page, "Bolt").click();
  const dialog = page.getByRole("dialog");
  await dialog.getByRole("button", { name: /Wrong guess: Twitch bolt/ }).click();

  await expect(dialog).toContainText("rejected");
  expect(calls.find((c) => c.key === "POST /api/linking/rejections").body).toEqual({
    epic_id: BOLT,
    platform: "twitch",
    handle: "bolt",
  });
});
