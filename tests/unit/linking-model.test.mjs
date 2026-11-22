// Unit tests for the pure linking logic in wwwroot/linking-model.js. No browser: these run under Node.
import test from "node:test";
import assert from "node:assert/strict";
import match from "../ui/fixtures/match.mjs";
import {
  attemptText,
  flagForDemo,
  indexPlayers,
  linkTooltip,
  normId,
  profileUrl,
  removeLink,
  replayRequest,
  searchUrl,
  upsertLink,
} from "../../src/ReplayViewer/wwwroot/linking-model.js";

const demo = { recordedAt: "2026-10-31T20:15:00+00:00", durationSeconds: 900 };
const start = Date.parse(demo.recordedAt) / 1000;
const lookup = (received_at, flag) => ({ received_at, flag });

test("flagForDemo picks the latest lookup inside the replay window, with slack", () => {
  const lookups = [
    lookup(start + 100, "none_found"),
    lookup(start + 500, "weak_only"),
    lookup(start - 3600, "ok"),
  ];
  assert.equal(flagForDemo(demo, lookups), "weak_only");
  // Ten minutes either side still counts; an hour does not.
  assert.equal(flagForDemo(demo, [lookup(start + 900 + 590, "none_found")]), "none_found");
  assert.equal(flagForDemo(demo, [lookup(start - 3600, "none_found")]), null);
  assert.equal(flagForDemo({ ...demo, recordedAt: undefined }, lookups), null);
  assert.equal(flagForDemo({ recordedAt: "nonsense" }, lookups), null);
});

test("replayRequest sends the session id, non-bot epic ids and the match time", () => {
  const body = replayRequest(match);
  assert.equal(body.game_session_id, match.gameSessionId);
  assert.equal(body.played_at, Date.parse(match.recordedAt) / 1000);
  assert.equal(body.epic_ids.length, 3);

  const noSession = replayRequest({ ...match, gameSessionId: undefined, recordedAt: undefined });
  assert.equal(noSession.game_session_id, null);
  assert.equal(noSession.played_at, null);
});

test("ids compare without hyphens or case", () => {
  assert.equal(normId("8A1F2B3C-4D5E-6F70-8192-A3B4C5D6E7F8"), "8a1f2b3c4d5e6f708192a3b4c5d6e7f8");
  assert.equal(normId(undefined), "");
});

test("one link per platform: upsert replaces, remove deletes", () => {
  const players = indexPlayers([{ epic_id: "AB-CD", name: null, links: [], attempts: [] }]);
  upsertLink(players, { platform: "twitch", handle: "a", source: "clipper_manual" }, "abcd");
  upsertLink(players, { platform: "twitch", handle: "b", source: "clipper_manual" }, "ABCD");
  upsertLink(players, { platform: "tiktok", handle: "c", source: "clipper_manual" }, "abcd");
  assert.deepEqual(
    players.get("abcd").links.map((l) => l.handle),
    ["b", "c"]
  );
  removeLink(players, "abcd", "twitch");
  assert.deepEqual(
    players.get("abcd").links.map((l) => l.handle),
    ["c"]
  );
  // A player the server did not list still gets an entry.
  upsertLink(players, { platform: "twitch", handle: "z", source: "clipper_manual" }, "ffff");
  assert.equal(players.get("ffff").links.length, 1);
});

test("search links are encoded and attempts read as a sentence", () => {
  assert.equal(searchUrl("twitch", "a b&c"), "https://www.twitch.tv/search?term=a%20b%26c");
  assert.equal(searchUrl("tiktok", "x"), "https://www.tiktok.com/search/user?q=x");
  assert.equal(
    attemptText({
      platform: "twitch",
      query: "ttvbob",
      variant: "tag_kept",
      match: "exact",
      status: "not_found",
      weak: true,
    }),
    "Twitch ttvbob · tag kept · exact · not found · weak"
  );
});

test("link tooltips say how the link was made", () => {
  const t = (source) => linkTooltip({ platform: "tiktok", handle: "a.b", source });
  assert.equal(t("clipper_manual"), "TikTok: a.b (manual)");
  assert.equal(t("tracker_claimed"), "TikTok: a.b (claimed on Tracker)");
  assert.equal(t("whatever"), "TikTok: a.b (automatic)");
});

test("profile links exist only for safe handles", () => {
  assert.equal(profileUrl("twitch", "Nova_1"), "https://www.twitch.tv/Nova_1");
  assert.equal(profileUrl("tiktok", "a.b"), "https://www.tiktok.com/@a.b");
  assert.equal(profileUrl("twitch", "a/b"), null);
  assert.equal(profileUrl("twitch", ""), null);
  assert.equal(profileUrl("twitch", "a".repeat(41)), null);
  assert.equal(profileUrl("youtube", "ok"), null);
});
